using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.FirstRun;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Core.Sync;
using AddonManager.Infrastructure;
using AddonManager.Sim2020;

namespace AddonManager.App.Session;

/// <summary>
/// The WPF-free heart of the app: startup, first run, scans, adopting Community at every discovery and watching (plans 5a and 5b). One lock guards the state, the
/// phase, the current view and the watcher reference. It is held while a run is requested and while its discovery and completion are
/// handled, never while a watcher is disposed or <see cref="Changed"/> is raised. The UI thread never takes it while a run can hold it:
/// <see cref="RescanAsync"/> and <see cref="CompleteFirstRunAsync"/> move to the thread pool first.
/// </summary>
public sealed partial class AppSession(Func<AppServices> openServices, Func<SimPaths?> locate, string gsxFolder) : IAppSession, IDisposable
{
    private readonly object gate = new();
    private readonly UserState emptyState = new();
    private AppServices? services;
    private SessionView view = new(SessionPhase.Starting, null, null, null);
    private IContentWatcher? watcher;
    private IReadOnlyList<CommunityEntryInfo>? community;
    private string? pickedRoot;
    private DiscoveryOutcome? latestDiscovery;
    private BoardInput? latestInput;
    private string? repairError;
    private ScanOutcome? lastHandled;
    private Task<ScanOutcome>? latestTask;
    private IReadOnlyList<PackageContent> cachedContents = [];
    private ContentAnalysis? cachedAnalysis;
    private bool unsaved;
    private volatile bool disposed;
    private int handledOutcomes;
    private int requestedRuns;

    public event Action<SessionView>? Changed;

    public SessionView Current => Volatile.Read(ref view);

    public UserState State => services?.State ?? emptyState;

    public SimPaths? Paths => services?.Paths;

    public string DatabaseFile => services?.Database.FilePath ?? "";

    /// <summary>How many completed runs were settled; a test checks that merged callers settle one run once.</summary>
    internal int HandledOutcomes => Volatile.Read(ref handledOutcomes);

    /// <summary>How many run requests reached the scan service; a test waits on it until merged callers have all joined.</summary>
    internal int RequestedRuns => Volatile.Read(ref requestedRuns);

    public Task StartAsync() => Task.Run(StartCoreAsync);

    public IReadOnlyList<CommunityEntryInfo> ReadCommunity()
    {
        lock (gate)
        {
            if (community is not null)
                return community;
            if (services?.Paths is null)
                return [];

            try
            {
                return community = services.ReadCommunity();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    public bool TryUsePackagesRoot(string root, out string? refusal)
    {
        SessionView? raise;
        lock (gate)
        {
            var paths = services is not null && view.Phase is SessionPhase.NeedsPackagesRoot or SessionPhase.FirstRun ? InstallLocator.FromPackagesRoot(root) : null;
            if (paths is null)
            {
                refusal = "This folder has no Community folder. Pick the folder that holds Community and Official.";
                return false;
            }

            refusal = null;
            pickedRoot = root;
            UseSim(paths);
            raise = Publish(new SessionView(SessionPhase.FirstRun, null, null, null));
        }

        Raise(raise);
        return true;
    }

    public Task CompleteFirstRunAsync(IReadOnlyList<LibraryRoot> libraries) => Task.Run(async () =>
    {
        SessionView? raise;
        bool proceed;
        string? error = null;
        lock (gate)
        {
            if (disposed)
                return;
            if (view.Phase != SessionPhase.FirstRun || libraries.Count == 0)
                throw new InvalidOperationException("The first run is not waiting for Libraries, or no Library was given.");

            var state = State;
            var oldLibraries = state.Libraries;
            var oldRoot = state.PackagesRoot;
            try
            {
                using var scope = services!.Store.BeginChange();
                state.Libraries = [.. libraries];
                if (pickedRoot is not null)
                    state.PackagesRoot = pickedRoot;
                services.Store.Save(state);
            }
            catch (UserDataException e)
            {
                state.Libraries = oldLibraries;
                state.PackagesRoot = oldRoot;
                error = e.Message;
            }

            proceed = error is null;
            raise = Publish(new SessionView(proceed ? SessionPhase.Scanning : SessionPhase.FirstRun, null, null, error));
        }

        Raise(raise);
        if (proceed)
            await RunAsync(false).ConfigureAwait(false);
    });

    public Task RescanAsync(bool full) => Task.Run(() => RunAsync(full));

    public bool IsSimRunning => services?.Links?.Sim.IsRunning ?? false;

    public void Dispose()
    {
        IContentWatcher? oldWatcher;
        AppServices? oldServices;
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            oldWatcher = watcher;
            watcher = null;
            oldServices = services;
        }

        oldWatcher?.Dispose();
        oldServices?.Dispose();
    }

    private async Task StartCoreAsync()
    {
        try
        {
            var opened = openServices();
            lock (gate)
            {
                if (disposed)
                {
                    // Nobody is left to use it; disposed below, outside the lock.
                }
                else
                {
                    services = opened;
                }
            }

            if (disposed)
            {
                opened.Dispose();
                return;
            }

            var state = opened.State;
            var firstRun = StartupRules.IsFirstRun(state);
            var storedRoot = state.PackagesRoot;
            var paths = storedRoot is not null ? InstallLocator.FromPackagesRoot(storedRoot) : locate();
            var missingCommunity = paths is not null && !Directory.Exists(paths.CommunityPath);

            SessionView? raise;
            var scan = false;
            lock (gate)
            {
                if (disposed)
                    return;

                if (paths is null || missingCommunity)
                {
                    raise = Publish(firstRun
                        ? new SessionView(SessionPhase.NeedsPackagesRoot, null, null, null)
                        : new SessionView(SessionPhase.Failed, null, null, NotFoundMessage(storedRoot, paths)));
                }
                else
                {
                    UseSim(paths);
                    scan = !firstRun;
                    raise = Publish(new SessionView(firstRun ? SessionPhase.FirstRun : SessionPhase.Scanning, null, null, null));
                }
            }

            Raise(raise);
            if (scan)
                await RunAsync(false).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            FailStart(e is UserDataException ? e.Message : $"Startup failed: {e.Message}");
        }
    }

    private static string NotFoundMessage(string? storedRoot, SimPaths? paths)
    {
        if (storedRoot is not null)
            return $"The stored Packages root {storedRoot} has no Community folder.";
        return paths is null
            ? "The MSFS 2020 install was not found: there is no UserCfg.opt with InstalledPackagesPath."
            : $"The MSFS 2020 install points to {paths.PackagesPath}, but {paths.CommunityPath} does not exist.";
    }

    private void FailStart(string message)
    {
        SessionView? raise;
        lock (gate)
        {
            if (disposed)
                return;
            raise = Publish(new SessionView(SessionPhase.Failed, null, null, message));
        }

        Raise(raise);
    }

    /// <summary>Attaches the sim to the services unless a test already did. Caller holds the lock.</summary>
    private void UseSim(SimPaths paths)
    {
        var current = services!;
        if (current.Scans is null || current.Paths != paths)
            current.UseSim(paths, gsxFolder);
        community = null;
    }

    /// <summary>Starts a run and settles it. Never throws.</summary>
    private async Task RunAsync(bool full)
    {
        Task<ScanOutcome>? task = null;
        try
        {
            SessionView? raise = null;
            lock (gate)
            {
                if (disposed || services?.Scans is not { } scans || !CanRun)
                    return;

                if (view.Phase == SessionPhase.Failed)
                    raise = Publish(new SessionView(SessionPhase.Scanning, null, null, null));
                task = latestTask = scans.RunAsync(State, full, new ScanCallbacks(OnCached, OnDiscovered, new ProgressSink(this)));
                Interlocked.Increment(ref requestedRuns);
            }

            Raise(raise);
            var outcome = await task.ConfigureAwait(false);
            Complete(outcome);
        }
        catch (Exception e)
        {
            Fail(e, task);
        }
    }

    /// <summary>A run may start while scanning or ready, and from a Failed that a run caused; a Failed from startup is final.</summary>
    private bool CanRun => view.Phase is SessionPhase.Scanning or SessionPhase.Ready || view is { Phase: SessionPhase.Failed, CanRetry: true };

    private void OnCached(IReadOnlyList<PackageContent> contents, ContentAnalysis analysis)
    {
        lock (gate)
        {
            cachedContents = contents;
            cachedAnalysis = analysis;
        }
    }

    private void OnDiscovered(DiscoveryOutcome discovery)
    {
        SessionView? raise = null;
        lock (gate)
        {
            if (disposed)
                return;

            latestDiscovery = discovery;
            // Changes during a long first scan must be seen, and a run that fails right after still needs a watcher to retry it.
            watcher ??= NewWatcher(discovery);

            var state = State;
            var links = services!.Links!;
            var input = latestInput = discovery.ToBoardInput(cachedContents, cachedAnalysis!, GsxFolderReader.Read(gsxFolder));

            // Community is adopted at every discovery, the first run's too, and that needs a board even when the wall is not shown yet (D4).
            StartupOutcome started;
            try
            {
                started = links.Sync.Start(state, discovery.Index, BuildBoard(input));
            }
            catch
            {
                // Start may have changed the state before its save failed; the next step saves it again.
                unsaved = true;
                throw;
            }

            // Start changed the state (adoption) and Community (repairs), so the board is built again from Community as it is now.
            var board = BuildBoard(input);
            repairError = started.Repaired is { Failed.Count: > 0 } repaired ? Describe(repaired.Failed) : null;

            var next = view with { LinksWaiting = started.RepairsWaiting, Error = repairError };
            if (StartupRules.ShowsWallAtDiscovery(state, cachedContents.Count))
                next = next with { Phase = SessionPhase.Ready, Board = board, CanRetry = false };
            if (!Equals(next, view))
                raise = Publish(next);
        }

        Raise(raise);
    }

    private void Complete(ScanOutcome outcome)
    {
        SessionView? raise;
        IContentWatcher? replaced;
        IContentWatcher current;
        lock (gate)
        {
            // An older run's completion can arrive after a newer run's discovery, and callers merged into one run all await the same outcome.
            if (disposed || !ReferenceEquals(outcome.Discovery, latestDiscovery) || ReferenceEquals(outcome, lastHandled))
                return;

            lastHandled = outcome;
            Interlocked.Increment(ref handledOutcomes);
            var state = State;
            var input = latestInput = outcome.ToBoardInput();
            SaveStep(() => Settlement.Apply(state, Reanalyzed(input), DateTimeOffset.UtcNow));
            // The waiting line and the repair error outlive the run's completion.
            raise = Publish(new SessionView(SessionPhase.Ready, BuildBoard(input), null, repairError, LinksWaiting: view.LinksWaiting));

            replaced = watcher;
            current = watcher = NewWatcher(outcome.Discovery);
            if (outcome.Content.SettleAt is { } settleAt)
                current.ScheduleRescan(settleAt);
        }

        // A watcher's Dispose waits for a raise in flight, so it is never disposed under the lock.
        // Once disposed it can no longer change; an event it had not raised yet would otherwise be lost, and the new watcher started after it.
        if (replaced is not null)
        {
            replaced.Dispose();
            if (replaced.HasPending)
            {
                // A later completion may already have replaced the captured watcher, so the live one is asked; it never raises synchronously.
                lock (gate)
                {
                    if (!disposed)
                        watcher?.ScheduleRescan(DateTimeOffset.UtcNow);
                }
            }
        }

        Raise(raise);
    }

    private void Fail(Exception e, Task<ScanOutcome>? task)
    {
        SessionView? raise;
        lock (gate)
        {
            // After Dispose a cancelled run or a disposed database is expected; a failure of an older run says nothing about the newer one.
            if (disposed || (task is not null && !ReferenceEquals(task, latestTask)))
                return;

            var error = e is UserDataException ? e.Message : $"The scan failed: {e.Message}";
            // A run that failed before its discovery has no watcher yet, and the retry hint promises one.
            if (watcher is null && services?.Scans is not null)
                watcher = NewWatcher(null);
            raise = Publish(view.Board is null
                ? new SessionView(SessionPhase.Failed, null, null, error, CanRetry: true, LinksWaiting: view.LinksWaiting)
                : new SessionView(SessionPhase.Ready, view.Board, null, error, LinksWaiting: view.LinksWaiting));
        }

        Raise(raise);
    }

    /// <summary>
    /// Opens the change scope first, so a locked file refuses the step before <c>State</c> changes and the next run repeats it. Saves when the
    /// step changed something, or when an earlier save failed after the state had already changed. Caller holds the lock.
    /// </summary>
    private void SaveStep(Func<bool> step)
    {
        var store = services!.Store;
        var scope = store.BeginChange();
        try
        {
            try
            {
                if (step() || unsaved)
                    store.Save(State);
            }
            finally
            {
                scope.Dispose();
            }

            unsaved = false;
        }
        catch
        {
            // The state may already differ from the file; the scope rolled back, so the next step saves again.
            unsaved = true;
            throw;
        }
    }

    /// <summary>"1 link could not be repaired: message", "3 links could not be repaired: message" (the first failure's message).</summary>
    private static string Describe(IReadOnlyList<(PackageName Package, string Error)> failed)
        => $"{failed.Count} {(failed.Count == 1 ? "link" : "links")} could not be repaired: {failed[0].Error}";

    /// <summary>The input with Community read now, under the lock: the run's copy can predate a change of our own (D3).</summary>
    private BoardInput WithCurrentCommunity(BoardInput input)
    {
        try
        {
            return input with { Community = CommunityClassifier.Classify(services!.ReadCommunity(), input.Index) };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return input;
        }
    }

    /// <summary>The input as every board and settlement takes it: Community as it is now, analysed with the current cards. Caller holds the lock.</summary>
    private BoardInput Reanalyzed(BoardInput input) => WithCurrentCommunity(input).Reanalyze(State);

    /// <summary>A board from the given input, never empty, also when the wall is not shown. Caller holds the lock.</summary>
    private Board BuildBoard(BoardInput input) => BoardBuilder.Build(State, Reanalyzed(input));

    private IContentWatcher NewWatcher(DiscoveryOutcome? discovery)
    {
        var created = services!.CreateWatcher([.. State.Libraries.Select(l => l.Path).Where(p => discovery is null || !discovery.Index.IsUnreachable(p))]);
        // Never takes the session lock: the watcher's thread may be in a raise while the session disposes it.
        created.RescanNeeded += () => _ = Task.Run(() => RunAsync(false));
        created.Start();
        return created;
    }

    private SessionView Publish(SessionView next)
    {
        Volatile.Write(ref view, next);
        return next;
    }

    private void Raise(SessionView? published)
    {
        if (published is not null && !disposed)
            Changed?.Invoke(published);
    }

    private void ReportProgress(ScanProgress progress)
    {
        SessionView? raise;
        lock (gate)
        {
            if (disposed)
                return;
            raise = Publish(view with { Progress = progress });
        }

        Raise(raise);
    }

    /// <summary>Implements the interface directly: <c>Progress&lt;T&gt;</c> would post thousands of reports to the UI context.</summary>
    private sealed class ProgressSink(AppSession owner) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => owner.ReportProgress(value);
    }
}
