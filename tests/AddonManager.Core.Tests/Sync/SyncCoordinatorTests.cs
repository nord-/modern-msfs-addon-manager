using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Core.Sync;
using AddonManager.Core.Tests.Fakes;
using static AddonManager.Core.Tests.TestData;

namespace AddonManager.Core.Tests.Sync;

public class SyncCoordinatorTests
{
    private const string CommunityPath = @"C:\MSFS\Community";

    private static readonly PackageName Pkg = new("pkg"), Other = new("other"), M = new("m");

    private readonly PackageCopy pkg = Copy("pkg"), other = Copy("other"), mNew = Copy("m", @"C:\Sim\Addons\Scenery\m");
    private readonly FakeCommunity community = new();
    private readonly FakeLinkFileSystem fs;
    private readonly InMemoryUserStateStore store = new();
    private readonly FakeSim sim = new();
    private readonly PackageIndex index;
    private readonly ChangeApplier applier;
    private readonly SyncCoordinator coordinator;
    private readonly UserState state = new();

    public SyncCoordinatorTests()
    {
        fs = new FakeLinkFileSystem(community);
        index = new PackageIndex([pkg, other, mNew], [Addons]);
        applier = new ChangeApplier(fs, community, store, sim, CommunityPath);
        coordinator = new SyncCoordinator(applier, community, store);
    }

    private static Dictionary<PackageName, SyncChoice> KeepAll(IEnumerable<SyncRow> rows) => rows.ToDictionary(r => r.Package, _ => SyncChoice.KeepCommunity);

    [Fact]
    public void First_start_adopts_community_and_shows_no_dialog()
    {
        community.AddJunction("pkg", pkg.Path);

        var o = coordinator.Start(state, index);

        Assert.True(o.FirstRun);
        Assert.Empty(o.Rows);
        Assert.NotNull(state.Snapshot);
        Assert.True(state.IsOn(Pkg));
        Assert.True(store.Load().IsOn(Pkg));
    }

    [Fact]
    public void Second_start_with_no_changes_shows_no_dialog()
    {
        community.AddJunction("pkg", pkg.Path);
        coordinator.Start(state, index);

        var o = coordinator.Start(state, index);

        Assert.False(o.FirstRun);
        Assert.Empty(o.Rows);
    }

    [Fact]
    public void Keep_community_is_not_asked_again_on_next_start()
    {
        coordinator.Start(state, index);
        community.AddJunction("other", other.Path);
        var o = coordinator.Start(state, index);
        Assert.Equal(SyncReason.LinkAppeared, o.Rows.Single().Reason);

        coordinator.Resolve(state, index, o.Rows, KeepAll(o.Rows));

        Assert.True(state.IsOn(Other));
        Assert.Empty(coordinator.Start(state, index).Rows);
        Assert.Empty(fs.Calls);
    }

    [Fact]
    public void Restore_app_relinks_and_is_not_asked_again()
    {
        coordinator.Start(state, index);
        community.AddJunction("other", other.Path);
        var o = coordinator.Start(state, index);

        coordinator.Resolve(state, index, o.Rows, new Dictionary<PackageName, SyncChoice> { [Other] = SyncChoice.RestoreApp });

        Assert.False(community.Has("other"));
        Assert.Empty(coordinator.Start(state, index).Rows);
    }

    [Fact]
    public void Keep_community_is_not_asked_again_even_when_the_relink_was_deferred()
    {
        community.AddJunction("m", @"C:\Sim\Addons\m", targetExists: false);
        coordinator.Start(state, index);
        var o = coordinator.Start(state, index);
        Assert.Equal(SyncReason.Moved, o.Rows.Single().Reason);

        sim.IsRunning = true;
        coordinator.Resolve(state, index, o.Rows, KeepAll(o.Rows));
        sim.IsRunning = false;

        Assert.Empty(coordinator.Start(state, index).Rows);
        Assert.False(community.Has("m"));
        Assert.False(state.IsOn(M));
        Assert.Empty(coordinator.Start(state, index).Rows);
    }

    [Fact]
    public void Pending_from_a_previous_session_is_applied_once_at_start()
    {
        coordinator.Start(state, index);
        sim.IsRunning = true;
        applier.SetChoices(state, index, new Dictionary<PackageName, bool> { [Pkg] = true });
        sim.IsRunning = false;

        var reloaded = store.Load();
        var o = coordinator.Start(reloaded, index);
        coordinator.Start(store.Load(), index);

        Assert.Equal([Pkg], o.Flushed!.Applied);
        Assert.Single(fs.Calls);
        Assert.Empty(store.Load().Pending);
    }

    [Fact]
    public void Start_while_the_sim_runs_reports_pending_as_deferred()
    {
        coordinator.Start(state, index);
        sim.IsRunning = true;
        applier.SetChoices(state, index, new Dictionary<PackageName, bool> { [Pkg] = true });

        var o = coordinator.Start(state, index);

        Assert.True(o.Flushed!.Deferred);
        Assert.Empty(fs.Calls);
        Assert.Null(coordinator.Start(new UserState { Snapshot = [] }, index).Flushed);
    }

    [Fact]
    public void Sim_exit_flushes_pending()
    {
        coordinator.Start(state, index);
        sim.IsRunning = true;
        applier.SetChoices(state, index, new Dictionary<PackageName, bool> { [Pkg] = true });
        sim.IsRunning = false;

        coordinator.OnSimExited(state, index);

        Assert.Single(fs.Calls);
        Assert.Empty(state.Pending);
    }

    [Fact]
    public void A_failed_moved_relink_changes_nothing_stored_and_is_offered_again()
    {
        community.AddJunction("m", @"C:\Sim\Addons\m", targetExists: false);
        coordinator.Start(state, index);
        var o = coordinator.Start(state, index);
        fs.FailOn("m", mNew.Path);

        var r = coordinator.Resolve(state, index, o.Rows, new Dictionary<PackageName, SyncChoice> { [M] = SyncChoice.RestoreApp });

        Assert.Equal(M, r.Failed.Single().Package);
        Assert.Null(state.Packages[M].ActiveCopyPath);
        Assert.Equal(M, coordinator.Start(state, index).Rows.Single().Package);
    }

    [Fact]
    public void A_change_made_while_the_dialog_was_open_is_offered_not_accepted()
    {
        coordinator.Start(state, index);
        community.AddJunction("other", other.Path);
        var o = coordinator.Start(state, index);
        community.AddJunction("pkg", pkg.Path);

        coordinator.Resolve(state, index, o.Rows, KeepAll(o.Rows));

        Assert.Equal([Pkg], coordinator.Start(state, index).Rows.Select(r => r.Package));
    }
}
