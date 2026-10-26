using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Core.Tests.Fakes;
using static AddonManager.Core.Tests.TestData;

namespace AddonManager.Core.Tests.Linking;

public class ChangeApplierTests
{
    private const string CommunityPath = @"C:\MSFS\Community";
    private static readonly LibraryRoot Unplugged = new(@"E:\Addons");
    private static readonly LibraryRoot Fsdt = new(@"C:\Sim\FSDT Addon Manager\MSFS", ManagedElsewhere: true);

    private static readonly PackageName Pkg = new("pkg");

    private readonly PackageCopy pkgCopy = Copy("pkg"), pkgNeofly = Copy("pkg", lib: Neofly);
    private readonly PackageCopy bad = Copy("bad"), good = Copy("good"), other = Copy("other"), lockedCopy = Copy("lk", lib: Fsdt);

    private readonly FakeCommunity community = new();
    private readonly FakeLinkFileSystem fs;
    private readonly InMemoryUserStateStore store = new();
    private readonly FakeSim sim = new();
    private readonly PackageIndex index;
    private readonly ChangeApplier applier;
    private readonly UserState state = new() { Snapshot = [] };

    public ChangeApplierTests()
    {
        fs = new FakeLinkFileSystem(community);
        index = new PackageIndex([pkgCopy, pkgNeofly, bad, good, other, lockedCopy], [Addons, Neofly, Fsdt, Unplugged], [Unplugged]);
        applier = NewApplier();
    }

    private ChangeApplier NewApplier() => new(fs, community, store, sim, CommunityPath);

    private static string C(string name) => Path.Combine(CommunityPath, name);

    private static Dictionary<PackageName, bool> On(params string[] names) => names.ToDictionary(n => new PackageName(n), _ => true);

    private static Dictionary<PackageName, bool> Off(params string[] names) => names.ToDictionary(n => new PackageName(n), _ => false);

    [Fact]
    public void Turning_on_creates_a_junction_then_saves_choice_copy_and_snapshot_once()
    {
        var r = applier.SetChoices(state, index, On("pkg"));

        Assert.Equal([new FsCall(FsCallKind.Create, C("pkg"), pkgCopy.Path)], fs.Calls);
        Assert.Equal([Pkg], r.Applied);
        Assert.True(state.IsOn(Pkg));
        Assert.Equal(pkgCopy.Path, state.Packages[Pkg].ActiveCopyPath);
        Assert.Equal(1, store.SaveCount);
        Assert.Contains(state.Snapshot!, s => s.Name == "pkg");
        Assert.True(store.Load().IsOn(Pkg));
    }

    [Fact]
    public void Turning_off_removes_the_link_and_the_snapshot_entry()
    {
        applier.SetChoices(state, index, On("pkg"));

        applier.SetChoices(state, index, Off("pkg"));

        Assert.Equal([FsCallKind.Create, FsCallKind.Remove], fs.Calls.Select(c => c.Kind));
        Assert.False(state.IsOn(Pkg));
        Assert.DoesNotContain(state.Snapshot!, s => s.Name == "pkg");
    }

    [Fact]
    public void Name_taken_by_a_fixed_entry_is_rejected_and_not_stored()
    {
        community.AddDirectory("pkg", hasManifest: true);

        var r = applier.SetChoices(state, index, On("PKG"));

        Assert.Equal(RejectReason.NameTaken, r.Rejected.Single().Reason);
        Assert.False(state.IsOn(Pkg));
        Assert.Empty(fs.Calls);
    }

    [Fact]
    public void Turning_off_never_removes_a_fixed_or_external_entry_and_the_choice_sticks()
    {
        community.AddDirectory("pkg", hasManifest: true);
        community.AddJunction("other", @"C:\Elsewhere\other");
        state.SetOn(Pkg, true);
        state.SetOn(new("other"), true);

        applier.SetChoices(state, index, Off("pkg", "other"));

        Assert.Empty(fs.Calls);
        Assert.False(state.IsOn(Pkg));
        Assert.False(state.IsOn(new("other")));
    }

    [Fact]
    public void Locked_package_is_skipped_by_every_action()
    {
        var lk = new PackageName("lk");
        state.SetOn(lk, true);

        Assert.Equal(RejectReason.Locked, applier.SetChoices(state, index, Off("lk")).Rejected.Single().Reason);
        Assert.Equal(RejectReason.Locked, applier.Relink(state, index, [lk]).Rejected.Single().Reason);
        Assert.Equal(RejectReason.Locked, applier.SetActiveCopy(state, index, lk, lockedCopy.Path).Rejected.Single().Reason);
        Assert.Empty(fs.Calls);
        Assert.True(state.IsOn(lk));
    }

    [Fact]
    public void A_failed_link_keeps_the_previous_choice_and_other_packages_still_apply()
    {
        fs.FailOn("bad");

        var r = applier.SetChoices(state, index, On("bad", "good"));

        Assert.False(state.IsOn(new("bad")));
        Assert.True(state.IsOn(new("good")));
        Assert.Equal(new PackageName("bad"), r.Failed.Single().Package);
        Assert.DoesNotContain(state.Snapshot!, s => s.Name == "bad");
    }

    [Fact]
    public void An_unexpected_failure_is_reported_and_completed_links_are_still_saved()
    {
        fs.FailOn("bad", error: new NotSupportedException("unexpected"));

        var r = applier.SetChoices(state, index, On("good", "bad"));

        Assert.Equal(new PackageName("bad"), r.Failed.Single().Package);
        Assert.True(store.Load().IsOn(new("good")));
        Assert.Contains(store.Load().Snapshot!, s => s.Name == "good");
    }

    [Fact]
    public void A_failed_repoint_restores_the_old_link()
    {
        community.AddJunction("pkg", pkgCopy.Path);
        state.SetOn(Pkg, true, pkgCopy.Path);
        fs.FailOn("pkg", pkgNeofly.Path);

        var r = applier.SetActiveCopy(state, index, Pkg, pkgNeofly.Path);

        Assert.Single(r.Failed);
        Assert.Equal(pkgCopy.Path, community.TargetOf("pkg"));
        Assert.Equal(pkgCopy.Path, state.Packages[Pkg].ActiveCopyPath);
    }

    [Fact]
    public void A_foreign_change_made_before_an_apply_is_still_in_the_next_diff()
    {
        community.AddJunction("other", other.Path);

        applier.SetChoices(state, index, On("pkg"));

        Assert.DoesNotContain(state.Snapshot!, s => s.Name == "other");
    }

    [Fact]
    public void While_the_sim_runs_nothing_writes_to_community_and_changes_are_pending()
    {
        sim.IsRunning = true;

        var r = applier.SetChoices(state, index, On("pkg"));

        Assert.True(r.Deferred);
        Assert.Empty(fs.Calls);
        Assert.Contains(Pkg, state.Pending);
        Assert.True(state.IsOn(Pkg));
        Assert.Contains(Pkg, store.Load().Pending);
    }

    [Fact]
    public void While_the_sim_runs_a_rejected_change_is_not_stored()
    {
        sim.IsRunning = true;
        community.AddDirectory("pkg", hasManifest: true);

        var r = applier.SetChoices(state, index, On("pkg"));

        Assert.Equal(RejectReason.NameTaken, r.Rejected.Single().Reason);
        Assert.False(state.IsOn(Pkg));
        Assert.Empty(state.Pending);
    }

    [Fact]
    public void On_off_on_while_running_results_in_one_create_after_the_sim_exits()
    {
        sim.IsRunning = true;
        applier.SetChoices(state, index, On("pkg"));
        applier.SetChoices(state, index, Off("pkg"));
        applier.SetChoices(state, index, On("pkg"));
        sim.IsRunning = false;

        applier.FlushPending(state, index);

        Assert.Equal([FsCallKind.Create], fs.Calls.Select(c => c.Kind));
        Assert.Empty(state.Pending);
    }

    [Fact]
    public void Pending_survives_a_restart_and_is_applied_once()
    {
        sim.IsRunning = true;
        applier.SetChoices(state, index, On("pkg"));
        sim.IsRunning = false;

        var first = store.Load();
        NewApplier().FlushPending(first, index);
        var second = store.Load();
        Assert.Empty(second.Pending);
        NewApplier().FlushPending(second, index);

        Assert.Single(fs.Calls);
    }

    [Fact]
    public void FlushPending_while_the_sim_runs_is_deferred()
    {
        state.Pending.Add(Pkg);
        state.SetOn(Pkg, true);
        sim.IsRunning = true;

        Assert.True(applier.FlushPending(state, index).Deferred);
        Assert.Empty(fs.Calls);
        Assert.Contains(Pkg, state.Pending);
    }

    [Fact]
    public void Pending_for_an_offline_package_stays_pending()
    {
        var offlinePkg = new PackageName("offline");
        state.SetOn(offlinePkg, true, @"E:\Addons\offline");
        state.Pending.Add(offlinePkg);

        var r = applier.FlushPending(state, index);

        Assert.Equal(RejectReason.Offline, r.Rejected.Single().Reason);
        Assert.Contains(offlinePkg, state.Pending);
        Assert.Empty(fs.Calls);
    }

    [Fact]
    public void NameTaken_at_flush_time_turns_the_choice_off_and_reports_it()
    {
        state.SetOn(Pkg, true);
        state.Pending.Add(Pkg);
        community.AddDirectory("pkg", hasManifest: true);

        var r = applier.FlushPending(state, index);

        Assert.False(state.IsOn(Pkg));
        Assert.Equal(RejectReason.NameTaken, r.Rejected.Single().Reason);
        Assert.Empty(state.Pending);
    }

    [Fact]
    public void Changing_the_active_copy_of_a_linked_package_repoints_the_link_and_stores_the_copy()
    {
        community.AddJunction("pkg", pkgCopy.Path);
        state.SetOn(Pkg, true, pkgCopy.Path);

        applier.SetActiveCopy(state, index, Pkg, pkgNeofly.Path);

        Assert.Equal([FsCallKind.Remove, FsCallKind.Create], fs.Calls.Select(c => c.Kind));
        Assert.Equal(pkgNeofly.Path, community.TargetOf("pkg"));
        Assert.Equal(pkgNeofly.Path, state.Packages[Pkg].ActiveCopyPath);
    }

    [Fact]
    public void Changing_the_active_copy_of_an_unlinked_package_only_stores_it()
    {
        applier.SetActiveCopy(state, index, Pkg, pkgNeofly.Path);

        Assert.Empty(fs.Calls);
        Assert.Equal(pkgNeofly.Path, state.Packages[Pkg].ActiveCopyPath);
        Assert.False(state.IsOn(Pkg));
    }

    [Fact]
    public void RemoveBrokenLink_removes_a_broken_link_only()
    {
        community.AddJunction("gone", @"C:\Old\gone", targetExists: false);
        community.AddDirectory("pkg", hasManifest: true);
        state.Snapshot = SnapshotEntry.From(community.Read());

        applier.RemoveBrokenLink(state, index, "gone");

        Assert.False(community.Has("gone"));
        Assert.DoesNotContain(state.Snapshot!, s => s.Name == "gone");
        Assert.Throws<InvalidOperationException>(() => applier.RemoveBrokenLink(state, index, "pkg"));
        Assert.Throws<InvalidOperationException>(() => applier.RemoveBrokenLink(state, index, "missing"));
    }

    [Fact]
    public void RemoveBrokenLink_while_the_sim_runs_does_nothing()
    {
        community.AddJunction("gone", @"C:\Old\gone", targetExists: false);
        sim.IsRunning = true;

        Assert.True(applier.RemoveBrokenLink(state, index, "gone").Deferred);
        Assert.True(community.Has("gone"));
    }

    [Fact]
    public void AcceptSnapshot_replaces_the_whole_snapshot_and_saves()
    {
        community.AddJunction("other", other.Path);

        applier.AcceptSnapshot(state);

        Assert.Equal(["other"], state.Snapshot!.Select(s => s.Name));
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void A_failed_link_at_flush_time_stays_pending_and_keeps_its_choice()
    {
        state.SetOn(Pkg, true);
        state.Pending.Add(Pkg);
        fs.FailOn("pkg");

        var r = applier.FlushPending(state, index);

        Assert.Equal(Pkg, r.Failed.Single().Package);
        Assert.Contains(Pkg, state.Pending);
        Assert.True(state.IsOn(Pkg));
    }

    [Fact]
    public void When_the_old_link_cant_be_restored_both_failures_are_reported()
    {
        community.AddJunction("pkg", pkgCopy.Path);
        state.SetOn(Pkg, true, pkgCopy.Path);
        fs.FailOn("pkg", pkgNeofly.Path);
        fs.FailOn("pkg", pkgCopy.Path);

        var r = applier.SetActiveCopy(state, index, Pkg, pkgNeofly.Path);

        var failure = r.Failed.Single();
        Assert.Equal(Pkg, failure.Package);
        Assert.Contains("could not restore", failure.Error);
        Assert.Equal(pkgCopy.Path, state.Packages[Pkg].ActiveCopyPath);
    }
}
