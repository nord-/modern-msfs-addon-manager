using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class CardPlacerTests
{
    private static World NewWorld()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons");
        return w;
    }

    private static PackageKey Key(string n) => PackageKey.Folder(new PackageName(n));

    /// <summary>Builds the summaries, takes as candidates the Found Packages with content that are not Sim-managed and on no card, and places them.</summary>
    private static bool Place(World w)
    {
        var input = w.Input();
        var summaries = PackageCatalog.Build(w.State, input);
        var candidates = summaries.Values
            .Where(s => s.HasContent && s.Presence == Presence.Found && s.Source != PackageSource.SimManaged && w.State.CardOf(Key(s.Name.Value)) is null)
            .Select(s => s.Name)
            .ToHashSet();
        return CardPlacer.Place(w.State, summaries, candidates, input.Analysis, World.Now);
    }

    [Fact]
    public void Every_new_package_gets_its_own_card_in_its_section()
    {
        var w = NewWorld();
        w.Add(Scenery("a-scenery"));
        w.Add(Aircraft("b-plane", [("B", null)]));
        w.Add(Build("c-tool", "MISC"));

        Assert.True(Place(w));

        Assert.Equal([Category.Scenery, Category.Aircraft, Category.Other], w.State.Cards.Select(c => c.Section).Order());
        Assert.All(w.State.Cards, c => Assert.Single(w.State.PackagesOn(c.Id)));
    }

    [Fact]
    public void A_scenery_package_with_icao_codes_gets_them_on_its_card_on_airports()
    {
        var w = NewWorld();
        w.Add(Scenery("x", ["EHAM"]));

        Place(w);

        var card = Assert.Single(w.State.Cards);
        Assert.Equal([Code("EHAM")], card.Codes);
        Assert.Equal(CardTab.Airports, card.Tab);
        Assert.Empty(card.RemovedCodes);
    }

    [Fact]
    public void Only_scenery_cards_get_icao_codes()
    {
        var w = NewWorld();
        w.Add(Aircraft("ac", [("A", null)]));

        Place(w);

        Assert.Empty(Assert.Single(w.State.Cards).Codes);
    }

    [Fact]
    public void Card_ids_increase_and_record_the_creation_time()
    {
        var w = NewWorld();
        w.Add(Scenery("b-pkg"));
        w.Add(Scenery("a-pkg"));

        Place(w);

        Assert.Equal([1L, 2L], w.State.Cards.Select(c => c.Id));
        Assert.Equal(1, w.State.CardOf(Key("a-pkg"))!.Id);
        Assert.Equal(2, w.State.CardOf(Key("b-pkg"))!.Id);
        Assert.All(w.State.Cards, c => Assert.Equal(World.Now, c.Created));
        Assert.Equal(3, w.State.NextCardId);
    }

    [Fact]
    public void A_package_on_a_card_is_never_placed_again()
    {
        var w = NewWorld();
        w.Add(Scenery("a"));
        w.Add(Scenery("b"));
        Assert.True(Place(w));

        Assert.False(Place(w));

        Assert.Equal(2, w.State.Cards.Count);
    }

    [Fact]
    public void Sim_managed_and_contentless_packages_get_no_card()
    {
        var w = NewWorld();
        w.SimManaged(Scenery("asobo-airport-eham", ["EHAM"]));
        w.Add(Scenery("no-content"));
        w.WithoutContent("no-content");

        Assert.False(Place(w));

        Assert.Empty(w.State.Cards);
    }

    [Fact]
    public void Only_the_offered_candidates_are_placed()
    {
        var w = NewWorld();
        w.Add(Scenery("a"));
        w.Add(Scenery("b"));
        var input = w.Input();
        var summaries = PackageCatalog.Build(w.State, input);

        var changed = CardPlacer.Place(w.State, summaries, new HashSet<PackageName> { new("b"), new("unknown") }, input.Analysis, World.Now);

        Assert.True(changed);
        Assert.Null(w.State.CardOf(Key("a")));
        Assert.NotNull(w.State.CardOf(Key("b")));
        Assert.Single(w.State.Cards);
    }

    // ---- Scenery cards by ICAO-code set ----

    /// <summary>An existing Scenery card on Airports with the given ICAO codes (and removed ones), holding a placeholder Package named <paramref name="holder"/>.</summary>
    private static CardRecord OldCard(World w, string holder, string[] codes, string[]? removed = null)
    {
        w.Add(Scenery(holder));
        var card = w.State.NewCard(Category.Scenery, CardTab.Airports, codes.Select(Code), World.Now);
        foreach (var code in removed ?? [])
        {
            card.RemoveCode(Code(code));
        }

        w.State.SetCard(Key(holder), card.Id);
        return card;
    }

    private static string[] CodesOf(CardRecord card) => [.. card.Codes.Select(c => c.Value)];

    [Fact]
    public void Packages_with_the_same_icao_code_share_one_card()
    {
        var w = NewWorld();
        w.Add(Scenery("orbx-eham-1", ["EHAM"]));
        w.Add(Scenery("orbx-eham-2", ["EHAM"]));
        w.Add(Scenery("orbx-eham-3", ["EHAM"]));

        Place(w);

        var card = Assert.Single(w.State.Cards);
        Assert.Equal(["EHAM"], CodesOf(card));
        Assert.Equal(3, w.State.PackagesOn(card.Id).Count);
    }

    [Fact]
    public void A_package_with_several_icao_codes_gets_a_several_card_with_all_of_them()
    {
        var w = NewWorld();
        w.Add(Scenery("sltcreations-airport-yq05-mount-mulligan", ["YQ05", "YMUZ"]));

        Place(w);

        var card = Assert.Single(w.State.Cards);
        Assert.Equal(["YMUZ", "YQ05"], CodesOf(card));
        Assert.Equal(CardTab.Airports, card.Tab);
    }

    [Fact]
    public void A_one_code_and_a_several_package_in_one_scan_get_two_cards()
    {
        var w = NewWorld();
        w.Add(Scenery("a-yq05", ["YQ05"]));
        w.Add(Scenery("b-yq05-ymuz", ["YQ05", "YMUZ"]));

        Place(w);

        Assert.Equal(2, w.State.Cards.Count);
        Assert.NotEqual(w.State.CardOf(Key("a-yq05"))!.Id, w.State.CardOf(Key("b-yq05-ymuz"))!.Id);
    }

    [Fact]
    public void A_later_package_joins_the_card_with_exactly_its_icao_codes()
    {
        var w = NewWorld();
        w.Add(Scenery("a-yq05", ["YQ05"]));
        w.Add(Scenery("b-yq05-ymuz", ["YQ05", "YMUZ"]));
        Place(w);
        w.Add(Scenery("c-update", ["YQ05", "YMUZ"]));

        Place(w);

        Assert.Equal(2, w.State.Cards.Count);
        Assert.Equal(w.State.CardOf(Key("b-yq05-ymuz"))!.Id, w.State.CardOf(Key("c-update"))!.Id);
    }

    [Fact]
    public void A_later_package_joins_the_oldest_card_whose_icao_codes_include_all_of_its()
    {
        var w = NewWorld();
        var one = OldCard(w, "h1", ["EHAM", "EHRD"]);
        OldCard(w, "h2", ["EHAM", "EHLE"]);
        w.Add(Scenery("orbx-eham-1-5", ["EHAM"]));

        Place(w);

        Assert.Equal(one.Id, w.State.CardOf(Key("orbx-eham-1-5"))!.Id);
        Assert.Equal(2, w.State.Cards.Count);
    }

    [Fact]
    public void An_update_with_two_icao_codes_joins_a_card_with_more()
    {
        var w = NewWorld();
        var card = OldCard(w, "h1", ["EHAM", "EHLE", "EHRD"]);
        w.Add(Scenery("update", ["EHAM", "EHRD"]));

        Place(w);

        Assert.Equal(card.Id, w.State.CardOf(Key("update"))!.Id);
        Assert.Single(w.State.Cards);
    }

    [Fact]
    public void A_lone_yq05_package_joins_the_several_card_when_no_yq05_card_exists()
    {
        var w = NewWorld();
        var several = OldCard(w, "h1", ["YMUZ", "YQ05"]);
        w.Add(Scenery("lone", ["YQ05"]));

        Place(w);

        Assert.Equal(several.Id, w.State.CardOf(Key("lone"))!.Id);
        Assert.Single(w.State.Cards);
    }

    [Fact]
    public void An_icao_code_no_card_has_gives_a_new_card()
    {
        var w = NewWorld();
        var old = OldCard(w, "h1", ["EHAM"]);
        w.Add(Scenery("pair", ["EHAM", "EHGG"]));

        Place(w);

        var card = w.State.CardOf(Key("pair"))!;
        Assert.NotEqual(old.Id, card.Id);
        Assert.Equal(["EHAM", "EHGG"], CodesOf(card));
    }

    [Fact]
    public void A_removed_icao_code_still_matches()
    {
        var w = NewWorld();
        var card = OldCard(w, "h1", ["EHAM"], ["EHRD"]);
        w.Add(Scenery("rd", ["EHRD"]));

        Place(w);

        Assert.Equal(card.Id, w.State.CardOf(Key("rd"))!.Id);
        Assert.Single(w.State.Cards);
    }

    [Fact]
    public void A_card_that_has_the_icao_codes_wins_over_an_older_card_covering_them_through_removals()
    {
        var w = NewWorld();
        OldCard(w, "h1", ["EHAM"], ["EHRD", "EHLE"]);
        var newer = OldCard(w, "h2", ["EHAM", "EHRD", "EHLE"]);
        w.Add(Scenery("pair", ["EHAM", "EHRD"]));

        Place(w);

        Assert.Equal(newer.Id, w.State.CardOf(Key("pair"))!.Id);
    }

    [Fact]
    public void An_exact_match_on_codes_wins_over_an_older_exact_match_through_removals()
    {
        var w = NewWorld();
        OldCard(w, "h1", [], ["EHAM"]);
        var newer = OldCard(w, "h2", ["EHAM"]);
        w.Add(Scenery("eham", ["EHAM"]));

        Place(w);

        Assert.Equal(newer.Id, w.State.CardOf(Key("eham"))!.Id);
    }

    [Fact]
    public void When_several_cards_match_the_oldest_wins()
    {
        var w = NewWorld();
        var first = OldCard(w, "h1", ["EHAM"]);
        OldCard(w, "h2", ["EHAM"]);
        w.Add(Scenery("eham", ["EHAM"]));
        w.State.Cards.Reverse();

        Place(w);

        Assert.Equal(first.Id, w.State.CardOf(Key("eham"))!.Id);
    }

    [Fact]
    public void A_scenery_package_without_icao_codes_matches_no_card()
    {
        var w = NewWorld();
        OldCard(w, "h1", []);
        w.Add(Scenery("a-none"));
        w.Add(Scenery("b-none"));

        Place(w);

        Assert.Equal(3, w.State.Cards.Count);
        Assert.NotEqual(w.State.CardOf(Key("a-none"))!.Id, w.State.CardOf(Key("b-none"))!.Id);
        Assert.Equal(1, w.State.CardOf(Key("h1"))!.Id);
    }

    [Fact]
    public void Packages_new_in_this_scan_never_join_a_card_made_in_the_same_scan()
    {
        var w = NewWorld();
        w.Add(Scenery("a-pair", ["EHAM", "EHRD"]));
        w.Add(Scenery("b-lone", ["EHAM"]));

        Place(w);

        Assert.Equal(2, w.State.Cards.Count);
        Assert.NotEqual(w.State.CardOf(Key("a-pair"))!.Id, w.State.CardOf(Key("b-lone"))!.Id);
    }

    // ---- Aircraft cards by Container ----

    private static CardRecord AircraftCard(World w, params string[] names) => w.Group(Category.Aircraft, CardTab.Aircraft, names);

    [Fact]
    public void Packages_that_provide_one_container_share_one_card()
    {
        var w = NewWorld();
        w.Add(Aircraft("sal1800-170b", [("Cessna-170B-Backcountry", null)]));
        w.Add(Aircraft("super-170b", [("Cessna-170B-Backcountry", null)]));

        Place(w);

        var card = Assert.Single(w.State.Cards);
        Assert.Equal(2, w.State.PackagesOn(card.Id).Count);
        Assert.Equal(CardTab.Aircraft, card.Tab);
    }

    [Fact]
    public void Liveries_join_the_card_of_the_aircraft_they_repaint()
    {
        var w = NewWorld();
        w.Add(Aircraft("plane", [("P", null)]));
        w.Add(Aircraft("livery-red", [("P_Red", "P")]));
        w.Add(Aircraft("livery-blue", [("P_Blue", "P")]));

        Place(w);

        var card = Assert.Single(w.State.Cards);
        Assert.Equal(3, w.State.PackagesOn(card.Id).Count);
    }

    [Fact]
    public void A_package_providing_several_containers_gives_one_card()
    {
        var w = NewWorld();
        w.Add(Aircraft("aerosoft-crj", [("CRJ_550", null), ("CRJ_700", null), ("CRJ_900", null), ("CRJ_1000", null)]));
        w.Add(Aircraft("crj-livery", [("CRJ_900_Red", "CRJ_900")]));

        Place(w);

        var card = Assert.Single(w.State.Cards);
        Assert.Equal(2, w.State.PackagesOn(card.Id).Count);
    }

    [Fact]
    public void A_livery_covering_two_aircraft_joins_the_first_by_name_and_never_merges_them()
    {
        var w = NewWorld();
        w.Add(Aircraft("b-plane", [("Beta", null)]));
        w.Add(Aircraft("a-plane", [("Alpha", null)]));
        w.Add(Aircraft("both-livery", [("L_Beta", "Beta"), ("L_Alpha", "Alpha")]));

        Place(w);

        Assert.Equal(2, w.State.Cards.Count);
        Assert.Equal(w.State.CardOf(Key("a-plane"))!.Id, w.State.CardOf(Key("both-livery"))!.Id);
        Assert.NotEqual(w.State.CardOf(Key("a-plane"))!.Id, w.State.CardOf(Key("b-plane"))!.Id);
    }

    [Fact]
    public void A_later_package_joins_the_oldest_card_providing_or_repainting_its_container()
    {
        var w = NewWorld();
        w.Add(Aircraft("p1", [("X", null)]));
        w.Add(Aircraft("p2", [("X", null)]));
        var first = AircraftCard(w, "p1");
        AircraftCard(w, "p2");
        w.Add(Aircraft("later", [("X_Red", "x")]));
        w.State.Cards.Reverse();

        Place(w);

        Assert.Equal(first.Id, w.State.CardOf(Key("later"))!.Id);
        Assert.Equal(2, w.State.Cards.Count);
    }

    [Fact]
    public void A_card_that_provides_a_container_wins_over_an_older_card_that_only_repaints_it()
    {
        var w = NewWorld();
        w.Add(Aircraft("a-plane", [("Alpha", null)]));
        w.Add(Aircraft("both-livery", [("L_Alpha", "Alpha"), ("L_Beta", "Beta")]));
        w.Add(Aircraft("b-plane", [("Beta", null)]));
        var older = AircraftCard(w, "a-plane", "both-livery");
        var provider = AircraftCard(w, "b-plane");
        w.Add(Aircraft("beta-livery", [("L_Beta2", "Beta")]));
        w.Add(Aircraft("b-plane-update", [("Beta", null)]));

        Place(w);

        Assert.NotEqual(older.Id, provider.Id);
        Assert.Equal(provider.Id, w.State.CardOf(Key("beta-livery"))!.Id);
        Assert.Equal(provider.Id, w.State.CardOf(Key("b-plane-update"))!.Id);
        Assert.Equal(2, w.State.Cards.Count);
    }

    [Fact]
    public void A_not_found_package_keeps_its_containers_for_matching()
    {
        var w = NewWorld();
        w.Add(Aircraft("crj-1-0", [("CRJ_700", null)]));
        w.Place();
        w.Without("crj-1-0");
        w.Add(Aircraft("crj-1-1", [("CRJ_700", null)]));

        Place(w);

        Assert.Single(w.State.Cards);
        Assert.Equal(w.State.CardOf(Key("crj-1-0"))!.Id, w.State.CardOf(Key("crj-1-1"))!.Id);
    }

    [Fact]
    public void Two_makers_of_one_type_get_two_cards()
    {
        var w = NewWorld();
        w.Add(Aircraft("asobo-c152-pack", [("Asobo_C152", null)]));
        w.Add(Aircraft("carenado-c152-pack", [("Carenado_C152", null)]));

        Place(w);

        Assert.Equal(2, w.State.Cards.Count);
    }

    [Fact]
    public void A_livery_for_a_built_in_aircraft_gets_a_card_that_later_liveries_join()
    {
        var w = NewWorld();
        w.SimManaged(Aircraft("Asobo_C152", [("C152", null)], source: PackageSource.SimManaged));
        w.Add(Aircraft("c152-red", [("C152_Red", "C152")]));
        Place(w);
        w.Add(Aircraft("c152-blue", [("C152_Blue", "C152")]));

        Place(w);

        var card = Assert.Single(w.State.Cards);
        Assert.Equal(CardTab.Aircraft, card.Tab);
        Assert.Equal(2, w.State.PackagesOn(card.Id).Count);
    }

    [Fact]
    public void A_livery_whose_aircraft_nothing_provides_gets_its_own_card_on_the_other_tab()
    {
        var w = NewWorld();
        w.Add(Aircraft("lonely-livery", [("L_Red", "Nowhere")]));

        Place(w);

        var card = Assert.Single(w.State.Cards);
        Assert.Equal(CardTab.Other, card.Tab);
    }

    [Fact]
    public void Instruments_packages_get_their_own_card_on_the_instruments_tab()
    {
        var w = NewWorld();
        w.Add(Build("instr-a", "INSTRUMENTS"));
        w.Add(Build("instr-b", "INSTRUMENTS"));

        Place(w);

        Assert.Equal(2, w.State.Cards.Count);
        Assert.All(w.State.Cards, c => Assert.Equal(CardTab.Instruments, c.Tab));
    }

    // ---- initial tab ----

    [Fact]
    public void An_instruments_package_starts_on_instruments_and_an_aircraft_with_a_provider_on_aircraft()
    {
        var w = NewWorld();
        w.Add(Build("instr", "INSTRUMENTS"));
        w.Add(Aircraft("ac-prov", [("Cessna", null)]));
        w.Add(Aircraft("livery-only", [("L_Red", "Elsewhere")]));

        Place(w);

        Assert.Equal(CardTab.Instruments, w.State.CardOf(Key("instr"))!.Tab);
        Assert.Equal(CardTab.Aircraft, w.State.CardOf(Key("ac-prov"))!.Tab);
        Assert.Equal(CardTab.Other, w.State.CardOf(Key("livery-only"))!.Tab);
    }

    [Fact]
    public void A_livery_of_a_built_in_aircraft_starts_on_aircraft_because_the_base_is_locked()
    {
        var w = NewWorld();
        w.SimManaged(Aircraft("Asobo_C152", [("C152", null)], source: PackageSource.SimManaged));
        w.Add(Aircraft("c152-livery", [("C152_Red", "C152")]));

        Place(w);

        Assert.Equal(CardTab.Aircraft, Assert.Single(w.State.Cards).Tab);
    }

    [Fact]
    public void Scenery_without_icao_codes_starts_on_assets_only_when_nothing_is_placed()
    {
        var w = NewWorld();
        w.Add(Scenery("assets") with { BglSectionTypes = [0x2B] });
        w.Add(Scenery("lands") with { BglSectionTypes = [0x25] });

        Place(w);

        Assert.Equal(CardTab.Assets, w.State.CardOf(Key("assets"))!.Tab);
        Assert.Equal(CardTab.Landscapes, w.State.CardOf(Key("lands"))!.Tab);
    }
}
