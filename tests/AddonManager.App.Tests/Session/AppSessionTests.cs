using AddonManager.App.Session;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Infrastructure;
using AddonManager.Sim2020;
using AddonManager.Sim2020.Content;
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

        /// <summary>How many times <c>BeginChange</c> was called.</summary>
        public int BeginCalls => Volatile.Read(ref beginCalls);

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

    /// <summary>A sim a test starts and stops by hand.</summary>
    private sealed class ToggleSim : ISimProcessMonitor
    {
        public volatile bool Running;

        public bool IsRunning => Running;
    }

    /// <summary>A sim that reports running from its <c>from</c>-th read on: it starts between a check and the next call.</summary>
    private sealed class SimStartingAtRead(int from) : ISimProcessMonitor
    {
        private int reads;

        public bool IsRunning => Interlocked.Increment(ref reads) >= from;
    }

    /// <summary>The real link writer, except that a junction to a target under <c>failUnder</c> cannot be made.</summary>
    private sealed class FailingLinks(string failUnder) : ILinkFileSystem
    {
        public const string Message = "the disk said no";

        private readonly JunctionFileSystem inner = new();

        public void CreateJunction(string linkPath, string targetPath)
        {
            if (PathNormalizer.IsUnder(targetPath, failUnder))
                throw new IOException(Message);
            inner.CreateJunction(linkPath, targetPath);
        }

        public void RemoveLink(string linkPath) => inner.RemoveLink(linkPath);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
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
        ISimProcessMonitor? sim = null, ILinkFileSystem? linkFs = null, IPackageContentReader? reader = null)
    {
        if (useGate)
            reader = gated = new GatedReader(machine.Paths.OfficialPath);
        var session = new AppSession(() =>
        {
            var services = AppServices.Open(machine.DatabaseFile);
            services.CreateWatcher = watchers.Create;
            services.Sim = sim ?? new StoppedSim();
            if (linkFs is not null)
                services.LinkFileSystem = () => linkFs;
            if (flakyStore)
                services.Store = store = new FlakyStore(services.Store);
            if (reader is not null)
                services.UseSim(machine.Paths, machine.Gsx, reader);
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

    private static Task<T> Bounded<T>(Task<T> task) => task.WaitAsync(Bound);

    private static Card CardOf(SessionView view, string package)
        => view.Board!.CardOf(PackageKey.Folder(new PackageName(package))) ?? throw new Xunit.Sdk.XunitException($"No card holds '{package}'.");

    /// <summary>Start, first run with one Library, and wait for the run to complete.</summary>
    private Task<AppSession> StartedAsync(params string[] libraries) => StartedAsync(NewSession(), libraries);

    private async Task<AppSession> StartedAsync(AppSession session, params string[] libraries)
    {
        await Bounded(session.StartAsync());
        Assert.Equal(SessionPhase.FirstRun, session.Current.Phase);
        await Bounded(session.CompleteFirstRunAsync(Libraries(libraries)));
        Assert.Equal(SessionPhase.Ready, session.Current.Phase);
        return session;
    }

    private string? LinkTarget(string name) => JunctionFileSystem.ReadLinkTarget(Path.Combine(machine.Community, name));

    private void AssertLinkedTo(string name, string library)
        => Assert.True(PathNormalizer.AreEqual(Path.Combine(machine.Root, library, name), LinkTarget(name) ?? ""), $"'{name}' points to {LinkTarget(name)}.");

    /// <summary>A Package linked from Lib and then moved to Lib2: its link is Stale and the first run can repair it.</summary>
    private (string Lib, string Lib2) MovedAddOn(string name = "a")
    {
        var lib = machine.AddLibrary();
        var lib2 = machine.AddLibrary("Lib2");
        machine.AddPackage(name);
        machine.Link(name);
        machine.MovePackage(name, "Lib", "Lib2");
        return (lib, lib2);
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

        await Bounded(session.RescanAsync(false));

        // The discovery is not lost: the next one adopts the link that was removed.
        Assert.Null(session.Current.Error);
        Assert.False(Reload().IsOn(new PackageName("pkg")));
    }

    [Fact]
    public async Task A_link_removed_outside_the_app_turns_the_card_off()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("pkg");
        machine.Link("pkg");
        var session = await StartedAsync(lib);
        Assert.Equal(CardState.On, CardOf(session.Current, "pkg").State);

        machine.Unlink("pkg");
        await Bounded(session.RescanAsync(false));

        Assert.Equal(CardState.Off, CardOf(session.Current, "pkg").State);
        Assert.False(Reload().IsOn(new PackageName("pkg")));
        Assert.Null(LinkTarget("pkg"));
        Assert.Null(session.Current.Error);
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
        Assert.Null(session.Current.Error);
        Assert.All(["a", "b", "c"], name => Assert.Equal(CardState.On, CardOf(session.Current, name).State));
        Assert.All(["a", "b", "c"], name => Assert.True(Reload().IsOn(new PackageName(name))));
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
        Assert.True(Reload().IsOn(new PackageName("pkg")));
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

    [Fact]
    public async Task A_link_removed_by_another_tool_is_adopted_at_the_watchers_discovery()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        machine.Link("a");
        var session = await StartedAsync(lib);

        machine.Unlink("a");
        watchers.All[^1].Raise();
        await Until(() => CardOf(session.Current, "a").State == CardState.Off);

        Assert.False(session.Current.LinksWaiting);
        Assert.Null(LinkTarget("a"));
        Assert.False(Reload().IsOn(new PackageName("a")));
    }

    [Fact]
    public async Task A_link_to_a_moved_add_on_of_an_off_package_is_re_pointed_and_turned_on()
    {
        var lib = machine.AddLibrary();
        var lib2 = machine.AddLibrary("Lib2");
        machine.AddPackage("a");
        var session = await StartedAsync(lib, lib2);
        Assert.Equal(CardState.Off, CardOf(session.Current, "a").State);

        // Another tool links the add-on, and it is moved before the app sees the link.
        machine.Link("a");
        machine.MovePackage("a", "Lib", "Lib2");
        await Bounded(session.RescanAsync(false));

        AssertLinkedTo("a", "Lib2");
        Assert.Equal(CardState.On, CardOf(session.Current, "a").State);
        Assert.True(Reload().IsOn(new PackageName("a")));
        Assert.False(session.Current.LinksWaiting);
    }

    [Fact]
    public async Task First_run_repairs_a_link_to_a_moved_add_on()
    {
        var (lib, lib2) = MovedAddOn();

        var session = await StartedAsync(lib, lib2);

        AssertLinkedTo("a", "Lib2");
        Assert.False(session.Current.LinksWaiting);
        Assert.Null(session.Current.Error);
    }

    [Fact]
    public async Task A_repair_waits_while_the_sim_runs_and_is_done_after()
    {
        var (lib, lib2) = MovedAddOn();
        var sim = new ToggleSim { Running = true };
        var session = await StartedAsync(NewSession(sim: sim), lib, lib2);

        AssertLinkedTo("a", "Lib");
        Assert.True(session.Current.LinksWaiting);

        sim.Running = false;
        await Bounded(session.RescanAsync(false));

        AssertLinkedTo("a", "Lib2");
        Assert.False(session.Current.LinksWaiting);
    }

    [Fact]
    public async Task A_failed_repair_shows_the_error_line()
    {
        var (lib, lib2) = MovedAddOn();

        var session = await StartedAsync(NewSession(linkFs: new FailingLinks(lib2)), lib, lib2);

        Assert.Equal($"1 link could not be repaired: {FailingLinks.Message}", session.Current.Error);
        Assert.Equal(SessionPhase.Ready, session.Current.Phase);
    }

    [Fact]
    public async Task Several_failed_repairs_are_counted()
    {
        var (lib, lib2) = MovedAddOn("a");
        machine.AddPackage("b");
        machine.Link("b");
        machine.MovePackage("b", "Lib", "Lib2");

        var session = await StartedAsync(NewSession(linkFs: new FailingLinks(lib2)), lib, lib2);

        Assert.Equal($"2 links could not be repaired: {FailingLinks.Message}", session.Current.Error);
    }

    [Fact]
    public async Task A_waiting_repair_survives_the_runs_completion()
    {
        var (lib, lib2) = MovedAddOn();
        var session = NewSession(useGate: true, sim: new ToggleSim { Running = true });
        await Bounded(session.StartAsync());
        gated!.Close();

        var first = session.CompleteFirstRunAsync(Libraries(lib, lib2));
        await Until(() => gated.Waiting > 0);
        Assert.True(session.Current.LinksWaiting);
        gated.Open();
        await Bounded(first);

        Assert.Equal(SessionPhase.Ready, session.Current.Phase);
        Assert.True(session.Current.LinksWaiting);
    }

    [Fact]
    public async Task A_repaired_link_shows_on_the_board_published_at_the_discovery()
    {
        var lib = machine.AddLibrary();
        var lib2 = machine.AddLibrary("Lib2");
        machine.AddPackage("a");
        machine.Link("a");
        (await StartedAsync(lib, lib2)).Dispose();
        machine.MovePackage("a", "Lib", "Lib2");

        var second = NewSession(useGate: true);
        gated!.Close();
        var start = second.StartAsync();
        await Until(() => second.Current is { Phase: SessionPhase.Ready, Board: not null });

        Assert.False(start.IsCompleted);
        Assert.Equal(CardState.On, CardOf(second.Current, "a").State);
        AssertLinkedTo("a", "Lib2");
        gated.Open();
        await Bounded(start);
    }

    [Fact]
    public async Task While_the_sim_runs_a_removed_link_is_adopted_without_waiting()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        machine.Link("a");
        var sim = new ToggleSim();
        var session = await StartedAsync(NewSession(sim: sim), lib);
        sim.Running = true;
        machine.Unlink("a");

        await Bounded(session.RescanAsync(false));

        Assert.Equal(CardState.Off, CardOf(session.Current, "a").State);
        Assert.False(Reload().IsOn(new PackageName("a")));
        Assert.False(session.Current.LinksWaiting);
        Assert.Null(LinkTarget("a"));
    }

    [Fact]
    public async Task A_sim_start_during_a_repair_waits_instead_of_failing()
    {
        var (lib, lib2) = MovedAddOn();

        var session = await StartedAsync(NewSession(sim: new SimStartingAtRead(2)), lib, lib2);

        Assert.Equal(SessionPhase.Ready, session.Current.Phase);
        Assert.Null(session.Current.Error);
        Assert.True(session.Current.LinksWaiting);
        AssertLinkedTo("a", "Lib");
    }

    [Fact]
    public async Task A_repair_during_the_first_scan_leaves_the_phase_at_scanning()
    {
        var (lib, lib2) = MovedAddOn();
        var session = NewSession(useGate: true);
        await Bounded(session.StartAsync());
        gated!.Close();

        var first = session.CompleteFirstRunAsync(Libraries(lib, lib2));
        await Until(() => gated.Waiting > 0);

        AssertLinkedTo("a", "Lib2");
        Assert.Equal(SessionPhase.Scanning, session.Current.Phase);
        Assert.DoesNotContain(Views(), v => v.Board is not null);
        gated.Open();
        await Bounded(first);
        Assert.Equal(SessionPhase.Ready, session.Current.Phase);
        Assert.NotNull(session.Current.Board);
    }

    private static PackageKey Pkg(string name) => PackageKey.Folder(new PackageName(name));

    private void DeletePackage(string name, string library = "Lib") => Directory.Delete(Path.Combine(machine.Root, library, name), true);

    [Fact]
    public async Task Switching_a_card_links_it_and_the_wall_shows_it_at_once()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        var session = await StartedAsync(lib);
        var card = CardOf(session.Current, "a");
        Assert.Equal(CardState.Off, card.State);

        var message = await Bounded(session.SwitchCardAsync(CardRef.Of(card)));

        Assert.Null(message);
        AssertLinkedTo("a", "Lib");
        Assert.Equal(CardState.On, CardOf(session.Current, "a").State);
        Assert.True(session.State.IsOn(new PackageName("a")));
    }

    [Fact]
    public async Task An_own_change_is_not_undone_by_the_next_discovery()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        var session = await StartedAsync(lib);
        await Bounded(session.SwitchCardAsync(CardRef.Of(CardOf(session.Current, "a"))));
        var handled = session.HandledOutcomes;

        watchers.All[^1].Raise();
        await Until(() => session.HandledOutcomes > handled);

        Assert.Equal(CardState.On, CardOf(session.Current, "a").State);
        AssertLinkedTo("a", "Lib");
        Assert.False(session.Current.LinksWaiting);
    }

    [Fact]
    public async Task A_switch_during_a_run_survives_its_completion()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        var session = NewSession(useGate: true);
        await Bounded(session.StartAsync());
        await Bounded(session.CompleteFirstRunAsync(Libraries(lib)));
        var card = CardRef.Of(CardOf(session.Current, "a"));
        gated!.Close();

        var run = session.RescanAsync(false);
        await Until(() => gated.Waiting > 0);
        var message = await Bounded(session.SwitchCardAsync(card));
        gated.Open();
        await Bounded(run);

        Assert.Null(message);
        Assert.Equal(CardState.On, CardOf(session.Current, "a").State);
        AssertLinkedTo("a", "Lib");
    }

    [Fact]
    public async Task A_switch_while_the_sim_runs_returns_the_sim_text_and_changes_nothing()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        var sim = new ToggleSim();
        var session = await StartedAsync(NewSession(sim: sim), lib);
        var card = CardRef.Of(CardOf(session.Current, "a"));
        sim.Running = true;

        var message = await Bounded(session.SwitchCardAsync(card));

        Assert.Equal(SimRunningException.Text, message);
        Assert.Empty(machine.ListCommunity());
        Assert.False(session.State.IsOn(new PackageName("a")));
        Assert.Equal(CardState.Off, CardOf(session.Current, "a").State);
    }

    [Fact]
    public async Task A_locked_database_refuses_the_switch_before_any_link()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        var session = NewSession(flakyStore: true);
        await StartedAsync(session, lib);
        var card = CardRef.Of(CardOf(session.Current, "a"));
        store!.FailBeginCall = store.BeginCalls + 1;

        var message = await Bounded(session.SwitchCardAsync(card));

        Assert.Contains("userdata.db", message);
        Assert.Empty(machine.ListCommunity());
    }

    [Fact]
    public async Task A_copy_gone_before_the_click_shows_the_board_reason()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        var session = await StartedAsync(lib);
        var card = CardOf(session.Current, "a");
        Assert.NotNull(card.Id);
        var before = machine.ListCommunity();
        DeletePackage("a");

        var message = await Bounded(session.SwitchCardAsync(CardRef.Of(card)));

        Assert.Null(message);
        Assert.Equal(before, machine.ListCommunity());
        Assert.False(session.State.IsOn(new PackageName("a")));
        await Bounded(session.RescanAsync(false));
        var row = session.Current.Board!.Find(card.Id!.Value)!.Packages.Single();
        Assert.NotNull(row.DisabledReason);
    }

    [Fact]
    public async Task A_failed_link_change_returns_its_message()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        var session = await StartedAsync(NewSession(linkFs: new FailingLinks(lib)), lib);

        var message = await Bounded(session.SwitchCardAsync(CardRef.Of(CardOf(session.Current, "a"))));

        Assert.Equal($"Could not change a: {FailingLinks.Message}", message);
        Assert.Empty(machine.ListCommunity());
    }

    [Fact]
    public async Task Several_failed_link_changes_return_the_count_and_the_first_message()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        machine.AddPackage("b");
        var session = await StartedAsync(NewSession(linkFs: new FailingLinks(lib)), lib);
        // The user grouped both Packages on one card.
        session.State.SetCard(Pkg("b"), CardOf(session.Current, "a").Id);
        session.State.RemoveEmptyCards();
        await Bounded(session.RescanAsync(false));
        var card = CardOf(session.Current, "a");
        Assert.Equal(2, card.Packages.Count);

        var message = await Bounded(session.SwitchCardAsync(CardRef.Of(card)));

        Assert.Equal($"2 packages could not be changed: {FailingLinks.Message}", message);
    }

    [Fact]
    public async Task A_link_removed_while_the_sim_ran_can_be_switched_back_on_after()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        machine.Link("a");
        var sim = new ToggleSim();
        var session = await StartedAsync(NewSession(sim: sim), lib);
        sim.Running = true;
        machine.Unlink("a");
        await Bounded(session.RescanAsync(false));
        Assert.Equal(CardState.Off, CardOf(session.Current, "a").State);
        sim.Running = false;

        var message = await Bounded(session.SwitchCardAsync(CardRef.Of(CardOf(session.Current, "a"))));
        await Bounded(session.RescanAsync(false));

        Assert.Null(message);
        AssertLinkedTo("a", "Lib");
        Assert.True(session.State.IsOn(new PackageName("a")));
        Assert.Equal(CardState.On, CardOf(session.Current, "a").State);
    }

    [Fact]
    public async Task Remove_forgets_a_not_found_package_and_its_card()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        var session = await StartedAsync(lib);
        var card = CardOf(session.Current, "a");
        DeletePackage("a");
        await Bounded(session.RescanAsync(false));

        var message = await Bounded(session.RemoveAsync(CardRef.Of(card), Pkg("a")));

        Assert.Null(message);
        Assert.Null(session.Current.Board!.CardOf(Pkg("a")));
        Assert.Empty(Reload().Cards);
        Assert.Empty(Reload().Packages);
    }

    [Fact]
    public async Task Remove_while_the_sim_runs_returns_the_sim_text()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        var sim = new ToggleSim();
        var session = await StartedAsync(NewSession(sim: sim), lib);
        var card = CardOf(session.Current, "a");
        DeletePackage("a");
        await Bounded(session.RescanAsync(false));
        sim.Running = true;

        var message = await Bounded(session.RemoveAsync(CardRef.Of(card), Pkg("a")));

        Assert.Equal(SimRunningException.Text, message);
        Assert.NotNull(session.Current.Board!.CardOf(Pkg("a")));
    }

    /// <summary>The real reader, except that the named Packages read as Scenery with a BGL file holding one airport record each.</summary>
    private sealed class AirportReader(string? officialPath, params (string Package, string Code, string Name)[] airports) : IPackageContentReader
    {
        private readonly PackageContentReader inner = new(new AirportNames(officialPath));

        public ChangeMarker? ReadMarker(string packagePath) => inner.ReadMarker(packagePath);

        public bool? HasBgl(string packagePath) => Find(Path.GetFileName(packagePath)) is null ? inner.HasBgl(packagePath) : true;

        public PackageContent? Read(ScanTarget target, ChangeMarker marker)
        {
            var content = inner.Read(target, marker);
            return content is null || Find(target.Name.Value) is not { } airport
                ? content
                : content with { HasBgl = true, BglSectionTypes = [0x03], Airports = [new AirportRecord(Code(airport.Code), airport.Name, new GeoPoint(52, 4))] };
        }

        public DateTimeOffset? NewestWrite(string packagePath) => inner.NewestWrite(packagePath);

        private (string Package, string Code, string Name)? Find(string package)
            => airports.Where(a => string.Equals(a.Package, package, StringComparison.OrdinalIgnoreCase)).Cast<(string, string, string)?>().FirstOrDefault();
    }

    private static AirportCode Code(string value) => AirportCode.TryParse(value, out var code) ? code : throw new ArgumentException(value);

    /// <summary>A started session over two Scenery cards on Airports: <c>eham-x</c> (EHAM Amsterdam Schiphol) and <c>ehrd-x</c> (EHRD Rotterdam).</summary>
    private async Task<AppSession> AirportsStartedAsync()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("eham-x");
        machine.AddPackage("ehrd-x");
        var reader = new AirportReader(machine.Paths.OfficialPath, ("eham-x", "EHAM", "Amsterdam Schiphol"), ("ehrd-x", "EHRD", "Rotterdam"));
        return await StartedAsync(NewSession(reader: reader), lib);
    }

    /// <summary>Every edit as one call on a stored card, by name for the theories below; each is one the card can take when nothing refuses it.</summary>
    private static readonly Dictionary<string, Func<AppSession, CardRef, Task<string?>>> EditCalls = new()
    {
        ["Rename"] = (session, card) => session.RenameAsync(card, "Home"),
        ["SetImage"] = (session, card) => session.SetImageAsync(card, @"C:\Images\home.png"),
        ["AddCode"] = async (session, card) => (await session.AddCodeAsync(card, "EHGG")).Message,
        ["RemoveCode"] = (session, card) => session.RemoveCodeAsync(card, Code("EHAM")),
        ["Split"] = (session, card) => session.SplitAsync(card, Pkg("ehrd-x")),
        ["Merge"] = (session, card) => session.MergeAsync(card, CardRef.Of(CardOf(session.Current, "ehle-x"))),
        ["MoveToTab"] = (session, card) => session.MoveToTabAsync(card, CardTab.Landscapes),
        ["MoveToSection"] = (session, card) => session.MoveToSectionAsync(card, Category.Other),
    };

    public static TheoryData<string> Edits => [.. EditCalls.Keys];

    /// <summary>
    /// A started session (with the flaky store) whose stored card holds <c>eham-x</c> and <c>ehrd-x</c> (EHAM and EHRD, merged), so every
    /// call in <see cref="EditCalls"/> can be made on it, next to the Scenery card of <c>ehle-x</c> (EHLE) to merge it into.
    /// </summary>
    private async Task<(AppSession Session, CardRef Card)> EditableAsync()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("eham-x");
        machine.AddPackage("ehrd-x");
        machine.AddPackage("ehle-x");
        var reader = new AirportReader(
            machine.Paths.OfficialPath, ("eham-x", "EHAM", "Amsterdam Schiphol"), ("ehrd-x", "EHRD", "Rotterdam"), ("ehle-x", "EHLE", "Lelystad"));
        var session = await StartedAsync(NewSession(flakyStore: true, reader: reader), lib);
        var card = CardRef.Of(CardOf(session.Current, "eham-x"));
        Assert.Null(await Bounded(session.MergeAsync(CardRef.Of(CardOf(session.Current, "ehrd-x")), card)));
        return (session, card);
    }

    /// <summary>The stored cards and the Packages on each, as text to compare.</summary>
    private static IReadOnlyList<string> CardsOf(UserState state)
        => [.. state.Cards.Select(c =>
            $"{c.Id}|{c.Section}|{c.Tab}|{c.Name}|{c.ImagePath}|{string.Join(",", c.Codes.Select(x => x.Value))}|{string.Join(",", state.PackagesOn(c.Id))}")];

    [Fact]
    public async Task Rename_saves_the_name_and_the_wall_shows_it_at_once()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        var session = await StartedAsync(lib);
        var card = CardOf(session.Current, "a");

        var message = await Bounded(session.RenameAsync(CardRef.Of(card), "Home"));

        Assert.Null(message);
        Assert.Equal("Home", CardOf(session.Current, "a").Name);
        Assert.Equal("Home", Reload().FindCard(card.Id!.Value)!.Name);
    }

    [Theory]
    [MemberData(nameof(Edits))]
    public async Task A_locked_database_refuses_every_edit_and_changes_nothing(string edit)
    {
        var (session, card) = await EditableAsync();
        var before = CardsOf(Reload());
        store!.FailBeginCall = store.BeginCalls + 1;

        var message = await Bounded(EditCalls[edit](session, card));

        Assert.Contains("userdata.db", message);
        Assert.Equal(before, CardsOf(Reload()));
    }

    [Theory]
    [MemberData(nameof(Edits))]
    public async Task Every_edit_of_a_card_that_is_gone_returns_the_gone_text(string edit)
    {
        var (session, _) = await EditableAsync();
        var before = CardsOf(Reload());

        var message = await Bounded(EditCalls[edit](session, new CardRef(999, null)));

        Assert.Equal(AppSession.CardGoneText, message);
        Assert.Equal(before, CardsOf(Reload()));
    }

    [Fact]
    public async Task A_merge_with_a_card_that_is_gone_returns_the_gone_text()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        var session = await StartedAsync(lib);
        var before = CardsOf(Reload());

        var message = await Bounded(session.MergeAsync(CardRef.Of(CardOf(session.Current, "a")), new CardRef(999, null)));

        Assert.Equal(AppSession.CardGoneText, message);
        Assert.Equal(before, CardsOf(Reload()));
        Assert.NotNull(CardOf(session.Current, "a").Id);
    }

    [Fact]
    public async Task Merging_a_card_into_itself_returns_a_message_and_throws_nothing()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        var session = await StartedAsync(lib);
        var card = CardRef.Of(CardOf(session.Current, "a"));

        var message = await Bounded(session.MergeAsync(card, card));

        // The ArgumentException's text, without its " (Parameter 'target')" suffix.
        Assert.Equal("A card cannot be merged into itself.", message);
    }

    [Fact]
    public async Task AddCode_returns_the_outcome_and_a_taken_code_names_the_holder()
    {
        var session = await AirportsStartedAsync();

        var added = await Bounded(session.AddCodeAsync(CardRef.Of(CardOf(session.Current, "eham-x")), "EHLE"));
        var taken = await Bounded(session.AddCodeAsync(CardRef.Of(CardOf(session.Current, "ehrd-x")), "EHLE"));

        Assert.Equal((new CodeOutcome(CodeResult.Added), (string?)null), added);
        Assert.Null(taken.Message);
        Assert.Equal(CodeResult.Taken, taken.Outcome!.Result);
        Assert.Equal(CardOf(session.Current, "eham-x").Name, taken.Outcome.HolderName);
        Assert.Contains(Code("EHLE"), CardOf(session.Current, "eham-x").Codes);
    }

    [Fact]
    public async Task Split_works_while_the_sim_runs()
    {
        var lib = machine.AddLibrary();
        machine.AddPackage("a");
        machine.AddPackage("b");
        var sim = new ToggleSim();
        var session = await StartedAsync(NewSession(sim: sim), lib);
        var a = CardRef.Of(CardOf(session.Current, "a"));
        Assert.Null(await Bounded(session.MergeAsync(CardRef.Of(CardOf(session.Current, "b")), a)));
        Assert.Single(session.Current.Board!.Cards);
        sim.Running = true;

        var message = await Bounded(session.SplitAsync(a, Pkg("b")));

        Assert.Null(message);
        Assert.Equal(2, session.Current.Board!.Cards.Count);
        Assert.NotEqual(CardOf(session.Current, "a").Id, CardOf(session.Current, "b").Id);
    }

    [Fact]
    public async Task Moving_to_a_section_puts_the_card_on_its_initial_tab()
    {
        var session = await AirportsStartedAsync();

        var message = await Bounded(session.MoveToSectionAsync(CardRef.Of(CardOf(session.Current, "eham-x")), Category.Other));

        Assert.Null(message);
        var card = CardOf(session.Current, "eham-x");
        Assert.Equal((Category.Other, CardTab.Other), (card.Section, card.Tab));
        Assert.Empty(card.Codes);
    }

    [Fact]
    public async Task MoveToTab_and_SetImage_and_RemoveCode_reach_the_editor()
    {
        var session = await AirportsStartedAsync();
        var card = CardRef.Of(CardOf(session.Current, "eham-x"));

        Assert.Null(await Bounded(session.MoveToTabAsync(card, CardTab.Landscapes)));
        Assert.Equal(CardTab.Landscapes, CardOf(session.Current, "eham-x").Tab);

        Assert.Null(await Bounded(session.SetImageAsync(card, @"C:\Images\home.png")));
        Assert.Equal(@"C:\Images\home.png", CardOf(session.Current, "eham-x").ImagePath);

        Assert.Null(await Bounded(session.RemoveCodeAsync(card, Code("EHAM"))));
        Assert.Empty(CardOf(session.Current, "eham-x").Codes);
    }

    [Fact]
    public async Task Airports_lists_the_known_airports_after_discovery()
    {
        var session = NewSession(reader: new AirportReader(machine.Paths.OfficialPath, ("eham-x", "EHAM", "Amsterdam Schiphol"), ("ehrd-x", "EHRD", "Rotterdam")));
        Assert.Empty(session.Airports);
        var lib = machine.AddLibrary();
        machine.AddPackage("eham-x");
        machine.AddPackage("ehrd-x");

        await StartedAsync(session, lib);

        Assert.Equal("Amsterdam Schiphol", session.Airports[Code("EHAM")].Name);
        Assert.Equal("Rotterdam", session.Airports[Code("EHRD")].Name);
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
