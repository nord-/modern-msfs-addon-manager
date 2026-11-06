using AddonManager.App.Session;
using AddonManager.Core.Cards;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Infrastructure;
using AddonManager.Sim2020;
using Microsoft.Data.Sqlite;

namespace AddonManager.App.Tests.Session;

public sealed class AppSessionTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    /// <summary>Wraps the real store so a test can make a step fail.</summary>
    private sealed class FlakyStore(IUserStateStore inner) : IUserStateStore
    {
        private int beginCalls;
        private Exception? failNextSaveSlot;

        /// <summary>The 1-based <c>BeginChange</c> call that throws, once; 0 means none.</summary>
        public int FailBeginCall { get; set; }

        public UserState Load() => inner.Load();

        public void Save(UserState state)
        {
            if (Interlocked.Exchange(ref failNextSaveSlot, null) is { } failure)
                throw failure;
            inner.Save(state);
        }

        public void ArmSaveFailure(Exception failure) => failNextSaveSlot = failure;

        public IDisposable BeginChange()
        {
            if (Interlocked.Increment(ref beginCalls) == FailBeginCall)
                throw new UserDataLockedException("userdata.db");
            return inner.BeginChange();
        }
    }

    /// <summary>The default sim of the suite: never running, so no test depends on a real FlightSimulator process.</summary>
    private sealed class StoppedSim : ISimProcessMonitor
    {
        public bool IsRunning => false;
    }

    private readonly SimMachine machine = new();
    private readonly FakeWatchers watchers = new();
    private readonly List<SessionView> views = [];
    private readonly List<AppSession> sessions = [];
    private GatedReader? gated;
    private FlakyStore? store;

    public void Dispose()
    {
        foreach (var session in sessions)
            session.Dispose();
        machine.Dispose();
    }

    private AppSession NewSession(Func<SimPaths?>? locate = null, bool useGate = false, bool flakyStore = false,
        ISimProcessMonitor? sim = null, ILinkFileSystem? linkFs = null)
    {
        if (useGate)
            gated = new GatedReader(machine.Paths.OfficialPath);
        var session = new AppSession(() =>
        {
            var services = AppServices.Open(machine.DatabaseFile);
            services.CreateWatcher = watchers.Create;
            services.Sim = sim ?? new StoppedSim();
            if (linkFs is not null)
                services.LinkFileSystem = () => linkFs;
            if (flakyStore)
                services.Store = store = new FlakyStore(services.Store);
            if (useGate)
                services.UseSim(machine.Paths, machine.Gsx, gated!);
            return services;
        }, locate ?? (() => machine.Paths), machine.Gsx);
        session.Changed += v =>
        {
            lock (views)
                views.Add(v);
        };
        sessions.Add(session);
        return session;
    }

    private List<SessionView> Views()
    {
        lock (views)
            return [.. views];
    }

    private static IReadOnlyList<LibraryRoot> Libraries(params string[] paths) => [.. paths.Select(p => new LibraryRoot(p, LibraryRoot.AllSections))];

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Bound;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(10);
        }
    }

    private static Task Bounded(Task task) => task.WaitAsync(Bound);

    private static Card CardOf(SessionView view, string package)
        => view.Board!.CardOf(PackageKey.Folder(new PackageName(package))) ?? throw new Xunit.Sdk.XunitException($"No card holds '{package}'.");

    /// <summary>Start, first run with one Library, and wait for the run to complete.</summary>
    private async Task<AppSession> StartedAsync(params string[] libraries)
    {
        var session = NewSession();
        await Bounded(session.StartAsync());
        Assert.Equal(SessionPhase.FirstRun, session.Current.Phase);
        await Bounded(session.CompleteFirstRunAsync(Libraries(libraries)));
        Assert.Equal(SessionPhase.Ready, session.Current.Phase);
        return session;
    }

    private UserState Reload()
    {
        using var database = UserDataDatabase.Open(machine.DatabaseFile, UserDataAccess.Full);
        return new SqliteUserStateStore(database).Load();
    }

    [Fact]
    public async Task Disposing_during_a_scan_is_quiet()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        var session = NewSession(useGate: true);
        await Bounded(session.StartAsync());
        gated!.Close();

        var first = session.CompleteFirstRunAsync(Libraries(lib));
        await Until(() => gated.Waiting > 0);
        session.Dispose();
        var seen = Views().Count;
        gated.Open();

        await Bounded(first);
        await Task.Delay(200);
        Assert.Equal(seen, Views().Count);
    }

    [Fact]
    public async Task An_unexpected_startup_error_fails_with_its_message()
    {
        var session = NewSession(locate: () => throw new IOException("the disk is gone"));

        await Bounded(session.StartAsync());

        Assert.Equal(SessionPhase.Failed, session.Current.Phase);
        Assert.Equal("Startup failed: the disk is gone", session.Current.Error);
        Assert.False(session.Current.CanRetry);
        await Bounded(session.RescanAsync(false));
        Assert.Equal(SessionPhase.Failed, session.Current.Phase);
    }

    [Fact]
    public async Task A_failed_first_scan_recovers_on_the_next_run()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        var session = NewSession(flakyStore: true);
        await Bounded(session.StartAsync());
        store!.FailBeginCall = 2;

        await Bounded(session.CompleteFirstRunAsync(Libraries(lib)));

        Assert.Equal(SessionPhase.Failed, session.Current.Phase);
        Assert.True(session.Current.CanRetry);
        var watcher = Assert.Single(watchers.All);
        var before = Views().Count;

        watcher.Raise();

        await Until(() => session.Current is { Phase: SessionPhase.Ready, Board: not null });
        Assert.Contains(Views().Skip(before), v => v.Phase == SessionPhase.Scanning);
    }

    [Fact]
    public async Task A_run_that_fails_before_discovery_still_gets_a_watcher_to_retry_it()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        var session = NewSession();
        await Bounded(session.StartAsync());
        // Community vanishes between startup and the first run, so the run throws before its discovery is reported.
        Directory.Delete(machine.Community, recursive: true);

        await Bounded(session.CompleteFirstRunAsync(Libraries(lib)));

        Assert.Equal(SessionPhase.Failed, session.Current.Phase);
        Assert.True(session.Current.CanRetry);
        var watcher = Assert.Single(watchers.All);
        Assert.True(watcher.Started);

        Directory.CreateDirectory(machine.Community);
        watcher.Raise();

        await Until(() => session.Current is { Phase: SessionPhase.Ready, Board: not null });
    }

    [Fact]
    public async Task First_run_waits_for_libraries()
    {
        machine.AddLibrary();
        machine.AddPackage("pkg");
        machine.Link("pkg");
        var listing = machine.ListCommunity();
        var session = NewSession();

        await Bounded(session.StartAsync());

        Assert.Equal(SessionPhase.FirstRun, session.Current.Phase);
        Assert.Null(session.Current.Board);
        Assert.Equal(listing, machine.ListCommunity());
        Assert.Single(session.ReadCommunity());
    }

    [Fact]
    public async Task No_install_on_first_run_asks_for_the_packages_root()
    {
        var session = NewSession(locate: () => null);

        await Bounded(session.StartAsync());

        Assert.Equal(SessionPhase.NeedsPackagesRoot, session.Current.Phase);
        Assert.Empty(session.ReadCommunity());
    }

    [Fact]
    public async Task A_picked_root_without_community_is_refused()
    {
        var session = NewSession(locate: () => null);
        await Bounded(session.StartAsync());
        var empty = Directory.CreateDirectory(Path.Combine(machine.Root, "Empty")).FullName;

        var ok = session.TryUsePackagesRoot(empty, out var refusal);

        Assert.False(ok);
        Assert.Equal("This folder has no Community folder. Pick the folder that holds Community and Official.", refusal);
        Assert.Equal(SessionPhase.NeedsPackagesRoot, session.Current.Phase);
    }

    [Fact]
    public async Task A_picked_root_is_saved_with_the_libraries()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        machine.Link("pkg");
        var session = NewSession(locate: () => null);
        await Bounded(session.StartAsync());

        Assert.True(session.TryUsePackagesRoot(machine.PackagesRoot, out var refusal));
        Assert.Null(refusal);
        Assert.Equal(SessionPhase.FirstRun, session.Current.Phase);
        Assert.Single(session.ReadCommunity());

        await Bounded(session.CompleteFirstRunAsync(Libraries(lib)));

        var saved = Reload();
        Assert.Equal(machine.PackagesRoot, saved.PackagesRoot);
        Assert.Equal(lib, Assert.Single(saved.Libraries).Path);
    }

    [Fact]
    public async Task No_install_after_first_run_fails_with_a_message()
    {
        machine.SeedLibrary(machine.AddLibrary());
        var session = NewSession(locate: () => null);

        await Bounded(session.StartAsync());

        Assert.Equal(SessionPhase.Failed, session.Current.Phase);
        Assert.Equal("The MSFS 2020 install was not found: there is no UserCfg.opt with InstalledPackagesPath.", session.Current.Error);
        Assert.Empty(watchers.All);
    }

    [Fact]
    public async Task A_database_that_cannot_be_opened_fails_with_its_message()
    {
        File.WriteAllText(machine.DatabaseFile, "not a database");
        var session = NewSession();

        await Bounded(session.StartAsync());

        Assert.Equal(SessionPhase.Failed, session.Current.Phase);
        Assert.Contains(machine.DatabaseFile, session.Current.Error);
        Assert.False(session.Current.CanRetry);
    }

    [Fact]
    public async Task A_locked_database_at_start_of_first_run_keeps_the_first_run_screen()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        var session = NewSession();
        await Bounded(session.StartAsync());

        using (var locker = HoldWriteLock())
        {
            await Bounded(session.CompleteFirstRunAsync(Libraries(lib)));

            Assert.Equal(SessionPhase.FirstRun, session.Current.Phase);
            Assert.NotNull(session.Current.Error);
            Assert.Empty(session.State.Libraries);
        }

        await Bounded(session.CompleteFirstRunAsync(Libraries(lib)));

        Assert.Contains(Views(), v => v.Phase == SessionPhase.Scanning);
        Assert.Equal(SessionPhase.Ready, session.Current.Phase);
    }

    [Fact]
    public async Task First_run_shows_the_wall_only_after_the_scan()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        var session = await StartedAsync(lib);

        var withBoard = Views().Where(v => v.Board is not null).ToList();
        Assert.Single(withBoard);
        Assert.Equal(SessionPhase.Ready, withBoard[0].Phase);
        Assert.Null(withBoard[0].Progress);
        Assert.NotNull(CardOf(session.Current, "pkg"));
        Assert.True(session.State.ContentPassCompleted);
    }

    [Fact]
    public async Task A_later_start_with_a_cache_shows_the_wall_at_discovery()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        (await StartedAsync(lib)).Dispose();

        var second = NewSession(useGate: true);
        gated!.Close();
        var start = second.StartAsync();

        await Until(() => second.Current is { Phase: SessionPhase.Ready, Board: not null });
        Assert.False(start.IsCompleted);
        Assert.NotNull(CardOf(second.Current, "pkg"));

        gated.Open();
        await Bounded(start);
        Assert.Equal(SessionPhase.Ready, second.Current.Phase);
    }

    [Fact]
    public async Task A_located_install_without_community_asks_for_the_packages_root()
    {
        var broken = new SimPaths(null, machine.PackagesRoot, Path.Combine(machine.PackagesRoot, "Missing"), null);
        var first = NewSession(locate: () => broken);
        await Bounded(first.StartAsync());
        Assert.Equal(SessionPhase.NeedsPackagesRoot, first.Current.Phase);

        machine.SeedLibrary(machine.AddLibrary());
        var later = NewSession(locate: () => broken);
        await Bounded(later.StartAsync());

        Assert.Equal(SessionPhase.Failed, later.Current.Phase);
        Assert.Contains(broken.CommunityPath, later.Current.Error);
        Assert.Equal($"The MSFS 2020 install points to {broken.PackagesPath}, but {broken.CommunityPath} does not exist.", later.Current.Error);
    }

    [Fact]
    public async Task A_failed_save_is_retried_by_the_next_step()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        machine.Link("pkg");
        var session = NewSession(flakyStore: true);
        await Bounded(session.StartAsync());
        await Bounded(session.CompleteFirstRunAsync(Libraries(lib)));
        Assert.True(Reload().IsOn(new PackageName("pkg")));
        machine.Unlink("pkg");
        store!.ArmSaveFailure(new UserDataUnavailableException(machine.DatabaseFile, "disk full", new IOException()));

        await Bounded(session.RescanAsync(false));

        Assert.NotNull(session.Current.Error);
        Assert.True(Reload().IsOn(new PackageName("pkg")));

        await Bounded(session.RescanAsync(false));

        Assert.Null(session.Current.Error);
        Assert.False(Reload().IsOn(new PackageName("pkg")));
    }

    [Fact]
    public async Task A_linked_package_is_on_and_follows_an_unlink()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        machine.Link("pkg");
        var session = await StartedAsync(lib);
        Assert.Equal(CardState.On, CardOf(session.Current, "pkg").State);

        machine.Unlink("pkg");
        await Bounded(session.RescanAsync(false));

        Assert.Equal(CardState.Off, CardOf(session.Current, "pkg").State);
        var saved = Reload();
        Assert.False(saved.IsOn(new PackageName("pkg")));
        Assert.DoesNotContain(saved.Snapshot!, e => e.Name.Equals("pkg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_burst_of_community_changes_ends_on_the_last_state()
    {
        var lib = machine.AddLibrary();
        foreach (var name in new[] { "a", "b", "c" })
            machine.AddPackage(name);
        var session = NewSession(useGate: true);
        await Bounded(session.StartAsync());
        await Bounded(session.CompleteFirstRunAsync(Libraries(lib)));
        gated!.Close();

        machine.Link("a");
        var first = session.RescanAsync(false);
        await Until(() => gated.Waiting > 0);
        machine.Link("b");
        var second = session.RescanAsync(false);
        machine.Link("c");
        var third = session.RescanAsync(false);
        gated.Open();

        await Bounded(Task.WhenAll(first, second, third));

        Assert.Equal(SessionPhase.Ready, session.Current.Phase);
        Assert.All(new[] { "a", "b", "c" }, name => Assert.Equal(CardState.On, CardOf(session.Current, name).State));
        Assert.Null(session.Current.Error);
        Assert.Equal(3, Reload().Packages.Count(p => p.Value.On));
    }

    [Fact]
    public async Task An_unreachable_library_is_not_watched_and_its_cards_stay()
    {
        var one = machine.AddLibrary("LibOne");
        var two = machine.AddLibrary("LibTwo");
        machine.AddPackage("one", "LibOne");
        machine.AddPackage("two", "LibTwo");
        var session = await StartedAsync(one, two);
        Assert.Equal(2, watchers.All[^1].Libraries.Count);

        Directory.Move(two, two + "-away");
        await Bounded(session.RescanAsync(false));

        Assert.Equal(SessionPhase.Ready, session.Current.Phase);
        var missing = Assert.Single(session.Current.Board!.Banner.LibrariesNotFound);
        Assert.Equal(two, missing.Path, ignoreCase: true);
        Assert.Equal(one, Assert.Single(watchers.All[^1].Libraries), ignoreCase: true);
        Assert.NotNull(CardOf(session.Current, "two"));
        Assert.NotNull(CardOf(session.Current, "one"));
    }

    [Fact]
    public async Task A_locked_database_keeps_the_wall_and_reports_the_file()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        var session = await StartedAsync(lib);
        var board = session.Current.Board;
        machine.Link("pkg");

        using (var locker = HoldWriteLock())
        {
            await Bounded(session.RescanAsync(false));

            Assert.Contains(machine.DatabaseFile, session.Current.Error);
            Assert.Equal(SessionPhase.Ready, session.Current.Phase);
            Assert.Same(board, session.Current.Board);
        }

        await Bounded(session.RescanAsync(false));

        Assert.Null(session.Current.Error);
        Assert.Equal(CardState.On, CardOf(session.Current, "pkg").State);
        var saved = Reload();
        Assert.True(saved.IsOn(new PackageName("pkg")));
        Assert.Contains(saved.Snapshot!, e => e.Name.Equals("pkg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_watcher_is_recreated_after_each_scan()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        await StartedAsync(lib);

        // One from the first run's discovery, one from its completion.
        var all = watchers.All;
        Assert.Equal(2, all.Count);
        Assert.True(all[0].Disposed);
        Assert.True(all[1].Started);
        Assert.False(all[1].Disposed);
        Assert.Equal(lib, Assert.Single(all[1].Libraries), ignoreCase: true);

        await Bounded(sessions[0].RescanAsync(false));

        all = watchers.All;
        Assert.Equal(3, all.Count);
        Assert.True(all[1].Disposed);
        Assert.True(all[2].Started);
    }

    [Fact]
    public async Task A_watcher_signal_runs_a_scan()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        var session = await StartedAsync(lib);
        var handled = session.HandledOutcomes;

        watchers.All[^1].Raise();

        await Until(() => session.HandledOutcomes == handled + 1);
        Assert.Equal(3, watchers.All.Count);
    }

    [Fact]
    public async Task A_watcher_signal_during_a_completion_neither_deadlocks_nor_races()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        watchers.FirstRaisesOnDispose = true;

        var session = await StartedAsync(lib);

        await Until(() => session.HandledOutcomes == 2 && session.Current.Phase == SessionPhase.Ready);
        await Bounded(session.RescanAsync(false));
        Assert.Null(session.Current.Error);
    }

    [Fact]
    public async Task A_change_the_replaced_watcher_had_not_raised_yet_is_not_lost()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        watchers.FirstIsPending = true;

        var session = await StartedAsync(lib);

        // The replaced watcher still held an event, so the new one is asked for a rescan.
        var all = watchers.All;
        Assert.Single(all[1].Scheduled);
        var handled = session.HandledOutcomes;
        all[1].Raise();
        await Until(() => session.HandledOutcomes == handled + 1);
    }

    [Fact]
    public async Task Callers_merged_into_one_run_settle_it_once()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        var session = NewSession(useGate: true);
        await Bounded(session.StartAsync());
        await Bounded(session.CompleteFirstRunAsync(Libraries(lib)));
        var handledBefore = session.HandledOutcomes;
        var watchersBefore = watchers.All.Count;
        var requestedBefore = session.RequestedRuns;
        gated!.Close();

        var first = session.RescanAsync(false);
        await Until(() => gated.Waiting > 0);
        var second = session.RescanAsync(false);
        var third = session.RescanAsync(false);
        // The gate opens only once both later callers have reached the session, or they could start a run of their own.
        await Until(() => session.RequestedRuns - requestedBefore == 3);
        gated.Open();

        await Bounded(Task.WhenAll(first, second, third));

        Assert.InRange(session.HandledOutcomes - handledBefore, 1, 2);
        Assert.InRange(watchers.All.Count - watchersBefore, 1, 2);
        Assert.Equal(SessionPhase.Ready, session.Current.Phase);
        Assert.Null(session.Current.Progress);
        Assert.Null(session.Current.Error);
    }

    [Fact]
    public async Task Nothing_outside_the_database_changes()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        machine.AddPackage("b");
        machine.Link("a");
        machine.Link("b");
        machine.WriteGsx("eham.ini");
        var gsx = machine.ListGsx();
        var session = await StartedAsync(lib);
        for (var i = 0; i < 3; i++)
            await Bounded(session.RescanAsync(i == 2));

        var before = machine.ListCommunity();
        machine.Unlink("b");
        var expected = machine.ListCommunity();
        await Bounded(session.RescanAsync(false));

        Assert.Equal(before.Count - 1, expected.Count);
        Assert.Equal(expected, machine.ListCommunity());
        Assert.Equal(gsx, machine.ListGsx());
    }

    /// <summary>A second, unpooled connection holding the write lock of the user database until it is disposed.</summary>
    private LockHolder HoldWriteLock() => new(machine.DatabaseFile);

    private sealed class LockHolder : IDisposable
    {
        private readonly SqliteConnection connection;

        public LockHolder(string file)
        {
            connection = new SqliteConnection($"Data Source={file};Pooling=False");
            connection.Open();
            Run("BEGIN IMMEDIATE");
        }

        public void Dispose()
        {
            Run("ROLLBACK");
            connection.Dispose();
        }

        private void Run(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
