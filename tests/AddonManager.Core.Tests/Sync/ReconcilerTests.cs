using AddonManager.Core.Community;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Core.Sync;
using static AddonManager.Core.Tests.TestData;

namespace AddonManager.Core.Tests.Sync;

public class ReconcilerTests
{
    private static readonly LibraryRoot Unplugged = new(@"E:\Addons");
    private static readonly LibraryRoot Fsdt = new(@"C:\Sim\FSDT Addon Manager\MSFS", ManagedElsewhere: true);

    private static readonly PackageName A = new("a"), B = new("b"), M = new("m"), X = new("x"), Lk = new("lk"), Off = new("off");

    private readonly PackageCopy a = Copy("a"), b = Copy("b");
    private readonly PackageCopy xOwn = Copy("x"), xNeofly = Copy("x", lib: Neofly);
    private readonly PackageCopy mNew = Copy("m", @"C:\Sim\Addons\Scenery\m");
    private readonly PackageCopy lk = Copy("lk", lib: Fsdt);
    private readonly PackageCopy offCopy = Copy("off");
    private readonly PackageIndex index;
    private readonly UserState state = new() { Snapshot = [] };

    private static readonly Dictionary<PackageName, SyncChoice> Defaults = [];

    public ReconcilerTests()
        => index = new PackageIndex([a, b, xOwn, xNeofly, mNew, lk, offCopy], [Addons, Neofly, Fsdt, Unplugged], [Unplugged]);

    private static CommunityEntryInfo J(string name, string target, bool exists = true) => new(name, EntryKind.Junction, target, exists, exists);

    private static CommunityEntryInfo J(PackageCopy copy) => J(copy.Name.Value, copy.Path);

    private static CommunityEntryInfo MovedLink => J("m", @"C:\Sim\Addons\m", exists: false);

    private IReadOnlyList<ClassifiedEntry> Classify(params CommunityEntryInfo[] entries) => CommunityClassifier.Classify(entries, index);

    private void Accept(params CommunityEntryInfo[] entries) => state.Snapshot = SnapshotEntry.From(entries);

    private IReadOnlyList<SyncRow> Diff(params CommunityEntryInfo[] current) => Reconciler.Diff(state, Classify(current), index);

    [Fact]
    public void First_run_produces_no_rows()
    {
        state.Snapshot = null;

        Assert.Empty(Diff(J(a)));
    }

    [Fact]
    public void Adopt_turns_on_exactly_the_linked_packages_and_stores_their_copy()
    {
        Reconciler.Adopt(state, Classify(J(xNeofly), J(a)), index);

        Assert.True(state.IsOn(X));
        Assert.True(state.IsOn(A));
        Assert.False(state.IsOn(B));
        Assert.Equal(xNeofly.Path, state.Packages[X].ActiveCopyPath);
    }

    [Fact]
    public void Adopt_then_default_choice_on_a_moved_row_repoints_instead_of_removing()
    {
        Reconciler.Adopt(state, Classify(MovedLink), index);
        Accept(MovedLink);
        var moved = Diff(MovedLink).Single();

        var relink = Reconciler.Resolve(state, [moved], Defaults);

        Assert.Equal(new DesiredLink(true, mNew.Path), relink[M]);
        Assert.Equal(mNew.Path, Assert.Single(relink).Value.CopyPath);
    }

    [Fact]
    public void Offline_packages_produce_no_rows_and_keep_their_choice()
    {
        var offline = J("off", @"E:\Addons\off", exists: false);
        state.SetOn(Off, true, @"E:\Addons\off");
        Accept(offline);

        Assert.Empty(Diff(offline));
        Assert.Empty(Diff());
        Assert.True(state.IsOn(Off));
    }

    [Fact]
    public void A_link_to_a_package_under_another_name_doesnt_produce_a_row_for_the_package_name()
    {
        Assert.Empty(Diff(J("zzz-a", a.Path)));
    }

    [Fact]
    public void An_unchanged_link_named_after_a_package_to_an_existing_non_package_folder_is_silent()
    {
        var ignored = J("a", @"C:\Sim\Addons\Foo", exists: true) with { HasManifest = false };
        Accept(ignored);

        Assert.Empty(Diff(ignored));
    }

    [Fact]
    public void Link_added_by_another_tool_is_LinkAppeared()
    {
        var row = Diff(J(a)).Single();

        Assert.Equal(new SyncRow(A, SyncReason.LinkAppeared, a.Path), row);
        Assert.Equal(SyncChoice.KeepCommunity, row.DefaultChoice);
    }

    [Fact]
    public void Link_removed_by_another_tool_is_LinkDisappeared()
    {
        state.SetOn(A, true);
        Accept(J(a));

        var row = Diff().Single();

        Assert.Equal(A, row.Package);
        Assert.Equal(SyncReason.LinkDisappeared, row.Reason);
    }

    [Fact]
    public void Link_pointed_to_another_copy_is_LinkRetargeted_and_keeping_it_stores_the_copy()
    {
        state.SetOn(X, true, xOwn.Path);
        Accept(J(xOwn));

        var row = Diff(J(xNeofly)).Single();
        Reconciler.Resolve(state, [row], Defaults);

        Assert.Equal(new SyncRow(X, SyncReason.LinkRetargeted, xNeofly.Path), row);
        Assert.Equal(xNeofly.Path, state.Packages[X].ActiveCopyPath);
    }

    [Fact]
    public void Changes_to_external_fixed_and_offline_entries_are_silent()
    {
        var entries = new[]
        {
            new CommunityEntryInfo("fixed", EntryKind.Directory, null, false, true),
            J("ext", @"C:\Other\ext"),
            J("gone-offline", @"E:\Addons\gone-offline", exists: false),
            new CommunityEntryInfo("readme.txt", EntryKind.File, null, false, false),
        };

        Assert.Empty(Diff(entries));
        Accept(entries);
        Assert.Empty(Diff());
    }

    [Fact]
    public void Locked_and_pending_packages_are_silent()
    {
        state.Pending.Add(A);

        Assert.Empty(Diff(J(a), J(lk)));
    }

    [Fact]
    public void Moved_package_defaults_to_restoring_the_link_at_the_new_location()
    {
        state.SetOn(M, true);
        Accept(MovedLink);

        var row = Diff(MovedLink).Single();

        Assert.Equal(SyncReason.Moved, row.Reason);
        Assert.Equal(mNew.Path, row.NewTarget);
        Assert.Equal(SyncChoice.RestoreApp, row.DefaultChoice);
    }

    [Fact]
    public void Self_check_finds_on_but_not_linked_even_when_community_matches_snapshot()
    {
        state.SetOn(A, true);
        Accept(J(b));

        var rows = Diff(J(b));

        Assert.Equal([new SyncRow(A, SyncReason.OnButNotLinked, null), new SyncRow(B, SyncReason.OffButLinked, b.Path)], rows);
    }

    [Fact]
    public void Locked_packages_follow_community_silently()
    {
        state.SetOn(Lk, true);
        Reconciler.FollowLocked(state, Classify(), index);
        Assert.False(state.IsOn(Lk));

        Reconciler.FollowLocked(state, Classify(J(lk)), index);
        Assert.True(state.IsOn(Lk));
    }

    [Fact]
    public void KeepCommunity_on_a_moved_row_turns_off_and_returns_the_package_so_the_stale_link_is_removed()
    {
        state.SetOn(M, true);
        Accept(MovedLink);
        var row = Diff(MovedLink).Single();

        var relink = Reconciler.Resolve(state, [row], new Dictionary<PackageName, SyncChoice> { [M] = SyncChoice.KeepCommunity });

        Assert.Equal(new DesiredLink(false), relink[M]);
        Assert.True(state.IsOn(M));
    }

    [Fact]
    public void KeepCommunity_sets_the_choice_and_RestoreApp_returns_the_package_to_relink()
    {
        state.SetOn(B, true);
        Accept(J(b));
        var rows = Diff(J(a));
        var appeared = rows.Single(r => r.Package == A);
        var disappeared = rows.Single(r => r.Package == B);

        var relink = Reconciler.Resolve(state, [appeared, disappeared],
            new Dictionary<PackageName, SyncChoice> { [A] = SyncChoice.KeepCommunity, [B] = SyncChoice.RestoreApp });

        Assert.True(state.Packages[A].On);
        Assert.True(state.Packages[B].On);
        Assert.Equal([B], relink.Keys);
    }

    [Fact]
    public void A_locked_package_on_an_unplugged_library_keeps_its_choice()
    {
        // The stored copy is offline, but a reachable duplicate in a Managed elsewhere Library makes the Package look locked.
        var lkOffline = new PackageName("lk");
        state.SetOn(lkOffline, true, @"E:\Addons\lk");

        Reconciler.FollowLocked(state, Classify(), index);

        Assert.True(state.IsOn(lkOffline));
    }

    [Fact]
    public void A_moved_row_offers_the_users_chosen_copy_not_the_first_one()
    {
        var mOther = Copy("m", @"C:\Sim\Neofly\addons\m", Neofly);
        var twoCopies = new PackageIndex([mNew, mOther], [Addons, Neofly]);
        state.SetOn(M, true, mOther.Path);
        var stale = CommunityClassifier.Classify([MovedLink], twoCopies);
        Accept(MovedLink);

        var row = Reconciler.Diff(state, stale, twoCopies).Single();

        Assert.Equal(mOther.Path, row.NewTarget);
    }

    [Fact]
    public void Removing_an_accepted_link_to_a_non_package_folder_is_silent()
    {
        // "a" is also a Package elsewhere in the Library, but this link never pointed at it.
        var ignored = J("a", @"C:\Sim\Addons\Foo") with { HasManifest = false };
        Accept(ignored);

        Assert.Empty(Diff());
    }
}
