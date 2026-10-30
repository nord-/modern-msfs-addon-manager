using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class BoardLayoutTests
{
    private static World NewWorld(params Category[] sections)
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons", sections);
        return w;
    }

    /// <summary>Places the Packages that are on no card yet (as the app does after a completed scan), then builds the board.</summary>
    private static Board Wall(World w, bool place = true)
    {
        if (place)
        {
            w.Place();
        }

        return BoardBuilder.Build(w.State, w.Input());
    }

    private static Card CardOf(Board b, string name) => Assert.IsType<Card>(b.CardOf(PackageKey.Folder(new PackageName(name))));

    private static string[] Names(IEnumerable<Card> cards) => [.. cards.Select(c => c.Name)];

    // ---- section, visibility, Not shown ----

    [Fact]
    public void A_stored_card_shows_in_its_stored_section_and_tab_with_its_packages_by_name()
    {
        var w = NewWorld();
        w.Add(Build("zz-misc", "MISC"));
        w.Add(Scenery("aa-scenery", ["EHAM"]));
        w.Group(Category.Scenery, CardTab.Airports, "zz-misc", "aa-scenery");

        var card = Assert.Single(Wall(w, place: false).Cards);

        Assert.Equal((Category.Scenery, CardTab.Airports), (card.Section, card.Tab));
        Assert.Equal(["aa-scenery", "zz-misc"], card.Packages.Select(p => p.Name.Value));
        Assert.All(card.Packages, p => Assert.Equal(PackageKind.Folder, p.Kind));
    }

    [Fact]
    public void A_stored_card_is_shown_even_when_no_library_is_registered_for_its_section()
    {
        var w = NewWorld(Category.Scenery);
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Group(Category.Other, CardTab.Other, "eham-x");

        var board = Wall(w, place: false);

        var card = Assert.Single(board.On(Category.Other, CardTab.Other));
        Assert.Equal(Category.Other, card.Section);
        Assert.Empty(board.NotShown);
    }

    [Fact]
    public void A_package_without_a_card_is_judged_by_the_libraries_that_hold_it()
    {
        var w = NewWorld(Category.Scenery);
        var other = w.Library(@"C:\Sim\Other", Category.Other);
        w.Add(Scenery("aa-scenery", ["EHAM"]));
        w.Add(Scenery("bb-scenery", ["EHAM"]), library: other);

        var board = Wall(w, place: false);

        Assert.Equal("aa-scenery", Assert.Single(board.Cards).Packages.Single().Name.Value);
        Assert.Equal("bb-scenery", Assert.Single(board.NotShown).Name.Value);
    }

    [Fact]
    public void A_package_without_a_card_in_an_unregistered_section_is_not_shown()
    {
        var w = NewWorld(Category.Scenery);
        w.Add(Build("yara-cj-aerial", "MISC"));

        var board = Wall(w, place: false);

        Assert.Empty(board.Cards);
        var row = Assert.Single(board.NotShown);
        Assert.Equal("yara-cj-aerial", row.Name.Value);
        Assert.Equal(Category.Other, row.Section);
        Assert.False(row.CanShowIn);
    }

    [Fact]
    public void A_not_shown_package_can_be_shown_in_once_its_section_is_fixed()
    {
        var w = NewWorld(Category.Scenery);
        w.Add(Scenery("yara-cj-aerial", hasBgl: false));
        var name = new PackageName("yara-cj-aerial");
        w.State.Packages[name] = new PackageState(false) { Section = Category.Other };

        var row = Assert.Single(Wall(w, place: false).NotShown);

        Assert.True(row.CanShowIn);
    }

    [Fact]
    public void A_scenery_package_is_never_shown_under_other_because_of_an_outer_library()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons", Category.Other);
        var inner = w.Library(@"C:\Sim\Addons\Scenery", Category.Scenery);
        w.Add(Scenery("esgg", ["ESGG"]), library: inner);

        var board = Wall(w, place: false);

        var card = Assert.Single(board.Cards);
        Assert.Equal(Category.Scenery, card.Section);
        Assert.Empty(board.NotShown);
    }

    [Fact]
    public void Packages_with_a_fixed_or_external_entry_are_always_shown()
    {
        var w = NewWorld(Category.Aircraft);
        w.Fixed(Scenery("fx-scenery", ["EHAM"]));
        w.External(Scenery("ex-scenery", ["EHRD"]), @"D:\Ext\ex-scenery");

        var board = Wall(w, place: false);

        Assert.Equal(2, board.Cards.Count);
        Assert.Empty(board.NotShown);
    }

    [Fact]
    public void A_not_found_package_keeps_its_card_place_and_name()
    {
        var w = NewWorld(Category.Scenery);
        w.Add(Scenery("eham-x", fullRecords: [Rec("EHAM", "Schiphol")]) with { ThumbnailPath = @"C:\t.png" });
        var before = Assert.Single(Wall(w).Cards);

        w.Without("eham-x");
        var board = BoardBuilder.Build(w.State, w.Input());

        var after = Assert.Single(board.Cards);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal((before.Section, before.Tab), (after.Section, after.Tab));
        Assert.Equal(@"C:\t.png", after.ImagePath);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal("Schiphol", after.AirportName);
        Assert.Equal(Presence.NotFound, Assert.Single(after.Packages).Presence);
        Assert.Empty(board.NotShown);
    }

    [Fact]
    public void A_library_not_found_package_keeps_its_card_place_name_and_image()
    {
        var w = new World();
        var drive = w.Library(@"E:\Addons", Category.Scenery);
        w.Add(Scenery("eham-x", fullRecords: [Rec("EHAM", "Schiphol")]) with { ThumbnailPath = @"C:\t.png" });
        var before = Assert.Single(Wall(w).Cards);

        w.Unreachable(drive);
        var board = BoardBuilder.Build(w.State, w.Input());

        var after = Assert.Single(board.Cards);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal((before.Section, before.Tab, before.ImagePath), (after.Section, after.Tab, after.ImagePath));
        Assert.Equal(Presence.LibraryNotFound, Assert.Single(after.Packages).Presence);
        Assert.Empty(board.NotShown);
    }

    [Fact]
    public void A_library_not_found_stored_copy_keeps_its_card_shown_when_its_duplicate_lies_in_a_library_without_the_section()
    {
        var w = new World();
        var aircraftOnly = w.Library(@"C:\Sim\Aircraft", Category.Aircraft);
        var drive = w.Library(@"E:\Addons", Category.Scenery);
        w.Add(Scenery("eham-x", fullRecords: [Rec("EHAM", "Schiphol")]), library: aircraftOnly);
        var stored = w.Add(Scenery("eham-x", fullRecords: [Rec("EHAM", "Schiphol")]) with { ThumbnailPath = @"E:\t.png" }, library: drive);
        w.State.SetOn(new PackageName("eham-x"), true, stored.Path);
        var before = Assert.Single(Wall(w).Cards);

        w.Unreachable(drive);
        var board = BoardBuilder.Build(w.State, w.Input());

        var after = Assert.Single(board.Cards);
        Assert.Equal((before.Id, before.Section, before.Tab, before.Name, before.ImagePath), (after.Id, after.Section, after.Tab, after.Name, after.ImagePath));
        var package = Assert.Single(after.Packages);
        Assert.Equal(Presence.LibraryNotFound, package.Presence);
        Assert.Equal(stored.Path, package.Path);
        Assert.Equal(@"Library not found — E:\Addons can't be reached", package.DisabledReason);
        Assert.Empty(board.NotShown);
    }

    [Fact]
    public void A_gone_package_of_an_unregistered_library_stays_on_its_card_as_not_found()
    {
        var w = NewWorld(Category.Scenery);
        w.Add(Scenery("eham-x", ["EHAM"]));
        Wall(w);

        w.Without("eham-x");
        w.State.Libraries.Clear();
        var board = BoardBuilder.Build(w.State, w.Input());

        // Spec 6.1: a Package on a card that no registered Library holds stays there; its folder is gone, so it is Not found (and removable).
        var package = Assert.Single(Assert.Single(board.Cards).Packages);
        Assert.Equal(Presence.NotFound, package.Presence);
        Assert.True(package.Removable);
        Assert.Empty(board.NotShown);
    }

    // ---- tabs ----

    [Fact]
    public void Scenery_with_icao_codes_starts_on_airports_also_with_several()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Add(Scenery("asf-demo", ["FEFA", "FEGK"]));

        var board = Wall(w);

        Assert.All(board.Cards, c => Assert.Equal(CardTab.Airports, c.Tab));
        Assert.Equal(2, board.Cards.Count);
    }

    [Fact]
    public void A_scenery_card_that_places_nothing_starts_on_assets()
    {
        var w = NewWorld();
        w.Add(Scenery("lib-assets") with { BglSectionTypes = [0x2B, 0x30] });

        Assert.Equal(CardTab.Assets, Assert.Single(Wall(w).Cards).Tab);
    }

    [Fact]
    public void A_scenery_card_with_no_or_unreadable_bgl_starts_on_landscapes()
    {
        var w = NewWorld();
        w.Add(Scenery("no-sections") with { BglSectionTypes = [] });
        w.Add(Scenery("unreadable") with { BglSectionTypes = [0x2B], UnreadableBglCount = 1 });
        w.Add(Scenery("places") with { BglSectionTypes = [0x25] });

        var board = Wall(w);

        Assert.Equal(3, board.Cards.Count);
        Assert.All(board.Cards, c => Assert.Equal((Category.Scenery, CardTab.Landscapes), (c.Section, c.Tab)));
    }

    [Fact]
    public void Instruments_aircraft_and_other_tabs()
    {
        var w = NewWorld();
        w.Add(Build("instr", "INSTRUMENTS"));
        w.Add(Aircraft("ac-prov", [("Cessna", null)]));
        w.Add(Aircraft("ac-nothing"));
        w.Add(Aircraft("livery-only", [("L_Red", "Elsewhere")]));

        var board = Wall(w);

        Assert.Equal(CardTab.Instruments, CardOf(board, "instr").Tab);
        Assert.Equal(CardTab.Aircraft, CardOf(board, "ac-prov").Tab);
        Assert.Equal(CardTab.Other, CardOf(board, "ac-nothing").Tab);
        Assert.Equal(CardTab.Other, CardOf(board, "livery-only").Tab);
        Assert.All(board.Cards, c => Assert.Equal(Category.Aircraft, c.Section));
    }

    [Fact]
    public void The_other_section_has_the_other_tab()
    {
        var w = NewWorld();
        w.Add(Build("misc-x", "MISC"));

        var card = Assert.Single(Wall(w).Cards);

        Assert.Equal((Category.Other, CardTab.Other), (card.Section, card.Tab));
    }

    // ---- name, codes, image, tags, order ----

    [Fact]
    public void A_card_is_named_after_its_first_package_unless_the_user_named_it()
    {
        var w = NewWorld();
        w.Add(Scenery("b-pkg", title: "Bravo"));
        w.Add(Scenery("a-pkg", title: "Alpha"));
        var card = w.Group(Category.Scenery, CardTab.Landscapes, "b-pkg", "a-pkg");

        Assert.Equal("Alpha", Assert.Single(Wall(w, place: false).Cards).Name);

        card.Name = "Mine";
        Assert.Equal("Mine", Assert.Single(BoardBuilder.Build(w.State, w.Input()).Cards).Name);
    }

    [Fact]
    public void A_card_with_one_icao_code_shows_the_airport_name()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", fullRecords: [Rec("EHAM", "Schiphol")]));

        var card = Assert.Single(Wall(w).Cards);

        Assert.Equal([Code("EHAM")], card.Codes);
        Assert.Equal("Schiphol", card.AirportName);
        Assert.False(card.Several);
    }

    [Fact]
    public void A_card_with_an_unknown_icao_code_has_no_airport_name()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));

        var card = Assert.Single(Wall(w).Cards);

        Assert.Equal("eham-x", card.Name);
        Assert.Equal([Code("EHAM")], card.Codes);
        Assert.Null(card.AirportName);
    }

    [Fact]
    public void A_card_with_several_icao_codes_is_several_and_names_no_airport()
    {
        var w = NewWorld();
        w.Add(Scenery("asf-demo", ["FEGK", "FEFA"]));

        var card = Assert.Single(Wall(w).Cards);

        Assert.True(card.Several);
        Assert.Null(card.AirportName);
        Assert.Equal([Code("FEFA"), Code("FEGK")], card.Codes);
        Assert.Empty(card.RemovedCodes);
    }

    [Fact]
    public void The_image_is_the_first_thumbnail_by_package_name_unless_the_user_chose_one()
    {
        var w = NewWorld();
        w.Add(Scenery("c-pkg") with { ThumbnailPath = @"C:\c.png" });
        w.Add(Scenery("b-pkg") with { ThumbnailPath = @"C:\b.png" });
        w.Add(Scenery("a-pkg"));
        var card = w.Group(Category.Scenery, CardTab.Landscapes, "c-pkg", "b-pkg", "a-pkg");

        Assert.Equal(@"C:\b.png", Assert.Single(Wall(w, place: false).Cards).ImagePath);

        card.ImagePath = @"D:\mine.png";
        Assert.Equal(@"D:\mine.png", Assert.Single(BoardBuilder.Build(w.State, w.Input()).Cards).ImagePath);
    }

    [Fact]
    public void Tags_are_the_union_of_package_tags()
    {
        var w = NewWorld();
        w.Add(Scenery("a1"), @"Europe\a1");
        w.Add(Scenery("a2"), @"Europe\a2");
        w.Add(Scenery("b1"), @"Asia\b1");
        w.Add(Scenery("b2"), @"Asia\b2");
        w.Group(Category.Scenery, CardTab.Landscapes, "a1", "b1");

        var card = CardOf(Wall(w, place: false), "a1");

        Assert.Equal(["Asia", "Europe"], card.Tags);
        Assert.Equal(["Europe"], card.Packages.Single(p => p.Name.Value == "a1").Tags);
    }

    [Fact]
    public void A_folder_keeps_its_tag_while_one_of_its_two_packages_is_not_found()
    {
        var w = NewWorld();
        w.Add(Scenery("p1"), @"Europe\p1");
        w.Add(Scenery("p2"), @"Europe\p2");
        Wall(w);

        w.Without("p2");
        var board = BoardBuilder.Build(w.State, w.Input());

        Assert.Equal(["Europe"], CardOf(board, "p1").Tags);
        Assert.Equal(["Europe"], CardOf(board, "p2").Tags);
    }

    [Fact]
    public void Airports_by_first_icao_code_and_landscapes_by_name_in_any_input_order()
    {
        var w = NewWorld();
        w.Add(Scenery("ehrd-x", ["EHRD"]));
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Add(Scenery("zulu-demo", ["FEFA", "FEGL"]));
        w.Add(Scenery("alpha-demo", ["FEFA", "FEGK"]));
        w.Add(Scenery("b-land") with { BglSectionTypes = [] });
        w.Add(Scenery("a-land") with { BglSectionTypes = [] });

        var board = Wall(w);
        var again = BoardBuilder.Build(w.State, w.Input());
        w.State.Cards.Reverse();
        var reversed = BoardBuilder.Build(w.State, w.Input());

        Assert.Equal(["eham-x", "ehrd-x", "alpha-demo", "zulu-demo"], Names(board.On(Category.Scenery, CardTab.Airports)));
        Assert.Equal(["a-land", "b-land"], Names(board.On(Category.Scenery, CardTab.Landscapes)));
        Assert.Equal(Names(again.Cards), Names(reversed.Cards));
    }

    [Fact]
    public void CardsAt_finds_cards_by_their_own_or_their_packages_icao_codes_by_name_then_id()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Add(Scenery("asf-demo", ["EHAM", "EHRD"]));
        w.Add(Scenery("eham-twin", ["EHAM", "EHLE"]));

        var board = Wall(w);

        Assert.Equal(["asf-demo", "eham-twin", "eham-x"], Names(board.CardsAt(Code("EHAM"))));
        Assert.Equal(["asf-demo"], Names(board.CardsAt(Code("EHRD"))));
        Assert.Empty(board.CardsAt(Code("ESSA")));
    }

    [Fact]
    public void CardsAt_also_finds_a_card_by_the_icao_codes_of_its_packages()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        var card = w.Group(Category.Scenery, CardTab.Landscapes, "eham-x");

        var board = Wall(w, place: false);

        Assert.Empty(card.Codes);
        Assert.Equal(card.Id, Assert.Single(board.CardsAt(Code("EHAM"))).Id);
    }

    [Fact]
    public void Find_and_CardOf_look_up_stored_cards_and_their_packages()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));

        var board = Wall(w);

        var card = CardOf(board, "eham-x");
        Assert.Equal(card, board.Find(card.Id!.Value));
        Assert.Null(board.Find(99));
        Assert.Null(board.CardOf(new PackageKey(PackageKind.GsxProfile, new PackageName("eham-x"))));
    }

    // ---- what a card shows (name, image, tags, order, marker) ----

    private static PackageContent Titled(PackageContent content, string title)
        => content with { Manifest = new Manifest(title, null, content.Manifest.ContentType, null, []) };

    private static PackageContent Designated(PackageContent content, string container, string designator)
        => content with { AircraftConfigs = [new AircraftConfig(container, null, designator)] };

    private static string NameOfAircraftCard(World w, params string[] names)
    {
        w.Group(Category.Aircraft, CardTab.Aircraft, names);
        return Assert.Single(Wall(w, place: false).Cards).Name;
    }

    [Fact]
    public void A_one_icao_code_card_is_named_after_its_airport()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", fullRecords: [Rec("EHAM", "Schiphol")]));

        Assert.Equal("EHAM Schiphol", Assert.Single(Wall(w).Cards).Name);
    }

    [Fact]
    public void A_one_icao_code_card_whose_airport_is_unknown_takes_its_first_package_name()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"], title: "Schiphol X"));

        Assert.Equal("Schiphol X", Assert.Single(Wall(w).Cards).Name);
    }

    [Fact]
    public void A_several_card_takes_its_first_package_name()
    {
        var w = NewWorld();
        w.Add(Scenery("yq05", fullRecords: [Rec("YQ05", "Mount Mulligan"), Rec("YQ06", "Other Strip")], title: "YQ05 Mount Mulligan"));

        var card = Assert.Single(Wall(w).Cards);

        Assert.True(card.Several);
        Assert.Equal("YQ05 Mount Mulligan", card.Name);
    }

    [Fact]
    public void An_aircraft_card_takes_its_provider_then_base_then_built_in_base_then_container()
    {
        var provider = NewWorld();
        provider.Add(Titled(Aircraft("zz-prov", [("Cessna", null)]), "Zulu Cessna"));
        provider.Add(Titled(Aircraft("aa-liv", [("Cessna_Red", "Cessna")]), "Alpha Livery"));
        Assert.Equal("Zulu Cessna", NameOfAircraftCard(provider, "zz-prov", "aa-liv"));

        var lockedBase = NewWorld();
        lockedBase.Fixed(Titled(Aircraft("fx-base", [("Cessna", null)], source: PackageSource.Community), "Fixed Base"));
        lockedBase.Add(Titled(Aircraft("aa-liv", [("Cessna_Red", "Cessna")]), "Alpha Livery"));
        Assert.Equal("Fixed Base", NameOfAircraftCard(lockedBase, "fx-base", "aa-liv"));

        var builtIn = NewWorld();
        builtIn.SimManaged(Titled(Aircraft("Asobo_C152", [("C152", null)], source: PackageSource.SimManaged), "Asobo C152"));
        builtIn.Add(Titled(Aircraft("c152-livery", [("C152_Blue", "C152")]), "Blue Livery"));
        Assert.Equal("Asobo C152", NameOfAircraftCard(builtIn, "c152-livery"));

        var container = NewWorld();
        container.Add(Titled(Aircraft("liv-x", [("L_Red", "Elsewhere")]), "Red Livery"));
        Assert.Equal("Elsewhere", NameOfAircraftCard(container, "liv-x"));
    }

    [Fact]
    public void The_users_name_and_image_always_win()
    {
        var w = NewWorld();
        w.Add(Titled(Aircraft("prov", [("Cessna", null)]), "Cessna") with { ThumbnailPath = @"C:\p.png" });
        var aircraft = w.Group(Category.Aircraft, CardTab.Aircraft, "prov");
        aircraft.Name = "My Cessna";
        aircraft.ImagePath = @"D:\mine.png";
        w.Add(Scenery("eham-x", fullRecords: [Rec("EHAM", "Schiphol")]));
        var scenery = w.Group(Category.Scenery, CardTab.Airports, "eham-x");
        scenery.AddCode(Code("EHAM"));
        scenery.Name = "Home";

        var board = Wall(w, place: false);

        Assert.Equal(("My Cessna", @"D:\mine.png"), (CardOf(board, "prov").Name, CardOf(board, "prov").ImagePath));
        Assert.Equal("Home", CardOf(board, "eham-x").Name);
    }

    [Fact]
    public void A_not_found_packages_remembered_airport_still_names_its_card()
    {
        var w = NewWorld(Category.Scenery);
        w.Add(Scenery("eham-x", fullRecords: [Rec("EHAM", "Schiphol")]));
        Assert.Equal("EHAM Schiphol", Assert.Single(Wall(w).Cards).Name);

        w.Without("eham-x");
        var after = Assert.Single(BoardBuilder.Build(w.State, w.Input()).Cards);

        Assert.Equal(Presence.NotFound, Assert.Single(after.Packages).Presence);
        Assert.Equal("EHAM Schiphol", after.Name);
    }

    [Fact]
    public void An_aircraft_card_shows_its_type_designators_as_tags()
    {
        var w = NewWorld();
        w.Add(Designated(Aircraft("c152", [("C152", null)]), "C152", "C152"), @"Cessna\c152");
        w.Add(Designated(Aircraft("c172", [("C172", null)]), "C172", "C172"), @"Cessna\c172");
        w.Group(Category.Aircraft, CardTab.Aircraft, "c152", "c172");
        w.Add(Scenery("eham-x", ["EHAM"]), @"Europe\eham-x");
        w.Add(Scenery("ehrd-x", ["EHRD"]), @"Europe\ehrd-x");

        var board = Wall(w);
        var card = CardOf(board, "c152");

        Assert.Equal(["C152", "C172", "Cessna"], card.Tags);
        Assert.Equal(["C152", "C172"], card.TypeDesignators);
        Assert.Equal(["Europe"], CardOf(board, "eham-x").Tags);
        Assert.Empty(CardOf(board, "eham-x").TypeDesignators);

        // A Not found Package keeps its designators and tags from its memo.
        w.Without("c172");
        var after = CardOf(BoardBuilder.Build(w.State, w.Input()), "c152");
        Assert.Equal(["C152", "C172", "Cessna"], after.Tags);
    }

    [Fact]
    public void Airports_cards_are_ordered_by_first_icao_code_then_by_name()
    {
        var w = NewWorld();
        w.Add(Scenery("m-several", ["EHAM", "EHRD"]));
        w.Add(Scenery("single-ehrd", ["EHRD"]));
        w.Add(Scenery("single-ehbk", ["EHBK"]));
        w.Add(Scenery("eham-x", fullRecords: [Rec("EHAM", "Schiphol")]));
        w.Add(Scenery("aaa-nocode"));
        w.Group(Category.Scenery, CardTab.Airports, "aaa-nocode");

        var board = Wall(w);
        w.State.Cards.Reverse();
        var reversed = BoardBuilder.Build(w.State, w.Input());

        var expected = new[] { "EHAM Schiphol", "m-several", "single-ehbk", "single-ehrd", "aaa-nocode" };
        Assert.Equal(expected, Names(board.On(Category.Scenery, CardTab.Airports)));
        Assert.Equal(expected, Names(reversed.On(Category.Scenery, CardTab.Airports)));
    }

    [Fact]
    public void Equal_names_are_ordered_by_card_id_and_provisional_cards_come_last()
    {
        var w = NewWorld();
        w.Add(Scenery("zz-first", title: "Same"));
        w.Add(Scenery("aa-second", title: "Same"));
        w.Group(Category.Scenery, CardTab.Landscapes, "zz-first");
        w.Group(Category.Scenery, CardTab.Landscapes, "aa-second");
        w.Add(Scenery("aa-provisional", title: "Same"));
        w.Add(Scenery("a-provisional", title: "Same"));

        var board = Wall(w, place: false);
        w.State.Cards.Reverse();
        var reversed = BoardBuilder.Build(w.State, w.Input());

        var expected = new[] { "zz-first", "aa-second", "a-provisional", "aa-provisional" };
        Assert.Equal(expected, board.Cards.Select(c => c.Packages.Single().Name.Value));
        Assert.Equal(expected, reversed.Cards.Select(c => c.Packages.Single().Name.Value));
    }

    [Fact]
    public void The_marker_opens_the_oldest_card_with_only_that_icao_code()
    {
        var w = NewWorld();
        w.Add(Scenery("zz-old", ["EHAM"]));
        w.Add(Scenery("aa-new", ["EHAM"]));
        w.Add(Scenery("aa-several", ["EHAM", "EHRD"]));
        var oldest = w.Group(Category.Scenery, CardTab.Airports, "zz-old");
        oldest.AddCode(Code("EHAM"));
        w.Group(Category.Scenery, CardTab.Airports, "aa-several").AddCode(Code("EHAM"));
        var newer = w.Group(Category.Scenery, CardTab.Airports, "aa-new");
        newer.AddCode(Code("EHAM"));

        var board = Wall(w, place: false);

        Assert.Equal(oldest.Id, board.MarkerCard(Code("EHAM"))!.Id);
        Assert.Null(board.MarkerCard(Code("ESSA")));
    }

    [Fact]
    public void Without_such_a_card_the_marker_opens_the_first_card_at_the_icao_code_by_name()
    {
        var w = NewWorld();
        w.Add(Scenery("bb-demo", ["EHAM", "EHRD"]));
        w.Add(Scenery("aa-demo", ["EHAM", "EHLE"]));
        var board = Wall(w);

        Assert.Equal("aa-demo", board.MarkerCard(Code("EHAM"))!.Name);

        // Another card holding an EHAM Package, and no Scenery card for it, is what the marker opens.
        var other = NewWorld();
        other.Add(Scenery("eham-x", ["EHAM"]));
        var otherCard = other.Group(Category.Other, CardTab.Other, "eham-x");

        Assert.Equal(otherCard.Id, Wall(other, place: false).MarkerCard(Code("EHAM"))!.Id);
    }

    // ---- provisional ----

    [Fact]
    public void A_package_without_a_card_gets_a_provisional_card()
    {
        var w = NewWorld();
        w.Add(Aircraft("ac-x", [("A", null)]));
        w.Add(Scenery("sc-x", ["EHAM"]));
        w.Add(Build("misc-x", "MISC"));

        var board = Wall(w, place: false);

        Assert.Empty(w.State.Cards);
        var aircraft = CardOf(board, "ac-x");
        Assert.Equal((Category.Aircraft, CardTab.Other), (aircraft.Section, aircraft.Tab));
        Assert.Equal(PackageKey.Folder(new PackageName("ac-x")), aircraft.ProvisionalPackage);
        Assert.Null(aircraft.Id);
        Assert.Equal((Category.Scenery, CardTab.Landscapes), (CardOf(board, "sc-x").Section, CardOf(board, "sc-x").Tab));
        Assert.Equal((Category.Other, CardTab.Other), (CardOf(board, "misc-x").Section, CardOf(board, "misc-x").Tab));
    }

    [Fact]
    public void Provisional_cards_follow_the_stored_cards_of_their_tab_by_package_name()
    {
        var w = NewWorld(Category.Scenery);
        w.Add(Scenery("zz-stored", hasBgl: false));
        w.Add(Scenery("bb-new", hasBgl: false));
        w.Add(Scenery("aa-new", hasBgl: false));
        var stored = w.Group(Category.Scenery, CardTab.Landscapes, "zz-stored");

        var cards = Wall(w, place: false).On(Category.Scenery, CardTab.Landscapes).ToList();

        Assert.Equal(stored.Id, cards[0].Id);
        Assert.Equal(["aa-new", "bb-new"], cards.Skip(1).Select(c => c.ProvisionalPackage!.Value.Name.Value));
    }

    [Fact]
    public void A_provisional_card_sits_in_its_manifest_section()
    {
        // The manifest says SCENERY, but the BGL files are missing: the content pass would say Other. Until a read fixes the section the card sits where the manifest puts it.
        var w = NewWorld(Category.Scenery);
        w.Add(Scenery("sc-x", hasBgl: false));

        var board = Wall(w, place: false);

        var card = Assert.Single(board.Cards);
        Assert.Equal((Category.Scenery, CardTab.Landscapes), (card.Section, card.Tab));
        Assert.Empty(board.NotShown);
        Assert.Equal(1, board.Banner.Provisional);
    }

    [Fact]
    public void An_existing_card_is_shown_after_its_library_loses_the_section()
    {
        var w = NewWorld(Category.Scenery);
        w.Add(Scenery("sc-x", ["EHAM"]));
        w.Place();
        w.State.Libraries[0] = w.State.Libraries[0] with { Sections = [Category.Other] };

        var board = Wall(w, place: false);

        Assert.Equal("sc-x", Assert.Single(board.Cards).Packages.Single().Name.Value);
        Assert.Empty(board.NotShown);
    }

    [Fact]
    public void A_cache_only_board_creates_no_card_and_shows_provisional_cards()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        var full = w.Input(scanComplete: false);

        var board = BoardBuilder.Build(w.State, BoardInput.WithoutContent(full.Index, full.Community));

        Assert.Empty(w.State.Cards);
        Assert.Equal(PackageKey.Folder(new PackageName("eham-x")), Assert.Single(board.Cards).ProvisionalPackage);
    }

    [Fact]
    public void A_contentless_fixed_community_folder_builds_a_board_under_without_content()
    {
        var w = NewWorld();
        w.Fixed(Scenery("fx-bare", ["EHAM"]));
        var full = w.Input(scanComplete: false);

        var board = BoardBuilder.Build(w.State, BoardInput.WithoutContent(full.Index, full.Community).Reanalyze(w.State));

        Assert.Equal(PackageKey.Folder(new PackageName("fx-bare")), Assert.Single(board.Cards).ProvisionalPackage);
    }

    [Fact]
    public void A_contentless_fixed_community_folder_after_a_failed_read_builds_a_board()
    {
        var w = NewWorld();
        w.Fixed(Scenery("fx-failed", ["EHAM"]));
        var read = w.Input();
        var failed = read with { Contents = [], Failed = new HashSet<string>([PathNormalizer.Normalize(@"C:\Sim\Community\fx-failed")], StringComparer.OrdinalIgnoreCase) };

        var board = BoardBuilder.Build(w.State, failed.Reanalyze(w.State));

        Assert.Equal(PackageKey.Folder(new PackageName("fx-failed")), Assert.Single(board.Cards).ProvisionalPackage);
    }

    [Fact]
    public void A_card_lists_only_the_active_copy_and_names_the_other_copies()
    {
        var w = NewWorld();
        var second = w.Library(@"C:\Sim\Backup");
        w.Add(Scenery("dup-x", ["EHAM"]));
        w.Add(Scenery("dup-x", ["EHAM"]), library: second);

        var card = Assert.Single(Wall(w).Cards);

        var package = Assert.Single(card.Packages);
        Assert.Equal(@"C:\Sim\Addons\dup-x", package.Path, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, package.Copies.Count);
        Assert.Contains(package.Copies, c => c.Equals(@"C:\Sim\Backup\dup-x", StringComparison.OrdinalIgnoreCase));
    }
}
