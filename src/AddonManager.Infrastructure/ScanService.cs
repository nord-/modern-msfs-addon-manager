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
    private sealed record Snapshot(IReadOnlyList<LibraryRoot> Libraries, ContentSettings Settings);

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
        var snapshot = new Snapshot([.. state.Libraries], state.Content.Clone());
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
        var (libraries, settings) = request.Snapshot;
        var callbacks = request.Callbacks;

        // The only cache read of the run: startup results, provisional categories and the scan all use these rows.
        var cached = cache.LoadAll();
        if (callbacks.OnCached is { } onCached)
            onCached(ContentAnalyzer.Analyze(cached, settings));
        cancellationToken.ThrowIfCancellationRequested();

        var discovered = LibraryScanner.Discover(libraries);
        var index = new PackageIndex(discovered.Packages, libraries, discovered.Unreachable);
        var community = CommunityClassifier.Classify(new CommunityReader(communityPath).Read(), index);
        var targets = ScanTargetCollector.Collect(community, communityPath, index, simManaged);
        var discovery = new DiscoveryOutcome(index, community, targets, ProvisionalCategories(targets, cached, cancellationToken));
        callbacks.OnDiscovered?.Invoke(discovery);

        var scanner = new ContentScanner(reader, cache);
        var content = scanner.Scan(targets, cached, [.. discovered.Unreachable.Select(l => l.Path)], request.Full, callbacks.Progress, cancellationToken);
        var analysis = ContentAnalyzer.Analyze(content.Contents, settings);
        return new ScanOutcome(discovery, content, analysis, watch.Elapsed);
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
