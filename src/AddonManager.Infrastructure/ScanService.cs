using System.Diagnostics;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Infrastructure;

/// <summary>
/// Runs discovery and the content scan as one operation (spec 7.2-7.3). At most one run is in progress; calls that arrive meanwhile
/// share one queued follow-up run. Never saves <see cref="UserState"/> and never touches Community.
/// </summary>
public sealed class ScanService(IPackageContentReader reader, IScanCache cache, string communityPath, ISimManagedPackages simManaged) : IDisposable
{
    /// <param name="FixedSections">The names of Packages whose menu section is fixed; every other Package is read again on each run.</param>
    /// <param name="CardedPaths">The memo paths of the Packages that are on a card.</param>
    private sealed record Snapshot(IReadOnlyList<LibraryRoot> Libraries, ContentSettings Settings, IReadOnlySet<PackageName> FixedSections, IReadOnlyList<string> CardedPaths);

    private sealed class Request(bool full, Snapshot snapshot, ScanCallbacks callbacks)
    {
        public bool Full { get; set; } = full;

        public Snapshot Snapshot { get; set; } = snapshot;

        public ScanCallbacks Callbacks { get; set; } = callbacks;

        public TaskCompletionSource<ScanOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly ScanCallbacks NoCallbacks = new();

    private readonly object gate = new();
    // Never disposed: a run may still read its token when Dispose is called, and without a timer there is nothing to release.
    private readonly CancellationTokenSource disposal = new();
    private bool running;
    private bool disposed;
    private Request? queued;
    private Request? current;

    /// <summary>
    /// Starts a run, or joins the queued follow-up when one is in progress. The input is snapshotted before this returns, so the caller can keep changing
    /// <paramref name="state"/>. <paramref name="cancellationToken"/> only ends this caller's wait; a run is cancelled by <see cref="Dispose"/> alone.
    /// </summary>
    public Task<ScanOutcome> RunAsync(UserState state, bool full, ScanCallbacks? callbacks = null, CancellationToken cancellationToken = default)
    {
        var snapshot = new Snapshot(
            [.. state.Libraries],
            state.Content.Clone(),
            state.Packages.Where(p => p.Value.Section is not null).Select(p => p.Key).ToHashSet(),
            [.. state.Packages.Values.Where(p => p.CardId is not null && p.Memo is not null).Select(p => p.Memo!.Path)]);
        var request = new Request(full, snapshot, callbacks ?? NoCallbacks);
        Task<ScanOutcome> task;

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (!running)
            {
                running = true;
                StartRun(request);
                task = request.Completion.Task;
            }
            else
            {
                // The newest state wins; a full scan requested by anyone is a full scan.
                queued ??= request;
                queued.Full |= full;
                queued.Snapshot = snapshot;
                queued.Callbacks = request.Callbacks;
                task = queued.Completion.Task;
            }
        }

        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    /// <summary>Cancels the run in progress and drops the queued one; both of their tasks end cancelled.</summary>
    public void Dispose()
    {
        Request? dropped;
        Request? interrupted;
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            dropped = queued;
            interrupted = current;
            queued = null;
        }

        disposal.Cancel();
        dropped?.Completion.TrySetCanceled();
        // A run past its last cancellation check would otherwise still complete with a result; the later TrySetResult then fails silently.
        interrupted?.Completion.TrySetCanceled();
    }

    private void StartRun(Request request)
    {
        current = request;      // every caller holds gate
        _ = Task.Run(() =>
        {
            ScanOutcome? outcome = null;
            Exception? failure = null;
            try
            {
                outcome = Run(request, disposal.Token);
            }
            catch (Exception e)
            {
                failure = e;
            }

            // Release the slot before completing, so a caller woken by this run can already start the next one.
            lock (gate)
            {
                running = false;
                current = null;
                if (queued is { } next && !disposed)
                {
                    queued = null;
                    running = true;
                    StartRun(next);
                }
            }

            Complete(request, outcome, failure);
        });
    }

    private static void Complete(Request request, ScanOutcome? outcome, Exception? failure)
    {
        switch (failure)
        {
            case null:
                request.Completion.TrySetResult(outcome!);
                break;
            case OperationCanceledException:
                request.Completion.TrySetCanceled();
                break;
            default:
                request.Completion.TrySetException(failure);
                break;
        }
    }

    private ScanOutcome Run(Request request, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var (libraries, settings, fixedSections, cardedPaths) = request.Snapshot;
        var callbacks = request.Callbacks;

        // The only cache read of the run: startup results, provisional categories and the scan all use these rows.
        var cached = cache.LoadAll();
        if (callbacks.OnCached is { } onCached)
            onCached(cached, ContentAnalyzer.Analyze(cached, settings));
        cancellationToken.ThrowIfCancellationRequested();

        var discovered = LibraryScanner.Discover(libraries);
        var index = new PackageIndex(discovered.Packages, libraries, discovered.Unreachable, discovered.Inaccessible);
        var community = CommunityClassifier.Classify(new CommunityReader(communityPath).Read(), index);
        var targets = ScanTargetCollector.Collect(community, communityPath, index, simManaged);
        var discovery = new DiscoveryOutcome(
            index, community, targets, ProvisionalCategories(targets, cached, cancellationToken),
            PresentOutside(cardedPaths, libraries, cancellationToken), discovered.Inaccessible);
        callbacks.OnDiscovered?.Invoke(discovery);

        // A Package without a fixed section may still be settling, or has never been read settled: read it again every run. Sim-managed Packages never get a section.
        var forceRead = targets
            .Where(t => t.Source != PackageSource.SimManaged && !fixedSections.Contains(t.Name))
            .Select(t => t.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scanner = new ContentScanner(reader, cache);
        var content = scanner.Scan(targets, cached, [.. discovered.Unreachable.Select(l => l.Path)], request.Full, forceRead, callbacks.Progress, cancellationToken);
        var analysis = ContentAnalyzer.Analyze(content.Contents, settings);
        return new ScanOutcome(discovery, content, analysis, watch.Elapsed);
    }

    // Paths under any registered Library (also an offline one) are the Library's business; the rest are checked on disk.
    private static SortedSet<string> PresentOutside(IReadOnlyList<string> cardedPaths, IReadOnlyList<LibraryRoot> libraries, CancellationToken cancellationToken)
    {
        var present = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in cardedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalised = PathNormalizer.Normalize(path);
            if (!libraries.Any(l => PathNormalizer.IsUnder(normalised, l.Path)) && Directory.Exists(normalised))
                present.Add(normalised);
        }

        return present;
    }

    // Sim-managed Packages are never switched, so their provisional Category isn't needed.
    private Dictionary<string, Category> ProvisionalCategories(IReadOnlyList<ScanTarget> targets, IReadOnlyList<PackageContent> cached, CancellationToken cancellationToken)
    {
        var hasBglByPath = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in cached)
            hasBglByPath[PathNormalizer.Normalize(row.Path)] = row.HasBgl;

        var categories = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets.Where(t => t.Source != PackageSource.SimManaged))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hasBgl = hasBglByPath.TryGetValue(target.Path, out var known) ? known : HasBglOrUnknown(target.Path);
            categories[target.Path] = CategoryRules.Provisional(target.Manifest.ContentType, hasBgl);
        }

        return categories;
    }

    private bool? HasBglOrUnknown(string path)
    {
        try
        {
            return reader.HasBgl(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
