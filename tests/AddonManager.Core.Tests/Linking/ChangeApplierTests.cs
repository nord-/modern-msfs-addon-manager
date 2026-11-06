using AddonManager.Core.Community;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Core.Tests.Fakes;
using static AddonManager.Core.Tests.TestData;

namespace AddonManager.Core.Tests.Linking;

public class ChangeApplierTests
{
    private const string CommunityPath = @"C:\MSFS\Community";
    private static readonly LibraryRoot Unplugged = new(@"E:\Addons", LibraryRoot.AllSections);
    private static readonly LibraryRoot Fsdt = new(@"C:\Sim\FSDT Addon Manager\MSFS", LibraryRoot.AllSections);

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
        index = new PackageIndex([pkgCopy, pkgNeofly, bad, good, other, lockedCopy], [SimAddons, Neofly, Fsdt, Unplugged], [Unplugged]);
        applier = NewApplier();
    }

    private ChangeApplier NewApplier() => new(fs, community, store, sim, CommunityPath);

    private static string C(string name) => Path.Combine(CommunityPath, name);

    private static Dictionary<PackageName, bool> On(params string[] names) => names.ToDictionary(n => new PackageName(n), _ => true);

    private static Dictionary<PackageName, bool> Off(params string[] names) => names.ToDictionary(n => new PackageName(n), _ => false);

    [Fact]
    public void A_store_that_refuses_the_change_leaves_every_link_untouched()
    {
        store.RefuseWith = new IOException("locked");

        Assert.Throws<IOException>(() => applier.SetChoices(state, index, On("pkg")));
        Assert.Throws<IOException>(() => applier.AcceptSnapshot(state));

        Assert.Empty(fs.Calls);
        Assert.False(state.IsOn(Pkg));
        Assert.Empty(state.Snapshot!);
        Assert.Equal(0, store.SaveCount);
    }

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
    public void The_active_copy_cannot_change_while_an_entry_holds_the_name()
    {
        var name = new PackageName("neofly-neofly-cargo");
        var libraryCopy = Copy("neofly-neofly-cargo", lib: Neofly);
        var idx = new PackageIndex([libraryCopy], [SimAddons, Neofly]);
        community.AddJunction("neofly-neofly-cargo", @"C:\Elsewhere\neofly-neofly-cargo");

        var result = applier.SetActiveCopy(state, idx, name, libraryCopy.Path);

        var rejected = result.Rejected.Single();
        Assert.Equal(RejectReason.NameTaken, rejected.Reason);
        Assert.Equal(EntryClass.External, rejected.HeldBy);
        Assert.Null(state.Packages.GetValueOrDefault(name)?.ActiveCopyPath);
        Assert.Empty(fs.Calls);
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

    [Theory]
    [InlineData("SetChoices")]
    [InlineData("SetChoicesNeedingNoLink")]
    [InlineData("Relink")]
    [InlineData("RelinkWithNameTaken")]
    [InlineData("ApplyLinks")]
    [InlineData("SetActiveCopy")]
    [InlineData("RemoveBrokenLink")]
    public void Every_change_is_refused_while_the_sim_runs_and_nothing_changes(string change)
    {
        community.AddJunction("gone", @"C:\Old\gone", targetExists: false);
        if (change == "RelinkWithNameTaken")
        {
            community.AddDirectory("pkg", hasManifest: true);
            state.SetOn(Pkg, true);
        }

        sim.IsRunning = true;
        var before = Fingerprint(state);
        var saves = store.SaveCount;

        Assert.Throws<SimRunningException>(() => _ = change switch
        {
            "SetChoices" => applier.SetChoices(state, index, On("pkg")),
            "SetChoicesNeedingNoLink" => applier.SetChoices(state, index, Off("pkg")),
            "Relink" or "RelinkWithNameTaken" => applier.Relink(state, index, [Pkg]),
            "ApplyLinks" => applier.ApplyLinks(state, index, new Dictionary<PackageName, DesiredLink> { [Pkg] = new(true) }),
            "SetActiveCopy" => applier.SetActiveCopy(state, index, Pkg, pkgNeofly.Path),
            _ => applier.RemoveBrokenLink(state, index, "gone"),
        });

        Assert.Equal(before, Fingerprint(state));
        Assert.Equal(saves, store.SaveCount);
        Assert.Empty(fs.Calls);
    }

    [Fact]
    public void The_sim_refusal_text_is_the_one_the_user_sees()
        => Assert.Equal("Close MSFS to change add-ons", new SimRunningException().Message);

    private static string Fingerprint(UserState s)
        => string.Join('|',
            s.Packages.OrderBy(p => p.Key.Value).Select(p => $"{p.Key}:{s.IsOn(p.Key)}:{p.Value.ActiveCopyPath}")
                .Concat(s.Pending.Select(p => $"pending:{p}").Order())
                .Concat(s.Snapshot!.Select(e => $"snap:{e.Name}:{e.Target}")));

    [Fact]
    public void A_no_op_choice_updates_its_snapshot_entry()
    {
        community.AddJunction("pkg", pkgCopy.Path);
        state.Snapshot = SnapshotEntry.From(community.Read());
        community.Remove("pkg");
        state.SetOn(Pkg, true, pkgCopy.Path);

        var r = applier.SetChoices(state, index, Off("pkg"));

        Assert.Empty(fs.Calls);
        Assert.Equal([Pkg], r.Applied);
        Assert.DoesNotContain(state.Snapshot, s => s.Name == "pkg");
    }

    [Fact]
    public void A_create_to_a_gone_target_is_rejected_and_nothing_is_stored()
    {
        fs.Gone(pkgCopy.Path);

        var r = applier.SetChoices(state, index, On("pkg"));

        Assert.Equal(RejectReason.TargetGone, r.Rejected.Single().Reason);
        Assert.Empty(fs.Calls);
        Assert.False(state.IsOn(Pkg));
        Assert.Empty(r.Applied);
    }

    [Fact]
    public void Pending_survives_a_restart_and_is_applied_once()
    {
        state.SetOn(Pkg, true);
        state.Pending.Add(Pkg);
        store.Save(state);

        var first = store.Load();
        NewApplier().FlushPending(first, index);
        var second = store.Load();
        Assert.Empty(second.Pending);
        NewApplier().FlushPending(second, index);

        Assert.Single(fs.Calls);
    }

    [Fact]
    public void FlushPending_while_the_sim_runs_is_still_deferred()
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
    public void Removing_a_not_found_packages_broken_link_keeps_its_stored_choice()
    {
        var vanished = new PackageName("orbx-eham-1-3");
        var target = @"C:\Sim\Addons\orbx-eham-1-3";
        state.SetOn(vanished, true, target);
        community.AddJunction("orbx-eham-1-3", target, targetExists: false);
        state.Snapshot = SnapshotEntry.From(community.Read());

        applier.RemoveBrokenLink(state, index, "orbx-eham-1-3");

        Assert.False(community.Has("orbx-eham-1-3"));
        Assert.True(state.IsOn(vanished));
        Assert.Equal(target, state.Packages[vanished].ActiveCopyPath);
        Assert.True(store.Load().IsOn(vanished));
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
