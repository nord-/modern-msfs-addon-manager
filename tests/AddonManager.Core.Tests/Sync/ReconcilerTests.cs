using AddonManager.Core.Community;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Core.Sync;
using static AddonManager.Core.Tests.TestData;

namespace AddonManager.Core.Tests.Sync;

public class ReconcilerTests
{
    private static readonly LibraryRoot Unplugged = new(@"E:\Addons", LibraryRoot.AllSections);
    private static readonly LibraryRoot Fsdt = new(@"C:\Sim\FSDT Addon Manager\MSFS", LibraryRoot.AllSections);

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
        => index = new PackageIndex([a, b, xOwn, xNeofly, mNew, lk, offCopy], [SimAddons, Neofly, Fsdt, Unplugged], [Unplugged]);

    private static CommunityEntryInfo J(string name, string target, bool exists = true) => new(name, EntryKind.Junction, target, exists, exists);

    private static CommunityEntryInfo J(PackageCopy copy) => J(copy.Name.Value, copy.Path);

    private static CommunityEntryInfo MovedLink => J("m", @"C:\Sim\Addons\m", exists: false);

    private IReadOnlyList<ClassifiedEntry> Classify(params CommunityEntryInfo[] entries) => CommunityClassifier.Classify(entries, index);

    private void Accept(params CommunityEntryInfo[] entries) => state.Snapshot = SnapshotEntry.From(entries);

    private IReadOnlyList<SyncRow> Diff(params CommunityEntryInfo[] current) => Reconciler.Diff(state, Classify(current), index, new HashSet<PackageName>(), new HashSet<PackageName>());

    private IReadOnlyList<SyncRow> DiffNotFound(PackageName notFound, params CommunityEntryInfo[] current)
        => Reconciler.Diff(state, Classify(current), index, new HashSet<PackageName> { notFound }, new HashSet<PackageName>());

    private IReadOnlyList<SyncRow> DiffNotShown(PackageName notShown, params CommunityEntryInfo[] current)
        => Reconciler.Diff(state, Classify(current), index, new HashSet<PackageName>(), new HashSet<PackageName> { notShown });

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
    public void Adopt_counts_a_link_into_an_unreachable_library_as_on()
    {
        Reconciler.Adopt(state, Classify(J("off", @"E:\Addons\off", exists: false)), index);

        Assert.True(state.IsOn(Off));
    }

    [Fact]
    public void A_subset_adoption_keeps_the_stored_choice_of_an_offline_package()
    {
        state.SetOn(Off, true, @"E:\Addons\off");
        state.SetOn(B, false);

        Reconciler.Adopt(state, Classify(J("off", @"E:\Addons\off", exists: false)), index, [Off, B]);

        Assert.True(state.IsOn(Off));
        Assert.Equal(@"E:\Addons\off", state.Packages[Off].ActiveCopyPath);
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
    public void Pending_packages_are_silent()
    {
        state.Pending.Add(A);

        Assert.Empty(Diff(J(a)));
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
    public void A_link_changed_by_another_tool_in_a_registered_library_gives_a_sync_row()
    {
        state.SetOn(Lk, true, lk.Path);
        Accept(J(lk));

        var rows = Diff();

        Assert.Equal([new SyncRow(Lk, SyncReason.LinkDisappeared, null)], rows);
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
    public void A_moved_row_offers_the_users_chosen_copy_not_the_first_one()
    {
        var mOther = Copy("m", @"C:\Sim\Neofly\addons\m", Neofly);
        var twoCopies = new PackageIndex([mNew, mOther], [SimAddons, Neofly]);
        state.SetOn(M, true, mOther.Path);
        var stale = CommunityClassifier.Classify([MovedLink], twoCopies);
        Accept(MovedLink);

        var row = Reconciler.Diff(state, stale, twoCopies, new HashSet<PackageName>(), new HashSet<PackageName>()).Single();

        Assert.Equal(mOther.Path, row.NewTarget);
    }

    private static readonly PackageName Vanished = new("orbx-eham-1-3");

    private static CommunityEntryInfo VanishedLink(bool exists = true) => J("orbx-eham-1-3", @"C:\Sim\Addons\orbx-eham-1-3", exists);

    [Fact]
    public void A_not_found_packages_link_is_broken_and_skips_the_self_check()
    {
        state.SetOn(Vanished, true);
        Accept(VanishedLink());

        // The folder is gone, the link is still there: nothing changed since the snapshot, and "on but not linked" is not asked of a Package that cannot be linked.
        var rows = DiffNotFound(Vanished, VanishedLink(exists: false));

        Assert.Empty(rows);
        Assert.Equal(EntryClass.Broken, Classify(VanishedLink(exists: false)).Single().Class);
    }

    [Fact]
    public void A_not_found_external_packages_unchanged_broken_link_gives_no_row()
    {
        var ex = new PackageName("ex");
        state.SetOn(ex, true, @"D:\Ext\ex");
        Accept(J("ex", @"D:\Ext\ex"));

        var current = J("ex", @"D:\Ext\ex", exists: false);

        Assert.Equal(EntryClass.Broken, Classify(current).Single().Class);
        Assert.Empty(DiffNotFound(ex, current));
    }

    [Fact]
    public void A_broken_link_retargeted_while_not_found_never_stores_the_vanished_target()
    {
        state.SetOn(Vanished, true);
        Accept(VanishedLink());

        var row = Assert.Single(DiffNotFound(Vanished, J("orbx-eham-1-3", @"D:\Elsewhere\orbx-eham-1-3", exists: false)));

        Assert.Equal(SyncReason.LinkRetargeted, row.Reason);
        Assert.False(row.Restorable);
        Reconciler.Resolve(state, [row], Defaults);
        Assert.True(state.IsOn(Vanished));
        Assert.Null(state.Packages[Vanished].ActiveCopyPath);
    }

    [Fact]
    public void A_not_found_packages_link_removed_by_another_tool_offers_only_keep_community()
    {
        state.SetOn(Vanished, true);
        Accept(VanishedLink());

        var row = Assert.Single(DiffNotFound(Vanished));

        Assert.Equal(Vanished, row.Package);
        Assert.Equal(SyncReason.LinkDisappeared, row.Reason);
        Assert.False(row.Restorable);
        Assert.Empty(Diff());

        var relink = Reconciler.Resolve(state, [row], new Dictionary<PackageName, SyncChoice> { [Vanished] = SyncChoice.RestoreApp });

        Assert.Empty(relink);
        Assert.False(state.IsOn(Vanished));
    }

    [Fact]
    public void A_sync_row_for_a_not_shown_package_offers_only_keep_community()
    {
        var row = Assert.Single(DiffNotShown(A, J(a)));

        Assert.Equal(SyncReason.LinkAppeared, row.Reason);
        Assert.False(row.Restorable);
        Assert.True(Diff(J(a)).Single().Restorable);

        var relink = Reconciler.Resolve(state, [row], new Dictionary<PackageName, SyncChoice> { [A] = SyncChoice.RestoreApp });

        Assert.Empty(relink);
        Assert.True(state.IsOn(A));
    }

    [Fact]
    public void A_not_shown_packages_link_re_pointed_to_a_real_duplicate_stores_that_copy_when_kept()
    {
        state.SetOn(X, true, xOwn.Path);
        Accept(J(xOwn));

        var row = Assert.Single(DiffNotShown(X, J(xNeofly)));
        Reconciler.Resolve(state, [row], Defaults);

        Assert.Equal(SyncReason.LinkRetargeted, row.Reason);
        Assert.False(row.Restorable);
        Assert.Equal(xNeofly.Path, state.Packages[X].ActiveCopyPath);
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
