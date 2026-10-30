using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class CardStatusTests
{
    private static World NewWorld()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons");
        return w;
    }

    /// <summary>Places the Packages that are on no card yet, then builds the board.</summary>
    private static Board Wall(World w, bool place = true, bool scanComplete = true)
    {
        if (place)
        {
            w.Place();
        }

        return BoardBuilder.Build(w.State, w.Input(scanComplete));
    }

    private static CardRecord Manual(World w, params string[] names) => w.Group(Category.Scenery, CardTab.Landscapes, names);

    private static PackageName Name(string name) => new(name);

    private static Card CardOf(Board b, string name) => Assert.IsType<Card>(b.CardOf(PackageKey.Folder(Name(name))));

    private static CardPackage PackageOf(Board b, string name) => CardOf(b, name).Packages.Single(p => p.Name == Name(name));

    private static PackageContent Titled(PackageContent content, string title) => content with { Manifest = new Manifest(title, null, content.Manifest.ContentType, null, []) };

    // ---- state ----

    [Theory]
    [InlineData(true, true, CardState.On)]
    [InlineData(false, false, CardState.Off)]
    [InlineData(true, false, CardState.Mixed)]
    public void State_follows_the_stored_choices(bool a, bool b, CardState expected)
    {
        var w = NewWorld();
        w.Add(Scenery("pkg-a", ["EHAM"]));
        w.Add(Scenery("pkg-b", ["EHAM"]));
        Manual(w, "pkg-a", "pkg-b");
        w.State.SetOn(Name("pkg-a"), a);
        w.State.SetOn(Name("pkg-b"), b);

        var card = Assert.Single(Wall(w).Cards);

        Assert.Equal(expected, card.State);
        Assert.Equal([a, b], card.Packages.Select(p => p.On));
        Assert.All(card.Packages, p => Assert.True(p.CanSwitch));
    }

    [Fact]
    public void Locked_packages_do_not_count_and_an_all_locked_card_is_locked()
    {
        var w = NewWorld();
        w.Fixed(Scenery("fx", ["EHAM"]));
        w.Add(Scenery("lib", ["EHAM"]));
        Manual(w, "fx", "lib");
        w.State.SetOn(Name("lib"), true);
        Assert.Equal(CardState.On, Assert.Single(Wall(w).Cards).State);

        var locked = NewWorld();
        locked.Fixed(Scenery("fx", ["EHAM"]));
        Manual(locked, "fx");

        var card = Assert.Single(Wall(locked).Cards);

        Assert.Equal(CardState.Locked, card.State);
        Assert.True(card.Packages.Single().On);
        Assert.False(card.Packages.Single().CanSwitch);
    }

    [Fact]
    public void Not_found_and_library_not_found_packages_count_with_their_stored_choice_but_cannot_be_switched()
    {
        var w = NewWorld();
        var library = w.State.Libraries[0];
        w.Add(Scenery("pkg-m", ["EHAM"]));
        w.Add(Scenery("pkg-o", ["EHAM"]), library: library);
        Manual(w, "pkg-m");
        Manual(w, "pkg-o");
        w.State.SetOn(Name("pkg-m"), true);
        w.State.SetOn(Name("pkg-o"), true);
        Wall(w);

        w.Without("pkg-m");
        var notFound = PackageOf(BoardBuilder.Build(w.State, w.Input()), "pkg-m");
        Assert.True(notFound.On);
        Assert.False(notFound.CanSwitch);
        Assert.Equal(@"Not found — C:\Sim\Addons\pkg-m is gone", notFound.DisabledReason);

        w.Unreachable(library);
        var board = BoardBuilder.Build(w.State, w.Input());
        var libraryNotFound = PackageOf(board, "pkg-o");
        Assert.Equal(CardState.On, CardOf(board, "pkg-o").State);
        Assert.False(libraryNotFound.CanSwitch);
        Assert.Equal(@"Library not found — C:\Sim\Addons can't be reached", libraryNotFound.DisabledReason);
    }

    [Fact]
    public void A_name_taken_package_counts_as_off_and_names_its_holder()
    {
        var w = NewWorld();
        w.Add(Scenery("x", ["EHAM"]));
        w.Ignored("x");
        Manual(w, "x");
        w.State.SetOn(Name("x"), true);

        var board = Wall(w);

        var package = PackageOf(board, "x");
        Assert.False(package.On);
        Assert.False(package.CanSwitch);
        Assert.Equal("Name taken by Ignored entry 'x' in Community", package.DisabledReason);
        Assert.Equal(CardState.Off, CardOf(board, "x").State);
    }

    [Fact]
    public void A_locked_package_names_its_entry()
    {
        var w = NewWorld();
        w.Fixed(Scenery("fx", ["EHAM"]));
        w.External(Scenery("ex", ["EHRD"]), @"D:\Ext\ex");

        var board = Wall(w);

        Assert.Equal("Locked — Fixed entry in Community", PackageOf(board, "fx").DisabledReason);
        Assert.Equal(@"Locked — External link to D:\Ext\ex", PackageOf(board, "ex").DisabledReason);
    }

    [Fact]
    public void A_card_with_a_pending_package_is_pending()
    {
        var w = NewWorld();
        w.Add(Scenery("pkg-a", ["EHAM"]));
        w.Add(Scenery("pkg-b", ["EHRD"]));
        w.State.Pending.Add(Name("pkg-a"));

        var board = Wall(w);

        Assert.True(CardOf(board, "pkg-a").Pending);
        Assert.False(CardOf(board, "pkg-b").Pending);
    }

    // ---- built-in parts and Replaces built-in ----

    [Fact]
    public void A_sim_managed_airport_attaches_to_the_card_anchored_to_its_code()
    {
        var w = NewWorld();
        w.SimManaged(Titled(Scenery("asobo-airport-eham", fullRecords: [Rec("EHAM", "Schiphol")]), "Schiphol (built-in)"));
        w.Add(Scenery("eham-x", ["EHAM"]));

        var card = Assert.Single(Wall(w).Cards);

        var part = Assert.Single(card.BuiltIns);
        Assert.Equal(Name("asobo-airport-eham"), part.Name);
        Assert.Equal("Schiphol (built-in)", part.DisplayName);
        Assert.Equal(Code("EHAM"), part.Code);
        Assert.Null(part.Container);
    }

    [Fact]
    public void A_built_in_airport_attaches_to_every_scenery_card_with_its_icao_code()
    {
        var w = NewWorld();
        w.SimManaged(Scenery("asobo-airport-fegk", ["FEGK"]));
        w.Add(Scenery("bb-demo", ["FEGK", "FEXX"]));
        w.Add(Scenery("aa-demo", ["FEGK", "FEFA"]));
        w.Add(Scenery("cc-other", ["FEGK"]));
        w.Group(Category.Other, CardTab.Other, "cc-other");

        var board = Wall(w);

        Assert.Equal(3, board.Cards.Count);
        Assert.Equal(Name("asobo-airport-fegk"), Assert.Single(CardOf(board, "aa-demo").BuiltIns).Name);
        Assert.Equal(Name("asobo-airport-fegk"), Assert.Single(CardOf(board, "bb-demo").BuiltIns).Name);
        Assert.Empty(CardOf(board, "cc-other").BuiltIns);
    }

    [Fact]
    public void A_card_of_only_an_attached_package_shows_and_is_named_after_the_built_in_aircraft()
    {
        var w = NewWorld();
        w.SimManaged(Titled(Aircraft("Asobo_C152", [("C152", null)], source: PackageSource.SimManaged), "Asobo C152"));
        w.Add(Aircraft("simflight-C152-interior-navtablet", airplaneFolders: ["C152"]));

        var card = Assert.Single(Wall(w).Cards);

        Assert.Equal(AircraftRole.Attached, card.Packages.Single().Role);
        Assert.Equal("Asobo C152", card.Name);
        Assert.Equal([Name("Asobo_C152")], card.BuiltIns.Select(b => b.Name));
    }

    [Fact]
    public void A_built_in_aircraft_part_attaches_to_every_aircraft_card_whose_packages_provide_or_repaint_its_container()
    {
        var w = NewWorld();
        w.SimManaged(Aircraft("Asobo_C152", [("C152", null)], source: PackageSource.SimManaged));
        w.Add(Aircraft("c152-livery", [("C152_Blue", "C152")]));
        w.Add(Aircraft("c152-mod", [("C152", null)]), "mod");
        w.Add(Aircraft("c152-other", [("C152_Red", "C152")]));
        w.Group(Category.Aircraft, CardTab.Aircraft, "c152-livery");
        w.Group(Category.Aircraft, CardTab.Aircraft, "c152-mod");
        w.Group(Category.Other, CardTab.Other, "c152-other");

        var board = Wall(w);

        Assert.Equal([Name("Asobo_C152")], CardOf(board, "c152-livery").BuiltIns.Select(b => b.Name));
        Assert.Equal([Name("Asobo_C152")], CardOf(board, "c152-mod").BuiltIns.Select(b => b.Name));
        Assert.Empty(CardOf(board, "c152-other").BuiltIns);
    }

    [Fact]
    public void A_built_in_airport_without_a_user_card_gives_no_card()
    {
        var w = NewWorld();
        w.SimManaged(Scenery("asobo-airport-eham", ["EHAM"]));

        var board = Wall(w);

        Assert.Empty(board.Cards);
    }

    [Fact]
    public void A_sim_managed_base_and_its_liveries_attach_to_the_aircraft_card()
    {
        var w = NewWorld();
        w.SimManaged(Titled(Aircraft("Asobo_C152", [("C152", null)], source: PackageSource.SimManaged), "Asobo C152"));
        w.SimManaged(Aircraft("Asobo_C152_Liv", [("C152_Red", "C152")], source: PackageSource.SimManaged));
        w.Add(Aircraft("c152-livery", [("C152_Blue", "C152")]));

        var card = Assert.Single(Wall(w).Cards);

        Assert.Equal(
            [(Name("Asobo_C152"), AircraftRole.LockedBase, "C152"), (Name("Asobo_C152_Liv"), AircraftRole.Livery, "C152")],
            card.BuiltIns.Select(b => (b.Name, b.Role, b.Container)));
        Assert.All(card.BuiltIns, b => Assert.Null(b.Code));
    }

    [Fact]
    public void Sim_managed_liveries_alone_never_make_a_card()
    {
        var w = NewWorld();
        w.SimManaged(Aircraft("Asobo_C152", [("C152", null)], source: PackageSource.SimManaged));
        w.SimManaged(Aircraft("Asobo_C152_Liv", [("C152_Red", "C152")], source: PackageSource.SimManaged));

        var board = Wall(w);

        Assert.Empty(board.Cards);
    }

    [Fact]
    public void A_package_replacing_built_in_lists_the_code()
    {
        var w = NewWorld();
        w.SimManaged(Scenery("asobo-airport-eham", ["EHAM"]));
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Add(Scenery("ehrd-x", ["EHRD"]));

        var board = Wall(w);

        Assert.Equal([Code("EHAM")], PackageOf(board, "eham-x").ReplacesBuiltIn);
        Assert.Empty(PackageOf(board, "ehrd-x").ReplacesBuiltIn);
    }

    // ---- warnings and banner ----

    [Fact]
    public void Duplicate_package_when_a_community_name_is_also_sim_managed()
    {
        var w = NewWorld();
        w.SimManaged(Scenery("dup"));
        w.Fixed(Scenery("dup"));
        w.Fixed(Scenery("other"));

        var board = Wall(w);

        Assert.Equal([new CardWarning(WarningKind.DuplicatePackage, Name("dup"))], CardOf(board, "dup").Warnings);
        Assert.Empty(CardOf(board, "other").Warnings);
    }

    [Fact]
    public void Duplicate_copy_when_a_name_has_two_library_copies()
    {
        var w = NewWorld();
        w.Add(Scenery("dc", ["EHAM"]), "f1");
        w.Add(Scenery("dc", ["EHAM"]), "f2");

        var card = Assert.Single(Wall(w).Cards);

        Assert.Equal([new CardWarning(WarningKind.DuplicateCopy, Name("dc"))], card.Warnings);
    }

    private static Card LiveryCard(Action<World> arrange, bool providerOn, bool liveryOn)
    {
        var w = NewWorld();
        arrange(w);
        w.State.SetOn(Name("prov"), providerOn);
        w.State.SetOn(Name("liv"), liveryOn);
        return CardOf(Wall(w), "liv");
    }

    private static void LibraryProvider(World w)
    {
        w.Add(Aircraft("prov", [("Cessna", null)]));
        w.Add(Aircraft("liv", [("Cessna_Red", "Cessna")]));
    }

    [Fact]
    public void Livery_without_aircraft_when_on_and_no_provider_is_on_or_always_loaded()
    {
        var warning = new CardWarning(WarningKind.LiveryWithoutAircraft, Name("liv"));

        Assert.Equal([warning], LiveryCard(LibraryProvider, providerOn: false, liveryOn: true).Warnings);
        Assert.Empty(LiveryCard(LibraryProvider, providerOn: true, liveryOn: true).Warnings);
        Assert.Empty(LiveryCard(LibraryProvider, providerOn: false, liveryOn: false).Warnings);

        Assert.Empty(LiveryCard(
            w =>
            {
                w.SimManaged(Aircraft("Asobo_C", [("Cessna", null)], source: PackageSource.SimManaged));
                w.Add(Aircraft("liv", [("Cessna_Red", "Cessna")]));
            },
            providerOn: false,
            liveryOn: true).Warnings);

        Assert.Empty(LiveryCard(
            w =>
            {
                w.Fixed(Aircraft("prov", [("Cessna", null)], source: PackageSource.Community));
                w.Add(Aircraft("liv", [("Cessna_Red", "Cessna")]));
            },
            providerOn: false,
            liveryOn: true).Warnings);
    }

    [Fact]
    public void The_banner_lists_broken_and_stray_entries_and_counts_pending()
    {
        var w = NewWorld();
        foreach (var name in new[] { "gone-b", "gone-a" })
        {
            w.Add(Scenery(name, ["EHAM"]));
            w.Linked(name);
            w.Without(name);
        }

        w.Stray("zz.txt");
        w.Stray("aa.txt");
        w.State.Pending.Add(Name("p1"));
        w.State.Pending.Add(Name("p2"));

        var banner = Wall(w).Banner;

        Assert.Equal(["gone-a", "gone-b"], banner.Broken.Select(e => e.Entry.Name));
        Assert.All(banner.Broken, e => Assert.Equal(EntryClass.Broken, e.Class));
        Assert.Equal(["aa.txt", "zz.txt"], banner.Stray.Select(e => e.Entry.Name));
        Assert.Equal(2, banner.Pending);
    }

    [Fact]
    public void Grouping_counts_every_provisional_card_and_a_completed_scan_names_unreadable_packages()
    {
        var w = NewWorld();
        w.Add(Scenery("pkg-a", ["EHAM"]));
        w.Add(Scenery("pkg-b", ["EHRD"]));

        var grouping = Wall(w, place: false, scanComplete: false).Banner;
        Assert.Equal(2, grouping.Provisional);
        Assert.Empty(grouping.Unreadable);

        w.WithoutContent("pkg-b");
        var done = Wall(w, place: false, scanComplete: true).Banner;
        Assert.Equal(2, done.Provisional);
        Assert.Equal([Name("pkg-b")], done.Unreadable);
    }

    // ---- Not found, Library not found, Unreadable (spec 6.1, 6.3, 6.7, 10) ----

    [Fact]
    public void A_package_whose_library_answers_but_whose_folder_is_gone_is_not_found_on_its_card()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"], title: "Schiphol"));
        Manual(w, "eham-x").Name = "My Schiphol";
        w.State.SetOn(Name("eham-x"), true);
        Wall(w);

        w.Without("eham-x");
        var board = BoardBuilder.Build(w.State, w.Input());

        var card = Assert.Single(board.Cards);
        Assert.Equal("My Schiphol", card.Name);
        var package = Assert.Single(card.Packages);
        Assert.Equal(Presence.NotFound, package.Presence);
        Assert.Equal("Schiphol", package.DisplayName);
        Assert.True(package.On);
        Assert.False(package.CanSwitch);
        Assert.True(package.Removable);
        Assert.Equal([new CardWarning(WarningKind.NotFound, Name("eham-x"))], card.Warnings);
        Assert.NotNull(w.State.Packages[Name("eham-x")].Memo);
    }

    [Fact]
    public void A_renamed_update_joins_the_card_and_the_old_package_is_not_found_there()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons", Category.Scenery);
        w.Add(Scenery("orbx-eham-1-3", ["EHAM"], title: "OrbX EHAM 1.3"));
        w.Place();
        var record = Assert.Single(w.State.Cards);
        record.Name = "My Schiphol";
        record.AddCode(Code("EHRD"));
        record.RemoveCode(Code("EHLE"));

        w.Without("orbx-eham-1-3");
        w.Add(Scenery("orbx-eham-1-5", ["EHAM"], title: "OrbX EHAM 1.5"));
        w.Place();
        var board = BoardBuilder.Build(w.State, w.Input());

        var card = Assert.Single(board.Cards);
        Assert.Equal(record.Id, card.Id);
        Assert.Equal("My Schiphol", card.Name);
        Assert.Equal([Code("EHAM"), Code("EHRD")], card.Codes);
        Assert.Equal([Code("EHLE")], card.RemovedCodes);
        Assert.Equal(["orbx-eham-1-3", "orbx-eham-1-5"], card.Packages.Select(p => p.Name.Value));
        Assert.Equal([Presence.NotFound, Presence.Found], card.Packages.Select(p => p.Presence));
        Assert.Equal([Code("EHAM"), Code("EHRD")], card.Packages[0].Codes);
    }

    [Fact]
    public void A_package_in_a_library_that_does_not_answer_keeps_its_card_and_cannot_be_switched()
    {
        var w = NewWorld();
        var library = w.State.Libraries[0];
        w.Add(Scenery("eham-x", ["EHAM"]));
        Manual(w, "eham-x").Name = "My Schiphol";
        w.State.SetOn(Name("eham-x"), true);
        Wall(w);

        w.Unreachable(library);
        var board = BoardBuilder.Build(w.State, w.Input());

        var card = Assert.Single(board.Cards);
        Assert.Equal("My Schiphol", card.Name);
        var package = Assert.Single(card.Packages);
        Assert.Equal(Presence.LibraryNotFound, package.Presence);
        Assert.True(package.On);
        Assert.False(package.CanSwitch);
        Assert.False(package.Removable);
        Assert.Empty(card.Warnings);
    }

    [Fact]
    public void A_fixed_package_whose_folder_is_gone_is_not_found()
    {
        var w = NewWorld();
        w.Fixed(Scenery("fx", ["EHAM"]));
        Manual(w, "fx");
        Wall(w);

        w.Without("fx");
        var package = PackageOf(BoardBuilder.Build(w.State, w.Input()), "fx");

        Assert.Equal(Presence.NotFound, package.Presence);
        Assert.Equal(@"Not found — C:\Sim\Community\fx is gone", package.DisabledReason);
        Assert.True(package.Removable);
    }

    [Fact]
    public void A_gone_fixed_package_sharing_its_name_with_a_sim_managed_package_stays_not_found_on_its_card()
    {
        var w = NewWorld();
        w.Fixed(Scenery("fx", ["EHAM"]));
        Manual(w, "fx");
        Wall(w);

        w.Without("fx");
        w.SimManaged(Scenery("fx", ["EHAM"]));
        var package = PackageOf(BoardBuilder.Build(w.State, w.Input()), "fx");

        Assert.Equal(Presence.NotFound, package.Presence);
        Assert.True(package.Removable);
    }

    [Fact]
    public void An_external_package_whose_link_was_removed_stays_off_and_locked_without_a_warning()
    {
        var w = NewWorld();
        w.External(Scenery("ex", ["EHAM"]), @"D:\Ext\ex");
        Manual(w, "ex");
        Wall(w);

        w.RemoveEntry("ex");
        var board = BoardBuilder.Build(w.State, w.Input());

        var package = PackageOf(board, "ex");
        Assert.Equal(Presence.Found, package.Presence);
        Assert.Equal(LockReason.NotInRegisteredLibrary, package.Lock);
        Assert.False(package.On);
        Assert.False(package.CanSwitch);
        Assert.True(package.Removable);
        Assert.Equal("Not in a registered Library", package.DisabledReason);
        Assert.Empty(CardOf(board, "ex").Warnings);
        Assert.Empty(board.Banner.Unreadable);
    }

    [Fact]
    public void An_external_package_whose_target_is_gone_is_not_found()
    {
        var w = NewWorld();
        w.External(Scenery("ex", ["EHAM"]), @"D:\Ext\ex");
        Manual(w, "ex");
        Wall(w);

        w.Without("ex");
        var board = BoardBuilder.Build(w.State, w.Input());

        var package = PackageOf(board, "ex");
        Assert.Equal(Presence.NotFound, package.Presence);
        Assert.Equal(@"Not found — D:\Ext\ex is gone", package.DisabledReason);
        Assert.Equal([new CardWarning(WarningKind.NotFound, Name("ex"))], CardOf(board, "ex").Warnings);
    }

    [Fact]
    public void A_package_is_in_no_registered_library_unless_a_run_has_looked_for_it()
    {
        var w = NewWorld();
        w.External(Scenery("ex", ["EHAM"]), @"D:\Ext\ex");
        Manual(w, "ex");
        Wall(w);
        w.Without("ex");

        // Before any run the startup board cannot know whether the folder is gone: it stays off and locked, not Not found.
        w.OutsideKnown = false;
        var package = PackageOf(BoardBuilder.Build(w.State, w.Input()), "ex");

        Assert.Equal(Presence.Found, package.Presence);
        Assert.Equal(LockReason.NotInRegisteredLibrary, package.Lock);
    }

    [Fact]
    public void A_library_copy_becomes_active_and_switchable_when_the_holding_entry_disappears()
    {
        var w = NewWorld();
        var copy = w.Add(Scenery("x", ["EHAM"]));
        w.External(Scenery("x", ["EHAM"]), @"D:\Ext\x");
        Manual(w, "x");
        Wall(w);
        Assert.Equal(LockReason.HeldByEntry, PackageOf(BoardBuilder.Build(w.State, w.Input()), "x").Lock);

        w.RemoveEntry("x");
        var package = PackageOf(BoardBuilder.Build(w.State, w.Input()), "x");

        Assert.Equal(Presence.Found, package.Presence);
        Assert.Equal(LockReason.None, package.Lock);
        Assert.Equal(copy.Path, package.Path);
        Assert.True(package.CanSwitch);
        Assert.False(package.On);
    }

    [Fact]
    public void A_package_no_registered_library_holds_is_locked_off_and_removable()
    {
        var w = NewWorld();
        var elsewhere = w.Library(@"D:\Elsewhere", Category.Scenery);
        var copy = w.Add(Scenery("x", ["EHAM"]), library: elsewhere);
        Manual(w, "x");
        w.State.SetOn(Name("x"), true);
        Wall(w);

        w.State.Libraries.Remove(elsewhere);
        w.Present(copy.Path);
        var board = BoardBuilder.Build(w.State, w.Input());

        var package = PackageOf(board, "x");
        Assert.Equal(Presence.Found, package.Presence);
        Assert.Equal(LockReason.NotInRegisteredLibrary, package.Lock);
        Assert.False(package.On);
        Assert.False(package.CanSwitch);
        Assert.True(package.Removable);
        Assert.Equal("Not in a registered Library", package.DisabledReason);
    }

    [Fact]
    public void A_linked_package_of_a_folder_removed_from_every_section_says_not_in_a_registered_library()
    {
        var w = NewWorld();
        var copy = w.Add(Scenery("x", ["EHAM"]));
        w.Linked("x");
        Manual(w, "x");
        w.State.SetOn(Name("x"), true, copy.Path);
        Wall(w);

        w.State.Libraries.Clear();
        var board = BoardBuilder.Build(w.State, w.Input());

        var package = PackageOf(board, "x");
        Assert.Equal(LockReason.HeldByEntry, package.Lock);
        Assert.Equal(EntryClass.External, package.Holder!.Class);
        Assert.Equal("Not in a registered Library", package.DisabledReason);
        Assert.False(package.CanSwitch);
        Assert.False(package.Removable);
        Assert.Equal(CardState.Locked, CardOf(board, "x").State);

        // The next completed scan settles the Package again (its memo now says Community); the message survives it.
        w.Place();
        Assert.Equal("Not in a registered Library", PackageOf(BoardBuilder.Build(w.State, w.Input()), "x").DisabledReason);
    }

    [Fact]
    public void Packages_that_are_not_found_count_with_their_stored_choice()
    {
        CardState StateWith(bool goneOn, bool otherOn)
        {
            var w = NewWorld();
            w.Add(Scenery("a-gone", ["EHAM"]));
            w.Add(Scenery("b-here", ["EHAM"]));
            Manual(w, "a-gone", "b-here");
            w.State.SetOn(Name("a-gone"), goneOn);
            w.State.SetOn(Name("b-here"), otherOn);
            Wall(w);
            w.Without("a-gone");
            return CardOf(BoardBuilder.Build(w.State, w.Input()), "a-gone").State;
        }

        Assert.Equal(CardState.On, StateWith(goneOn: true, otherOn: true));
        Assert.Equal(CardState.Mixed, StateWith(goneOn: true, otherOn: false));
        Assert.Equal(CardState.Mixed, StateWith(goneOn: false, otherOn: true));
        Assert.Equal(CardState.Off, StateWith(goneOn: false, otherOn: false));
    }

    [Fact]
    public void External_and_unregistered_packages_do_not_count_toward_the_card_state()
    {
        var w = NewWorld();
        var elsewhere = w.Library(@"D:\Elsewhere", Category.Scenery);
        w.Add(Scenery("lib", ["EHAM"]));
        var unregistered = w.Add(Scenery("unreg", ["EHAM"]), library: elsewhere);
        w.External(Scenery("ex", ["EHAM"]), @"D:\Ext\ex");
        Manual(w, "lib", "unreg", "ex");
        // Only "lib" counts and it is off; the stored On of "unreg" and the always-loaded External must not turn the card Mixed.
        w.State.SetOn(Name("lib"), false);
        w.State.SetOn(Name("unreg"), true);
        Wall(w);

        w.State.Libraries.Remove(elsewhere);
        w.Present(unregistered.Path);
        var board = BoardBuilder.Build(w.State, w.Input());

        Assert.Equal(LockReason.NotInRegisteredLibrary, PackageOf(board, "unreg").Lock);
        Assert.Equal(CardState.Off, CardOf(board, "lib").State);
    }

    [Fact]
    public void A_not_found_external_package_does_not_count_toward_the_card_state()
    {
        var w = NewWorld();
        w.Add(Scenery("lib-a", ["EHAM"]));
        w.Add(Scenery("lib-b", ["EHAM"]));
        w.External(Scenery("ex", ["EHAM"]), @"D:\Ext\ex");
        Manual(w, "lib-a", "lib-b", "ex");
        w.State.SetOn(Name("lib-a"), true);
        w.State.SetOn(Name("lib-b"), true);
        Wall(w);

        w.Without("ex");
        var board = BoardBuilder.Build(w.State, w.Input());

        Assert.Equal(Presence.NotFound, PackageOf(board, "ex").Presence);
        Assert.Equal(CardState.On, CardOf(board, "lib-a").State);
    }

    [Fact]
    public void The_banner_lists_libraries_not_found()
    {
        var w = NewWorld();
        var drive = w.Library(@"E:\Addons", Category.Scenery);
        var other = w.Library(@"D:\Other", Category.Scenery);
        w.Add(Scenery("x", ["EHAM"]), library: drive);
        w.Add(Scenery("y", ["EHRD"]), library: other);
        Wall(w);

        w.Unreachable(other);
        w.Unreachable(drive);
        var banner = BoardBuilder.Build(w.State, w.Input()).Banner;

        Assert.Equal([other, drive], banner.LibrariesNotFound);
    }

    [Fact]
    public void A_package_under_a_folder_discovery_could_not_read_is_unreadable_never_not_found()
    {
        var w = NewWorld();
        w.Add(Scenery("x", ["EHAM"]), folder: @"Locked\x");
        Manual(w, "x");
        w.State.SetOn(Name("x"), true);
        Wall(w);

        w.Without("x");
        w.Inaccessible(@"C:\Sim\Addons\Locked");
        var board = BoardBuilder.Build(w.State, w.Input());

        var card = Assert.Single(board.Cards);
        var package = Assert.Single(card.Packages);
        Assert.Equal(Presence.Unreadable, package.Presence);
        Assert.Equal(@"Unreadable — C:\Sim\Addons\Locked can't be read", package.DisabledReason);
        Assert.True(package.On);
        Assert.False(package.CanSwitch);
        Assert.False(package.Removable);
        Assert.Equal(CardState.On, card.State);
        Assert.Empty(card.Warnings);
        Assert.Equal([Name("x")], board.Banner.Unreadable);
    }

    [Fact]
    public void A_cardless_package_under_an_unreadable_folder_is_a_provisional_card_not_not_shown()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons", Category.Scenery);
        var path = @"C:\Sim\Addons\Locked\x";
        w.State.Packages[Name("x")] = new PackageState(true, path)
        {
            Memo = new PackageMemo(path, PackageSource.Library, "X", "SCENERY", null, null, BglPlacement.Places, [Code("EHAM")], [], AircraftRole.None, []),
        };
        w.Inaccessible(@"C:\Sim\Addons\Locked");

        var board = BoardBuilder.Build(w.State, w.Input());

        Assert.Empty(board.NotShown);
        var card = Assert.Single(board.Cards);
        Assert.Equal(PackageKey.Folder(Name("x")), card.ProvisionalPackage);
        Assert.Equal(Category.Scenery, card.Section);
        Assert.Equal(Presence.Unreadable, Assert.Single(card.Packages).Presence);
    }

    [Fact]
    public void A_not_found_package_loses_an_icao_code_the_card_removed_later()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM", "EHRD"]));
        var record = Manual(w, "eham-x");
        Wall(w);

        w.Without("eham-x");
        Assert.Equal(["EHAM", "EHRD"], BoardBuilder.Build(w.State, w.Input()).Cards.Single().Packages.Single().Codes.Select(c => c.Value));
        record.RemoveCode(Code("EHRD"));
        var board = BoardBuilder.Build(w.State, w.Input());

        Assert.Equal([Code("EHAM")], board.Cards.Single().Packages.Single().Codes);
        Assert.Single(board.CardsAt(Code("EHAM")));
        Assert.Empty(board.CardsAt(Code("EHRD")));
    }
}
