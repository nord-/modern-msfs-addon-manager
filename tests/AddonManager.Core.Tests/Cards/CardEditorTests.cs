using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.Tests.Fakes;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class CardEditorTests
{
    private const string LibPath = @"C:\Sim\Addons";

    private static World NewWorld(params Category[] sections)
    {
        var w = new World();
        w.Library(LibPath, sections);
        return w;
    }

    private static PackageKey Key(string n) => PackageKey.Folder(new PackageName(n));

    private static Board Wall(World w) => BoardBuilder.Build(w.State, w.Input());

    private static CardRecord CardOf(World w, string package) => Assert.IsType<CardRecord>(w.State.CardOf(Key(package)));

    private static (CardEditor Editor, InMemoryUserStateStore Store) NewEditor()
    {
        var store = new InMemoryUserStateStore();
        return (new CardEditor(store, TimeProvider.System), store);
    }

    private static World AirportsWorld()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"], title: "Schiphol"));
        w.Add(Scenery("essa-x", ["ESSA"], title: "Arlanda"));
        w.Place();
        return w;
    }

    [Fact]
    public void A_merge_refused_by_a_locked_file_leaves_the_state_unchanged()
    {
        var w = AirportsWorld();
        var board = Wall(w);
        var target = CardOf(w, "eham-x");
        var dragged = CardOf(w, "essa-x");
        var (editor, store) = NewEditor();
        store.RefuseWith = new IOException("locked");
        var before = Shape(w);

        Assert.Throws<IOException>(() => editor.Merge(w.State, board, dragged.Id, target.Id));

        Assert.Equal(before, Shape(w));
        Assert.Equal(0, store.SaveCount);
    }

    private static string Shape(World w) => string.Join(";", w.State.Cards.OrderBy(c => c.Id).Select(c =>
        $"{c.Id}|{c.Name}|{c.ImagePath}|{c.Tab}|{string.Join(",", c.Codes)}|{string.Join(",", w.State.PackagesOn(c.Id))}"));

    [Fact]
    public void A_merge_moves_the_packages_and_pins_the_target_look()
    {
        var w = AirportsWorld();
        var board = Wall(w);
        var target = CardOf(w, "eham-x");
        var dragged = CardOf(w, "essa-x");
        var targetCard = Assert.IsType<Card>(board.Find(target.Id));
        var (editor, store) = NewEditor();

        editor.Merge(w.State, board, dragged.Id, target.Id);

        Assert.Same(target, Assert.Single(w.State.Cards));
        Assert.Null(w.State.FindCard(dragged.Id));
        Assert.Equal([Key("eham-x"), Key("essa-x")], w.State.PackagesOn(target.Id));
        Assert.Equal(targetCard.Name, target.Name);
        Assert.Equal(targetCard.ImagePath, target.ImagePath);
        Assert.Equal(targetCard.Tab, target.Tab);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void A_merge_keeps_a_name_the_user_already_gave_the_target()
    {
        var w = AirportsWorld();
        var board = Wall(w);
        var target = CardOf(w, "eham-x");
        target.Name = "Mine";
        var (editor, _) = NewEditor();

        editor.Merge(w.State, board, CardOf(w, "essa-x").Id, target.Id);

        Assert.Equal("Mine", target.Name);
    }

    [Fact]
    public void A_merge_adds_the_dragged_icao_codes_but_never_one_the_target_removed()
    {
        var w = AirportsWorld();
        var board = Wall(w);
        var target = CardOf(w, "eham-x");
        var dragged = CardOf(w, "essa-x");
        target.RemoveCode(Code("ESSA"));
        dragged.RemoveCode(Code("EHAM"));
        dragged.AddCode(Code("ESSA"));
        dragged.RemoveCode(Code("ESGG"));
        var (editor, _) = NewEditor();

        editor.Merge(w.State, board, dragged.Id, target.Id);

        Assert.Equal([Code("EHAM")], target.Codes);
        Assert.Equal([Code("ESGG"), Code("ESSA")], target.RemovedCodes);
    }

    [Fact]
    public void Merging_a_card_into_itself_or_an_unknown_card_throws_and_changes_nothing()
    {
        var w = AirportsWorld();
        var board = Wall(w);
        var a = CardOf(w, "eham-x");
        var b = CardOf(w, "essa-x");
        var (editor, store) = NewEditor();

        Assert.Throws<ArgumentException>(() => editor.Merge(w.State, board, a.Id, a.Id));
        Assert.Throws<ArgumentException>(() => editor.Merge(w.State, board, a.Id, 99));
        Assert.Throws<ArgumentException>(() => editor.Merge(w.State, board, 99, a.Id));

        Assert.Equal(2, w.State.Cards.Count);
        Assert.Equal([Key("eham-x")], w.State.PackagesOn(a.Id));
        Assert.Equal([Key("essa-x")], w.State.PackagesOn(b.Id));
        Assert.Null(a.Name);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Moving_to_a_section_lands_on_its_initial_tab_and_drops_the_icao_codes()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Place();
        var card = CardOf(w, "eham-x");
        var (editor, store) = NewEditor();

        editor.MoveToSection(w.State, w.Input(), card.Id, Category.Aircraft);

        Assert.Equal((Category.Aircraft, CardTab.Other), (card.Section, card.Tab));
        Assert.Empty(card.Codes);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void Merging_a_card_into_itself_or_across_tabs_is_refused_and_changes_nothing()
    {
        var w = AirportsWorld();
        w.Add(Scenery("hills-x", []));
        var other = w.Group(Category.Scenery, CardTab.Landscapes, "hills-x");
        var aircraft = w.Group(Category.Aircraft, CardTab.Other);
        var board = Wall(w);
        var a = CardOf(w, "eham-x");
        var (editor, store) = NewEditor();

        Assert.Throws<ArgumentException>(() => editor.Merge(w.State, board, a.Id, a.Id));
        var tabs = Assert.Throws<InvalidOperationException>(() => editor.Merge(w.State, board, other.Id, a.Id));
        Assert.Equal("Cards on different tabs are merged by first moving one of them to the other's tab.", tabs.Message);
        Assert.Throws<InvalidOperationException>(() => editor.Merge(w.State, board, aircraft.Id, a.Id));

        Assert.Equal(4, w.State.Cards.Count);
        Assert.Equal([Key("hills-x")], w.State.PackagesOn(other.Id));
        Assert.Equal([Key("eham-x")], w.State.PackagesOn(a.Id));
        Assert.Null(a.Name);
        Assert.Null(a.ImagePath);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void A_merge_adds_the_dragged_icao_codes_except_those_the_target_removed()
    {
        var w = AirportsWorld();
        var board = Wall(w);
        var target = CardOf(w, "eham-x");
        var dragged = CardOf(w, "essa-x");
        target.RemoveCode(Code("ESSA"));
        dragged.AddCode(Code("ESGG"));
        var (editor, _) = NewEditor();

        editor.Merge(w.State, board, dragged.Id, target.Id);

        Assert.Equal([Code("EHAM"), Code("ESGG")], target.Codes);
        Assert.Equal([Code("ESSA")], target.RemovedCodes);
    }

    [Fact]
    public void A_merge_adds_the_dragged_removals_except_icao_codes_the_target_has()
    {
        var w = AirportsWorld();
        var board = Wall(w);
        var target = CardOf(w, "eham-x");
        var dragged = CardOf(w, "essa-x");
        dragged.RemoveCode(Code("EHAM"));
        dragged.RemoveCode(Code("ESGG"));
        var (editor, _) = NewEditor();

        editor.Merge(w.State, board, dragged.Id, target.Id);

        Assert.Equal([Code("EHAM"), Code("ESSA")], target.Codes);
        Assert.Equal([Code("ESGG")], target.RemovedCodes);
    }

    [Fact]
    public void After_merging_ehrd_into_eham_a_later_ehrd_package_joins_the_merged_card()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Add(Scenery("ehrd-x", ["EHRD"]));
        w.Place();
        var board = Wall(w);
        var target = CardOf(w, "eham-x");
        var (editor, _) = NewEditor();
        editor.Merge(w.State, board, CardOf(w, "ehrd-x").Id, target.Id);

        w.Add(Scenery("ehrd-y", ["EHRD"]));
        w.Place();

        Assert.Equal([Code("EHAM"), Code("EHRD")], target.Codes);
        Assert.Equal(target.Id, CardOf(w, "ehrd-y").Id);
        Assert.Single(w.State.Cards);
    }

    [Fact]
    public void A_duplicate_eham_card_merged_away_does_not_come_back()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Add(Scenery("pair-x", ["EHAM", "EHGG"]));
        var target = w.Group(Category.Scenery, CardTab.Airports, "eham-x");
        target.AddCode(Code("EHAM"));
        var dragged = w.Group(Category.Scenery, CardTab.Airports, "pair-x");
        dragged.AddCode(Code("EHAM"));
        dragged.AddCode(Code("EHGG"));
        var (editor, _) = NewEditor();
        editor.Merge(w.State, Wall(w), dragged.Id, target.Id);

        w.Add(Scenery("pair-y", ["EHAM", "EHGG"]));
        w.Place();

        Assert.Equal([Code("EHAM"), Code("EHGG")], target.Codes);
        Assert.Equal(target.Id, CardOf(w, "pair-y").Id);
        Assert.Single(w.State.Cards);
    }

    [Fact]
    public void Moving_a_card_out_of_scenery_drops_its_icao_codes_and_removals()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Place();
        var card = CardOf(w, "eham-x");
        card.RemoveCode(Code("ESSA"));
        var (editor, store) = NewEditor();

        editor.MoveToSection(w.State, w.Input(), card.Id, Category.Aircraft);

        Assert.Equal((Category.Aircraft, CardTab.Other), (card.Section, card.Tab));
        Assert.Empty(card.Codes);
        Assert.Empty(card.RemovedCodes);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void Moving_a_card_into_scenery_gives_it_the_icao_codes_its_packages_share()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-a", ["EHAM"]));
        w.Add(Scenery("eham-b", ["EHAM"]));
        w.Place();
        var card = w.Group(Category.Other, CardTab.Other, "eham-a", "eham-b");
        var (editor, _) = NewEditor();

        editor.MoveToSection(w.State, w.Input(), card.Id, Category.Scenery);

        Assert.Equal((Category.Scenery, CardTab.Airports), (card.Section, card.Tab));
        Assert.Equal([Code("EHAM")], card.Codes);
    }

    [Fact]
    public void Moving_a_card_whose_packages_differ_into_scenery_gives_no_icao_codes()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Add(Scenery("ehrd-x", ["EHRD"]));
        w.Place();
        var card = w.Group(Category.Other, CardTab.Other, "eham-x", "ehrd-x");
        var (editor, _) = NewEditor();

        editor.MoveToSection(w.State, w.Input(), card.Id, Category.Scenery);

        Assert.Equal(Category.Scenery, card.Section);
        Assert.Empty(card.Codes);
        Assert.NotEqual(CardTab.Airports, card.Tab);
    }

    [Fact]
    public void A_card_without_package_summaries_moved_into_scenery_lands_on_landscapes()
    {
        var w = NewWorld();
        var card = w.Group(Category.Other, CardTab.Other, "ghost");
        var (editor, _) = NewEditor();

        editor.MoveToSection(w.State, w.Input(), card.Id, Category.Scenery);

        Assert.Equal((Category.Scenery, CardTab.Landscapes), (card.Section, card.Tab));
        Assert.Empty(card.Codes);
    }

    [Fact]
    public void Moving_a_card_to_its_own_section_changes_nothing_and_saves_nothing()
    {
        var w = AirportsWorld();
        var card = CardOf(w, "eham-x");
        card.Tab = CardTab.Assets;
        var (editor, store) = NewEditor();

        editor.MoveToSection(w.State, w.Input(), card.Id, Category.Scenery);

        Assert.Equal(CardTab.Assets, card.Tab);
        Assert.Equal([Code("EHAM")], card.Codes);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void A_moved_card_keeps_its_packages_sections()
    {
        var w = AirportsWorld();
        var card = CardOf(w, "eham-x");
        var (editor, _) = NewEditor();

        editor.MoveToSection(w.State, w.Input(), card.Id, Category.Aircraft);

        Assert.Equal(Category.Scenery, w.State.Packages[new PackageName("eham-x")].Section);
    }

    [Fact]
    public void Move_out_copies_the_removals_and_takes_no_icao_codes()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-a", ["EHAM"]));
        w.Add(Scenery("eham-b", ["EHAM"]));
        var source = w.Group(Category.Scenery, CardTab.Airports, "eham-a", "eham-b");
        source.AddCode(Code("EHAM"));
        source.RemoveCode(Code("EHRD"));
        var (editor, _) = NewEditor();

        var id = editor.MoveOut(w.State, source.Id, Key("eham-b"));

        var created = Assert.IsType<CardRecord>(w.State.FindCard(id));
        Assert.Empty(created.Codes);
        Assert.Equal([Code("EHRD")], created.RemovedCodes);
        Assert.Equal([Code("EHAM")], source.Codes);
        Assert.Equal([Code("EHRD")], source.RemovedCodes);
    }

    [Fact]
    public void Move_out_is_refused_on_the_only_package()
    {
        var w = AirportsWorld();
        var card = CardOf(w, "eham-x");
        var (editor, store) = NewEditor();

        Assert.Throws<InvalidOperationException>(() => editor.MoveOut(w.State, card.Id, Key("eham-x")));

        Assert.Equal(2, w.State.Cards.Count);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Moving_to_a_tab_of_another_section_is_refused()
    {
        var w = AirportsWorld();
        var card = CardOf(w, "eham-x");
        var (editor, store) = NewEditor();

        Assert.Throws<ArgumentException>(() => editor.MoveToTab(w.State, card.Id, CardTab.Aircraft));

        Assert.Equal(CardTab.Airports, card.Tab);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Moving_to_a_valid_tab_stores_it_and_an_invalid_tab_throws()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Place();
        var card = CardOf(w, "eham-x");
        var (editor, store) = NewEditor();

        editor.MoveToTab(w.State, card.Id, CardTab.Assets);
        Assert.Throws<ArgumentException>(() => editor.MoveToTab(w.State, card.Id, CardTab.Aircraft));
        Assert.Throws<ArgumentException>(() => editor.MoveToTab(w.State, 99, CardTab.Assets));

        Assert.Equal(CardTab.Assets, card.Tab);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void Move_out_gives_the_package_a_new_card_on_the_same_tab()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-a", ["EHAM"]));
        w.Add(Scenery("eham-b", ["EHAM"]));
        var source = w.Group(Category.Scenery, CardTab.Assets, "eham-a", "eham-b");
        var (editor, store) = NewEditor();

        var id = editor.MoveOut(w.State, source.Id, Key("eham-b"));

        var created = Assert.IsType<CardRecord>(w.State.FindCard(id));
        Assert.Equal([Key("eham-b")], w.State.PackagesOn(created.Id));
        Assert.Equal([Key("eham-a")], w.State.PackagesOn(source.Id));
        Assert.Equal((source.Section, source.Tab), (created.Section, created.Tab));
        Assert.Empty(created.Codes);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void The_only_package_of_a_card_or_one_on_another_card_cannot_be_moved_out()
    {
        var w = AirportsWorld();
        var card = CardOf(w, "eham-x");
        var (editor, store) = NewEditor();

        Assert.Throws<InvalidOperationException>(() => editor.MoveOut(w.State, card.Id, Key("eham-x")));
        Assert.Throws<InvalidOperationException>(() => editor.MoveOut(w.State, card.Id, Key("essa-x")));
        Assert.Throws<ArgumentException>(() => editor.MoveOut(w.State, 99, Key("eham-x")));

        Assert.Equal(2, w.State.Cards.Count);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Rename_and_image_set_and_clear_the_users_values()
    {
        var w = AirportsWorld();
        var card = CardOf(w, "eham-x");
        var (editor, store) = NewEditor();

        editor.Rename(w.State, card.Id, "My Schiphol");
        editor.SetImage(w.State, card.Id, @"C:\img\a.png");

        var shown = Assert.IsType<Card>(Wall(w).Find(card.Id));
        Assert.Equal("My Schiphol", shown.Name);
        Assert.Equal(@"C:\img\a.png", shown.ImagePath);

        editor.Rename(w.State, card.Id, "  ");
        editor.SetImage(w.State, card.Id, null);

        Assert.Null(card.Name);
        Assert.Null(card.ImagePath);
        Assert.Equal(4, store.SaveCount);
        Assert.Throws<ArgumentException>(() => editor.Rename(w.State, 99, "x"));
    }

    [Fact]
    public void Showing_a_not_shown_package_in_scenery_gives_it_a_card_like_a_new_one()
    {
        var w = NewWorld(Category.Other);
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Add(Scenery("photo-x"));
        w.Place();
        Assert.Empty(w.State.Cards);
        var input = w.Input();
        Assert.All(BoardBuilder.Build(w.State, input).NotShown, n => Assert.True(n.CanShowIn));
        var (editor, store) = NewEditor();

        var airport = editor.ShowIn(w.State, input, new PackageName("eham-x"), Category.Scenery);
        var landscape = editor.ShowIn(w.State, input, new PackageName("photo-x"), Category.Scenery);

        var eham = CardOf(w, "eham-x");
        Assert.Equal(airport, eham.Id);
        Assert.Equal((Category.Scenery, CardTab.Airports), (eham.Section, eham.Tab));
        Assert.Equal([Code("EHAM")], eham.Codes);
        var photo = CardOf(w, "photo-x");
        Assert.Equal(landscape, photo.Id);
        Assert.Equal((Category.Scenery, CardTab.Landscapes), (photo.Section, photo.Tab));
        Assert.Empty(photo.Codes);
        Assert.Equal(2, store.SaveCount);
        Assert.Empty(BoardBuilder.Build(w.State, w.Input()).NotShown);
    }

    [Fact]
    public void Showing_in_another_section_gives_a_card_in_that_section_without_icao_codes()
    {
        var w = NewWorld(Category.Other);
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Place();
        var (editor, _) = NewEditor();

        editor.ShowIn(w.State, w.Input(), new PackageName("eham-x"), Category.Other);

        var card = CardOf(w, "eham-x");
        Assert.Equal((Category.Other, CardTab.Other), (card.Section, card.Tab));
        Assert.Empty(card.Codes);
    }

    [Fact]
    public void Showing_is_refused_unless_the_package_is_not_shown_with_a_fixed_section()
    {
        var w = NewWorld(Category.Other);
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Add(Scenery("essa-x", ["ESSA"]));
        var (editor, store) = NewEditor();

        // The section is not fixed yet.
        Assert.Throws<InvalidOperationException>(() => editor.ShowIn(w.State, w.Input(), new PackageName("eham-x"), Category.Scenery));

        w.Place();
        editor.ShowIn(w.State, w.Input(), new PackageName("eham-x"), Category.Scenery);

        // It has a card now, and an unknown Package has none to show.
        Assert.Throws<InvalidOperationException>(() => editor.ShowIn(w.State, w.Input(), new PackageName("eham-x"), Category.Scenery));
        Assert.Throws<InvalidOperationException>(() => editor.ShowIn(w.State, w.Input(), new PackageName("nope"), Category.Scenery));
        Assert.Equal(1, store.SaveCount);
        Assert.Single(w.State.Cards);
    }

    private static World TwoSceneryCards()
    {
        var w = NewWorld();
        w.Add(Scenery("aa-x", ["EHAM"]));
        w.Add(Scenery("bb-x", ["ESSA"]));
        w.Add(Scenery("cc-x", ["ESGG"]));
        return w;
    }

    [Fact]
    public void An_ICAO_code_two_cards_hold_is_taken_by_the_oldest_card_whatever_the_list_order()
    {
        var w = TwoSceneryCards();
        var oldest = w.Group(Category.Scenery, CardTab.Airports, "aa-x");
        var newer = w.Group(Category.Scenery, CardTab.Airports, "bb-x");
        var target = w.Group(Category.Scenery, CardTab.Airports, "cc-x");
        oldest.AddCode(Code("EHAM"));
        newer.AddCode(Code("EHAM"));
        w.State.Cards.Reverse();
        var (editor, store) = NewEditor();

        var outcome = editor.AddCode(w.State, Wall(w), target.Id, "EHAM");

        Assert.Equal(CodeResult.Taken, outcome.Result);
        Assert.Equal(oldest.Id, outcome.Holder);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void An_ICAO_code_another_card_only_removed_is_not_taken()
    {
        var w = TwoSceneryCards();
        var other = w.Group(Category.Scenery, CardTab.Airports, "aa-x");
        var target = w.Group(Category.Scenery, CardTab.Airports, "bb-x");
        other.AddCode(Code("EHAM"));
        other.RemoveCode(Code("EHAM"));
        var (editor, _) = NewEditor();

        var outcome = editor.AddCode(w.State, Wall(w), target.Id, "EHAM");

        Assert.Equal(CodeResult.Added, outcome.Result);
        Assert.Equal([Code("EHAM")], target.Codes);
    }

    [Fact]
    public void A_card_outside_Scenery_is_never_the_holder_of_an_ICAO_code()
    {
        var w = TwoSceneryCards();
        var other = w.Group(Category.Other, CardTab.Other, "aa-x");
        other.Codes = [Code("EHAM")];
        var target = w.Group(Category.Scenery, CardTab.Airports, "bb-x");
        var (editor, _) = NewEditor();

        var outcome = editor.AddCode(w.State, Wall(w), target.Id, "EHAM");

        Assert.Equal(CodeResult.Added, outcome.Result);
    }
}
