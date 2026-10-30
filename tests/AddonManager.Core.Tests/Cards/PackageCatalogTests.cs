using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class PackageCatalogTests
{
    private const string SimAddons = @"C:\Sim\Addons";

    private static World WorldWithLibrary(params Category[] sections)
    {
        var w = new World();
        w.Library(SimAddons, sections);
        return w;
    }

    private static IReadOnlyDictionary<PackageName, PackageSummary> Packages(World w) => PackageCatalog.Build(w.State, w.Input());

    private static bool Remember(World w) => PackageCatalog.Remember(w.State, Packages(w), w.Input());

    private static PackageSummary Of(IReadOnlyDictionary<PackageName, PackageSummary> packages, string name) => packages[new PackageName(name)];

    private static void Place(World w, params string[] names)
        => w.Group(Category.Scenery, CardTab.Landscapes, names);

    [Fact]
    public void A_library_package_with_content_is_found_with_its_facts()
    {
        var w = WorldWithLibrary(Category.Scenery);
        w.Add(Scenery("eham-x", ["EHAM"]));

        var s = Of(Packages(w), "eham-x");

        Assert.Equal(Presence.Found, s.Presence);
        Assert.Equal(PackageSource.Library, s.Source);
        Assert.Equal(LockReason.None, s.Lock);
        Assert.Equal(Category.Scenery, s.Section);
        Assert.Equal([Code("EHAM")], s.Codes);
        Assert.Equal([Category.Scenery], s.LibrarySections);
        Assert.True(s.HasContent);
        Assert.Equal(new LibraryRoot(SimAddons, LibraryRoot.AllSections), s.Library);
        Assert.Equal([Path.Combine(SimAddons, "eham-x")], s.Copies);
    }

    [Fact]
    public void A_package_without_content_is_found_with_its_provisional_category()
    {
        var w = WorldWithLibrary();
        w.Add(Build("ac-x", "AIRCRAFT"));

        var s = Of(PackageCatalog.Build(w.State, w.Input() with { Contents = [] }), "ac-x");

        Assert.Equal(Presence.Found, s.Presence);
        Assert.False(s.HasContent);
        Assert.Equal(Category.Aircraft, s.Section);
        Assert.Empty(s.Codes);
    }

    [Fact]
    public void A_library_package_whose_name_an_external_entry_holds_is_one_locked_package()
    {
        const string target = @"C:\Sim\Neofly\addons\neofly-neofly-cargo";
        var w = WorldWithLibrary();
        w.Add(Scenery("neofly-neofly-cargo"));
        w.External(Scenery("neofly-neofly-cargo"), target);

        var packages = Packages(w);

        var s = Assert.Single(packages).Value;
        Assert.Equal(PackageSource.Community, s.Source);
        Assert.Equal(LockReason.HeldByEntry, s.Lock);
        Assert.Equal(EntryClass.External, s.Holder!.Class);
        Assert.Equal(target, s.Path);
        Assert.True(s.HasContent);
        Assert.Equal([Path.Combine(SimAddons, "neofly-neofly-cargo")], s.Copies);
        Assert.Empty(s.LibrarySections);
        Assert.Null(s.Library);
    }

    [Fact]
    public void A_not_found_package_whose_name_an_ignored_entry_holds_stays_name_taken()
    {
        var w = WorldWithLibrary(Category.Scenery);
        w.Add(Scenery("x"));
        Place(w, "x");
        Assert.True(Remember(w));

        w.Without("x");
        w.Ignored("x");
        var s = Of(Packages(w), "x");

        Assert.Equal(Presence.NotFound, s.Presence);
        Assert.Equal(LockReason.NameTaken, s.Lock);
        Assert.Equal(EntryClass.Ignored, s.Holder!.Class);
    }

    [Fact]
    public void A_name_held_by_an_ignored_entry_is_name_taken()
    {
        var w = WorldWithLibrary();
        w.Add(Scenery("x"));
        w.Ignored("x");

        var s = Of(Packages(w), "x");

        Assert.Equal(LockReason.NameTaken, s.Lock);
        Assert.Equal(EntryClass.Ignored, s.Holder!.Class);
        Assert.Equal(PackageSource.Library, s.Source);
    }

    [Fact]
    public void Fixed_and_external_entries_are_locked_packages()
    {
        var w = WorldWithLibrary();
        w.Fixed(Scenery("fx"));
        w.External(Scenery("ex"), @"D:\Ext\ex");

        var packages = Packages(w);

        foreach (var name in new[] { "fx", "ex" })
        {
            var s = Of(packages, name);
            Assert.Equal(PackageSource.Community, s.Source);
            Assert.Equal(LockReason.HeldByEntry, s.Lock);
            Assert.Equal(Presence.Found, s.Presence);
            Assert.True(s.HasContent);
        }

        Assert.Equal(@"C:\Sim\Community\fx", Of(packages, "fx").Path);
        Assert.Equal(EntryClass.Fixed, Of(packages, "fx").Holder!.Class);
    }

    [Fact]
    public void Sim_managed_content_is_never_a_package()
    {
        var w = WorldWithLibrary();
        w.SimManaged(Scenery("fs-base-x", ["EHAM"]));

        Assert.Empty(Packages(w));
    }

    [Fact]
    public void A_vanished_package_is_not_found_with_its_remembered_facts()
    {
        var w = WorldWithLibrary(Category.Scenery);
        w.Add(Scenery("eham-x", ["EHAM"], title: "Schiphol"));
        Place(w, "eham-x");
        Assert.True(Remember(w));
        w.State.Packages[new PackageName("eham-x")] = w.State.Packages[new PackageName("eham-x")] with { Section = Category.Scenery };

        w.Without("eham-x");
        var s = Of(Packages(w), "eham-x");

        Assert.Equal(Presence.NotFound, s.Presence);
        Assert.False(s.HasContent);
        Assert.Equal("Schiphol", s.DisplayName);
        Assert.Equal(Category.Scenery, s.Section);
        Assert.Equal([Code("EHAM")], s.Codes);
        Assert.Equal([Category.Scenery], s.LibrarySections);
        Assert.Equal(Path.Combine(SimAddons, "eham-x"), s.Path);
    }

    [Fact]
    public void A_package_under_an_unreachable_library_is_library_not_found()
    {
        var w = new World();
        var lib = w.Library(SimAddons);
        w.Add(Scenery("x", ["EHAM"]));
        Remember(w);

        w.Unreachable(lib);
        var s = Of(Packages(w), "x");

        Assert.Equal(Presence.LibraryNotFound, s.Presence);
        Assert.Equal([Code("EHAM")], s.Codes);
    }

    [Fact]
    public void A_package_whose_stored_copy_is_library_not_found_is_library_not_found_even_with_a_reachable_duplicate()
    {
        var w = new World();
        var a = w.Library(SimAddons);
        var b = w.Library(@"C:\Sim\Other");
        w.Add(Scenery("x"), library: a);
        var onB = w.Add(Scenery("x"), library: b);
        w.State.SetOn(new PackageName("x"), true, onB.Path);
        w.Unreachable(b);

        var s = Of(Packages(w), "x");

        Assert.Equal(Presence.LibraryNotFound, s.Presence);
        Assert.Equal(onB.Path, s.Path);
        Assert.Equal(b, s.Library);
        Assert.Equal([Path.Combine(SimAddons, "x")], s.Copies);
        var board = BoardBuilder.Build(w.State, w.Input());
        var card = Assert.Single(board.Cards);
        Assert.Equal(@"Library not found — C:\Sim\Other can't be reached", Assert.Single(card.Packages).DisabledReason);
    }

    [Fact]
    public void A_package_last_seen_under_no_registered_library_is_left_out()
    {
        var w = WorldWithLibrary();
        w.Add(Scenery("x"));
        Remember(w);

        w.Without("x");
        w.State.Libraries.Clear();

        Assert.Empty(Packages(w));
    }

    [Fact]
    public void A_vanished_external_package_on_a_card_is_not_found()
    {
        var w = WorldWithLibrary();
        w.External(Scenery("ex"), @"D:\Ext\ex");
        Place(w, "ex");
        Remember(w);

        w.Without("ex");
        var s = Of(Packages(w), "ex");

        Assert.Equal(Presence.NotFound, s.Presence);
        Assert.Equal(PackageSource.Community, s.Source);

        w.State.SetCard(PackageKey.Folder(new PackageName("ex")), null);
        Assert.Empty(Packages(w));
    }

    [Fact]
    public void A_package_found_again_at_a_new_path_is_found_there()
    {
        var w = new World();
        var a = w.Library(SimAddons);
        var b = w.Library(@"C:\Sim\Other");
        var old = w.Add(Scenery("x"), library: a);
        w.State.SetOn(new PackageName("x"), true, old.Path);
        Remember(w);

        w.Without("x");
        var recreated = w.Add(Scenery("x"), library: b);
        var s = Of(Packages(w), "x");

        Assert.Equal(Presence.Found, s.Presence);
        Assert.Equal(recreated.Path, s.Path);
        Assert.Equal([recreated.Path], s.Copies);
    }

    [Fact]
    public void Bgl_placement_reads_the_section_types()
    {
        var w = WorldWithLibrary();
        w.Add(Scenery("nothing") with { BglSectionTypes = [0x2B] });
        w.Add(Scenery("places") with { BglSectionTypes = [0x25] });
        w.Add(Scenery("empty") with { BglSectionTypes = [] });
        w.Add(Scenery("unreadable") with { BglSectionTypes = [0x2B], UnreadableBglCount = 1 });

        var packages = Packages(w);

        Assert.Equal(BglPlacement.PlacesNothing, Of(packages, "nothing").Bgl);
        Assert.Equal(BglPlacement.Places, Of(packages, "places").Bgl);
        Assert.Equal(BglPlacement.None, Of(packages, "empty").Bgl);
        Assert.Equal(BglPlacement.None, Of(packages, "unreadable").Bgl);
    }

    [Fact]
    public void Library_sections_come_from_the_current_state()
    {
        var w = WorldWithLibrary(Category.Scenery);
        w.Add(Scenery("x"));
        var input = w.Input();

        w.State.Libraries[0] = new LibraryRoot(SimAddons, [Category.Aircraft, Category.Scenery]);
        var s = Of(PackageCatalog.Build(w.State, input), "x");

        Assert.Equal([Category.Scenery, Category.Aircraft], s.LibrarySections);
    }

    [Fact]
    public void A_package_on_a_card_without_content_keeps_its_memo_facts()
    {
        var w = WorldWithLibrary();
        w.Add(Scenery("eham-x", ["EHAM"]) with { BglSectionTypes = [0x25] });
        w.Add(Aircraft("ac-x", [("a320", null)]));
        Place(w, "eham-x", "ac-x");
        Remember(w);

        var input = w.Input();
        var packages = PackageCatalog.Build(w.State, input with { Contents = [] });

        var scenery = Of(packages, "eham-x");
        Assert.Equal(Presence.Found, scenery.Presence);
        Assert.False(scenery.HasContent);
        Assert.Equal(Category.Scenery, scenery.Section);
        Assert.Equal(BglPlacement.Places, scenery.Bgl);
        Assert.Equal([Code("EHAM")], scenery.Codes);

        var aircraft = Of(packages, "ac-x");
        Assert.Equal(Category.Aircraft, aircraft.Section);
        Assert.Equal(["a320"], aircraft.Containers);
        Assert.Equal(AircraftRole.Provider, aircraft.Role);
    }

    [Fact]
    public void A_package_with_several_icao_codes_lists_them_sorted()
    {
        var w = WorldWithLibrary();
        w.Add(Scenery("yq05-airstrip", ["YQ05", "YMUZ"]));

        var s = Of(Packages(w), "yq05-airstrip");

        Assert.Equal([Code("YMUZ"), Code("YQ05")], s.Codes);
    }

    [Fact]
    public void Excluded_packages_have_no_codes()
    {
        var w = WorldWithLibrary();
        w.Add(Scenery("navigraph-navdata", Enumerable.Range(0, 20).Select(i => $"AB{i:00}").ToArray()));

        Assert.Empty(Of(Packages(w), "navigraph-navdata").Codes);
    }

    [Fact]
    public void Remember_reports_whether_anything_changed()
    {
        var w = WorldWithLibrary();
        var content = w.Add(Scenery("eham-x", ["EHAM"]));

        Assert.True(Remember(w));
        Assert.False(Remember(w));

        w.Without("eham-x");
        w.Add(content with { ThumbnailPath = @"C:\t.png" });
        Assert.True(Remember(w));
        Assert.Equal(@"C:\t.png", w.State.Packages[new PackageName("eham-x")].Memo!.ThumbnailPath);
    }

    [Fact]
    public void The_memo_keeps_the_packages_own_icao_codes_whatever_the_card_says()
    {
        var w = WorldWithLibrary(Category.Scenery);
        w.Add(Scenery("x", fullRecords: [Rec("EHAM", "Schiphol"), Rec("EHLE", "Lelystad")]));
        var card = w.Group(Category.Scenery, CardTab.Landscapes, "x");
        card.AddCode(Code("EHAM"));
        card.AddCode(Code("EHRD"));
        card.RemoveCode(Code("EHLE"));

        w.Place();

        var memo = Assert.IsType<PackageMemo>(w.State.Packages[new PackageName("x")].Memo);
        Assert.Equal([Code("EHAM"), Code("EHLE")], memo.Codes);
        Assert.Equal([Code("EHAM"), Code("EHLE")], memo.Airports.Select(a => a.Code));
        var s = Of(Packages(w), "x");
        Assert.Equal([Code("EHAM"), Code("EHRD")], s.Codes);
        Assert.Equal([Code("EHAM"), Code("EHLE")], s.OwnCodes);
    }

    private static PackageMemo MemoAt(string path)
        => new(path, PackageSource.Library, "Gone", "SCENERY", null, null, BglPlacement.Places, [Code("EHAM")], [], AircraftRole.None, []);

    [Fact]
    public void A_package_is_unreadable_when_either_its_stored_copy_or_its_memo_path_lies_in_an_unreadable_folder()
    {
        var locked = Path.Combine(SimAddons, "Locked");
        var name = new PackageName("x");
        foreach (var (stored, remembered) in new[] { (Path.Combine(SimAddons, "Gone", "x"), Path.Combine(locked, "x")), (Path.Combine(locked, "x"), Path.Combine(SimAddons, "Gone", "x")) })
        {
            foreach (var onCard in new[] { true, false })
            {
                var w = WorldWithLibrary(Category.Scenery);
                w.State.Packages[name] = new PackageState(true, stored) { Memo = MemoAt(remembered) };
                if (onCard)
                {
                    w.Group(Category.Scenery, CardTab.Landscapes, "x");
                }

                w.Inaccessible(locked);
                var s = Of(Packages(w), "x");

                Assert.Equal(Presence.Unreadable, s.Presence);
                Assert.Equal(Path.Combine(locked, "x"), s.Path);
                Assert.Equal(Category.Scenery, s.Section);
                Assert.Equal([Code("EHAM")], s.Codes);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_reachable_duplicate_does_not_take_over_a_package_whose_stored_copy_is_unreadable(bool offlineLink)
    {
        var w = new World();
        w.Library(SimAddons, Category.Scenery);
        var other = w.Library(@"C:\Sim\Other", Category.Scenery);
        var locked = Path.Combine(SimAddons, "Locked", "x");
        w.Add(Scenery("x", ["EHAM"]), library: other);
        w.State.Packages[new PackageName("x")] = new PackageState(true, locked) { Memo = MemoAt(locked) };
        w.Group(Category.Scenery, CardTab.Landscapes, "x");
        w.Inaccessible(Path.Combine(SimAddons, "Locked"));
        if (offlineLink)
        {
            w.LinkTo("x", locked);
        }

        Assert.Equal(Presence.Unreadable, Of(Packages(w), "x").Presence);
        Assert.Equal(locked, Of(Packages(w), "x").Path);
        var package = Assert.Single(Assert.Single(BoardBuilder.Build(w.State, w.Input()).Cards).Packages);
        Assert.Equal(Presence.Unreadable, package.Presence);
        Assert.False(package.CanSwitch);
        Assert.False(package.Removable);
    }

    [Fact]
    public void An_inaccessible_folder_is_checked_before_an_unreachable_library()
    {
        var w = new World();
        var library = w.Library(SimAddons, Category.Scenery);
        var path = Path.Combine(SimAddons, "Locked", "x");
        w.State.Packages[new PackageName("x")] = new PackageState(true, path) { Memo = MemoAt(path) };
        w.Group(Category.Scenery, CardTab.Landscapes, "x");
        w.Unreachable(library);
        w.Inaccessible(Path.Combine(SimAddons, "Locked"));

        Assert.Equal(Presence.Unreadable, Of(Packages(w), "x").Presence);
    }

    [Fact]
    public void Reanalyze_uses_the_current_cards()
    {
        var w = WorldWithLibrary();
        w.Add(Scenery("grancanaria"));
        var input = w.Input();

        w.Group(Category.Scenery, CardTab.Airports, "grancanaria").AddCode(Code("GCLP"));
        var s = Of(PackageCatalog.Build(w.State, input.Reanalyze(w.State)), "grancanaria");

        Assert.Equal([Code("GCLP")], s.Codes);
    }
}
