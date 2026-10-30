using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Core.Sync;
using AddonManager.Core.Tests.Cards;
using AddonManager.Core.Tests.Fakes;
using static AddonManager.Core.Tests.Content.ContentBuilder;
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
        index = new PackageIndex([pkg, other, mNew], [SimAddons]);
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
        // The fake restores the old link as an existing one; on disk it stays broken, so classify it as Stale again.
        community.AddJunction("m", @"C:\Sim\Addons\m", targetExists: false);
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

    private static readonly LibraryRoot LibB = new(@"C:\B", LibraryRoot.AllSections);
    private static readonly PackageName Esgg = new("esgg"), PB = new("b"), PC = new("c");

    private PackageIndex IndexOf(IEnumerable<PackageCopy> copies, params LibraryRoot[] libraries) => new(copies, libraries);

    /// <summary>A normal start with Library A, then a Community that links b into the not yet registered Library B.</summary>
    private (PackageCopy bB, PackageCopy c, PackageIndex indexB) StartedWithForeignLink()
    {
        var bB = Copy("b", lib: LibB);
        var c = Copy("c", lib: LibB);
        community.AddJunction("b", bB.Path);
        coordinator.Start(state, index);
        return (bB, c, IndexOf([pkg, other, mNew, bB, c], SimAddons, LibB));
    }

    [Fact]
    public void First_run_offers_stale_links_for_re_point()
    {
        var esgg = Copy("esgg", @"C:\Sim\Addons\Scenery\esgg");
        community.AddJunction("esgg", @"C:\Sim\Addons\esgg", targetExists: false);

        var o = coordinator.Start(state, IndexOf([esgg], SimAddons));

        Assert.True(o.FirstRun);
        Assert.Equal([new SyncRow(Esgg, SyncReason.Moved, esgg.Path)], o.Rows);
        Assert.True(state.IsOn(Esgg));
    }

    [Fact]
    public void Registering_a_library_adopts_its_packages_without_sync_rows()
    {
        var (_, _, indexB) = StartedWithForeignLink();

        var rows = coordinator.RegisterLibrary(state, LibB, indexB);

        Assert.Empty(rows);
        Assert.True(state.IsOn(PB));
        Assert.False(state.IsOn(PC));
        Assert.Contains(LibB, state.Libraries);
        Assert.Empty(coordinator.Start(state, indexB).Rows);
    }

    [Fact]
    public void Registering_a_library_keeps_other_user_data_of_known_packages()
    {
        var (bB, _, indexB) = StartedWithForeignLink();
        state.Packages[PB] = new PackageState(false, bB.Path);
        state.Content.OptedIn.Add(PB);

        coordinator.RegisterLibrary(state, LibB, indexB);

        Assert.Equal(new PackageState(true, bB.Path), state.Packages[PB]);
        Assert.Contains(PB, state.Content.OptedIn);
    }

    private (PackageCopy bA, PackageCopy bB, PackageIndex indexAB, IReadOnlyList<SyncRow> rows) RegisterWithOtherActiveCopy()
    {
        var (bB, _, _) = StartedWithForeignLink();
        var bA = Copy("b");
        var indexAB = IndexOf([pkg, other, mNew, bA, bB], SimAddons, LibB);
        state.Packages[PB] = new PackageState(false, bA.Path);
        return (bA, bB, indexAB, coordinator.RegisterLibrary(state, LibB, indexAB));
    }

    [Fact]
    public void A_link_to_another_copy_than_the_stored_active_copy_is_offered_as_a_row()
    {
        var (_, bB, indexAB, rows) = RegisterWithOtherActiveCopy();

        Assert.Equal([new SyncRow(PB, SyncReason.LinkRetargeted, bB.Path)], rows);

        coordinator.Resolve(state, indexAB, rows, KeepAll(rows));

        Assert.Equal(bB.Path, state.Packages[PB].ActiveCopyPath);
        Assert.True(state.IsOn(PB));
    }

    [Fact]
    public void Restoring_the_stored_active_copy_re_points_the_link()
    {
        var (bA, _, indexAB, rows) = RegisterWithOtherActiveCopy();

        coordinator.Resolve(state, indexAB, rows, new Dictionary<PackageName, SyncChoice> { [PB] = SyncChoice.RestoreApp });

        Assert.Equal(bA.Path, community.TargetOf("b"));
        Assert.Equal(bA.Path, state.Packages[PB].ActiveCopyPath);
    }

    [Fact]
    public void A_stored_copy_that_vanished_gives_way_to_the_linked_copy_without_a_row()
    {
        var (bB, _, indexB) = StartedWithForeignLink();
        state.Packages[PB] = new PackageState(false, Copy("b").Path);

        var rows = coordinator.RegisterLibrary(state, LibB, indexB);

        Assert.Empty(rows);
        Assert.Equal(new PackageState(true, bB.Path), state.Packages[PB]);
    }

    [Fact]
    public void A_stored_copy_on_an_unreachable_library_is_kept_and_the_link_is_offered_as_a_row()
    {
        var (bB, _, _) = StartedWithForeignLink();
        var drive = new LibraryRoot(@"E:\Addons", LibraryRoot.AllSections);
        var onDrive = Copy("b", lib: drive);
        var indexB = new PackageIndex([pkg, other, mNew, bB], [SimAddons, LibB, drive], [drive]);
        state.Packages[PB] = new PackageState(false, onDrive.Path);

        var rows = coordinator.RegisterLibrary(state, LibB, indexB);

        Assert.Equal([new SyncRow(PB, SyncReason.LinkRetargeted, bB.Path)], rows);
        Assert.Equal(new PackageState(true, onDrive.Path), state.Packages[PB]);
    }

    [Fact]
    public void Registering_a_library_offers_no_stale_row_for_a_pending_package()
    {
        coordinator.Start(state, index);
        var esgg = Copy("esgg", @"C:\B\Scenery\esgg", LibB);
        community.AddJunction("esgg", @"C:\B\esgg", targetExists: false);
        state.SetOn(Esgg, true);
        state.Pending.Add(Esgg);

        var rows = coordinator.RegisterLibrary(state, LibB, IndexOf([pkg, other, mNew, esgg], SimAddons, LibB));

        Assert.Empty(rows);
        Assert.Contains(Esgg, state.Pending);
    }

    [Fact]
    public void Registering_a_library_leaves_pending_packages_alone()
    {
        var (_, _, indexB) = StartedWithForeignLink();
        community.Remove("b");
        state.SetOn(PB, true);
        state.Pending.Add(PB);

        coordinator.RegisterLibrary(state, LibB, indexB);

        Assert.True(state.IsOn(PB));
        Assert.Contains(PB, state.Pending);
    }

    [Fact]
    public void Registering_a_library_offers_its_stale_links()
    {
        coordinator.Start(state, index);
        var esgg = Copy("esgg", @"C:\B\Scenery\esgg", LibB);
        community.AddJunction("esgg", @"C:\B\esgg", targetExists: false);

        var rows = coordinator.RegisterLibrary(state, LibB, IndexOf([pkg, other, mNew, esgg], SimAddons, LibB));

        Assert.Equal([new SyncRow(Esgg, SyncReason.Moved, esgg.Path)], rows);
        Assert.True(state.IsOn(Esgg));
    }

    [Fact]
    public void Unregistering_a_library_keeps_user_data_and_gives_no_sync_rows()
    {
        var (_, _, indexB) = StartedWithForeignLink();
        coordinator.RegisterLibrary(state, LibB, indexB);
        var before = state.Packages[PB];

        coordinator.UnregisterLibrary(state, LibB);

        Assert.DoesNotContain(LibB, state.Libraries);
        Assert.Empty(coordinator.Start(state, index).Rows);
        Assert.Equal(before, state.Packages[PB]);
    }

    [Fact]
    public void A_link_replaced_by_an_external_one_gives_a_row_that_cannot_be_restored()
    {
        var esgg = Copy("esgg");
        var idx = IndexOf([esgg], SimAddons);
        community.AddJunction("esgg", esgg.Path);
        coordinator.Start(state, idx);
        community.AddJunction("esgg", @"D:\Elsewhere\esgg");

        var rows = coordinator.Start(state, idx).Rows;

        var row = Assert.Single(rows);
        Assert.Equal(SyncReason.LinkDisappeared, row.Reason);
        Assert.False(row.Restorable);

        coordinator.Resolve(state, idx, rows, new Dictionary<PackageName, SyncChoice> { [Esgg] = SyncChoice.RestoreApp });

        Assert.False(state.IsOn(Esgg));
        Assert.Empty(fs.Calls);
    }

    [Fact]
    public void Unregistering_a_library_gives_no_row_when_the_package_has_a_copy_elsewhere()
    {
        var bA = Copy("b");
        var bB = Copy("b", lib: LibB);
        var both = IndexOf([bA, bB], SimAddons, LibB);
        state.Libraries.Add(LibB);
        community.AddJunction("b", bB.Path);
        coordinator.Start(state, both);
        Assert.True(state.IsOn(PB));

        coordinator.UnregisterLibrary(state, LibB);
        var rows = coordinator.Start(state, IndexOf([bA], SimAddons)).Rows;

        Assert.Empty(rows);
    }

    [Fact]
    public void Registering_a_folder_for_another_section_keeps_its_position()
    {
        var bA = Copy("b");
        var bB = Copy("b", lib: LibB);
        var both = IndexOf([bA, bB], LibB, SimAddons);
        state.Libraries.AddRange([new LibraryRoot(LibB.Path, [Category.Scenery]), new LibraryRoot(SimAddons.Path, [Category.Scenery])]);
        coordinator.Start(state, both);
        state.SetOn(PB, true, bB.Path);

        var rows = coordinator.RegisterLibrary(state, LibB.Path, Category.Other, both);

        Assert.Empty(rows);
        Assert.Equal([LibB.Path, SimAddons.Path], state.Libraries.Select(l => l.Path));
        Assert.Equal([Category.Scenery, Category.Other], state.Libraries[0].Sections);
        Assert.Equal(new PackageState(true, bB.Path), state.Packages[PB]);
        Assert.Empty(fs.Calls);
        Assert.Equal(state.Libraries[0].Sections, store.Load().Libraries[0].Sections);
    }

    [Fact]
    public void Registering_scenery_for_a_known_folder_then_settling_gives_one_eham_card()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons", Category.Other);
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Place();
        Assert.Empty(w.State.Cards);

        var rows = coordinator.RegisterLibrary(w.State, @"C:\Sim\Addons", Category.Scenery, w.Input().Index);
        w.Place();

        Assert.Empty(rows);
        var card = Assert.Single(w.State.Cards);
        Assert.Equal(Category.Scenery, card.Section);
        Assert.Equal(CardTab.Airports, card.Tab);
        Assert.Equal([Code("EHAM")], card.Codes);
    }

    [Fact]
    public void Removing_a_section_keeps_the_cards_and_the_managed_entries()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons", Category.Scenery, Category.Other);
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Linked("eham-x");
        w.Place();
        w.State.SetOn(new PackageName("eham-x"), true);
        var card = Assert.Single(w.State.Cards);

        coordinator.UnregisterLibrary(w.State, @"C:\Sim\Addons", Category.Scenery);

        Assert.Equal([Category.Other], w.State.Libraries.Single().Sections);
        Assert.Same(card, Assert.Single(w.State.Cards));
        Assert.Equal(EntryClass.Managed, Assert.Single(w.Input().Community).Class);
        Assert.True(w.State.IsOn(new PackageName("eham-x")));
    }

    [Fact]
    public void Removing_a_section_moves_the_unplaced_packages_it_made_eligible_to_not_shown()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons", Category.Scenery, Category.Other);
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Place();
        w.State.SetCard(PackageKey.Folder(new PackageName("eham-x")), null);
        w.State.RemoveEmptyCards();

        coordinator.UnregisterLibrary(w.State, @"C:\Sim\Addons", Category.Scenery);

        var board = BoardBuilder.Build(w.State, w.Input());
        Assert.Empty(board.Cards);
        Assert.Equal("eham-x", Assert.Single(board.NotShown).Name.Value);
    }

    [Fact]
    public void Removing_the_last_section_turns_the_links_external_and_keeps_the_cards()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons", Category.Scenery);
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Linked("eham-x");
        w.Place();
        var card = Assert.Single(w.State.Cards);

        coordinator.UnregisterLibrary(w.State, @"C:\Sim\Addons", Category.Scenery);

        Assert.Empty(w.State.Libraries);
        Assert.Same(card, Assert.Single(w.State.Cards));
        Assert.Equal(EntryClass.External, Assert.Single(w.Input().Community).Class);
        Assert.Contains(new PackageName("eham-x"), w.State.Packages.Keys);
    }

    [Fact]
    public void At_startup_a_not_found_packages_vanished_link_gives_one_keep_only_row()
    {
        state.Libraries.Add(SimAddons);
        community.AddJunction("pkg", pkg.Path);
        coordinator.Start(state, index);
        var card = state.NewCard(Category.Scenery, CardTab.Landscapes, [], DateTimeOffset.UnixEpoch);
        state.Packages[Pkg] = state.Packages[Pkg] with
        {
            CardId = card.Id,
            Memo = new PackageMemo(pkg.Path, PackageSource.Library, "Pkg", "SCENERY", null, null, BglPlacement.Places, [], [], AircraftRole.None, []),
        };

        // The Package's folder is gone and another tool removed its link; the board built from cached data shows it as Not found.
        community.Remove("pkg");
        var goneIndex = IndexOf([other, mNew], SimAddons);
        var board = BoardBuilder.Build(state, BoardInput.WithoutContent(goneIndex, CommunityClassifier.Classify(community.Read(), goneIndex)));
        Assert.Equal(Presence.NotFound, board.CardOf(PackageKey.Folder(Pkg))!.Packages.Single(p => p.Name == Pkg).Presence);

        var o = coordinator.Start(state, goneIndex, board);

        var row = Assert.Single(o.Rows);
        Assert.Equal(Pkg, row.Package);
        Assert.Equal(SyncReason.LinkDisappeared, row.Reason);
        Assert.False(row.Restorable);

        coordinator.Resolve(state, goneIndex, o.Rows, new Dictionary<PackageName, SyncChoice> { [Pkg] = SyncChoice.RestoreApp }, board);

        Assert.False(state.IsOn(Pkg));
        Assert.Empty(fs.Calls);
    }
}

/// <summary>Most coordinator tests have no content scan: they pass a board with no Not found Packages.</summary>
internal static class SyncCoordinatorTestExtensions
{
    private static readonly Board NoBoard = new([], [], new Banner([], [], 0, 0, [], []));

    public static StartupOutcome Start(this SyncCoordinator coordinator, UserState state, PackageIndex index)
        => coordinator.Start(state, index, NoBoard);

    /// <summary>Registers a Library for each of its sections; a new folder is adopted by the first, the rest only add their section.</summary>
    public static IReadOnlyList<SyncRow> RegisterLibrary(this SyncCoordinator coordinator, UserState state, LibraryRoot library, PackageIndex index)
    {
        var rows = coordinator.RegisterLibrary(state, library.Path, library.Sections[0], index);
        foreach (var section in library.Sections.Skip(1))
            coordinator.RegisterLibrary(state, library.Path, section, index);
        return rows;
    }

    public static void UnregisterLibrary(this SyncCoordinator coordinator, UserState state, LibraryRoot library)
    {
        foreach (var section in library.Sections)
            coordinator.UnregisterLibrary(state, library.Path, section);
    }

    public static ApplyResult Resolve(
        this SyncCoordinator coordinator,
        UserState state,
        PackageIndex index,
        IReadOnlyList<SyncRow> rows,
        IReadOnlyDictionary<PackageName, SyncChoice> choices)
        => coordinator.Resolve(state, index, rows, choices, NoBoard);
}
