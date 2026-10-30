using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Core.Tests.Cards;
using AddonManager.Core.Tests.Fakes;

namespace AddonManager.Core.Tests.Scenarios;

/// <summary>The card wall over Rickard's sanitised content (spec 13); every expectation is a real-world example from spec 6.4 to 6.6.</summary>
public sealed class BoardScenarioTests
{
    private static (UserState State, BoardInput Input, Board Board, IReadOnlyDictionary<PackageName, PackageSummary> Catalog) Wall(LibraryRoot[]? libraries = null)
    {
        var (state, input) = RickardWorld.Build(libraries);
        Settlement.Apply(state, input.Reanalyze(state), World.Now);
        var settled = input.Reanalyze(state);
        return (state, input, BoardBuilder.Build(state, settled), PackageCatalog.Build(state, settled));
    }

    private static readonly Board Shared = Wall().Board;

    private static AirportCode Code(string text) => Assert.IsType<AirportCode>(AirportCode.TryParse(text, out var c) ? c : null);

    private static string[] Codes(Card card) => [.. card.Codes.Select(c => c.Value)];

    private static Card CardOf(string name, Board? board = null)
        => Assert.IsType<Card>((board ?? Shared).CardOf(PackageKey.Folder(new PackageName(name))));

    private static CardPackage PackageOf(string name)
        => CardOf(name).Packages.First(p => p.Name == new PackageName(name));

    private static string[] Names(Card card) => [.. card.Packages.Select(p => p.Name.Value).Order(StringComparer.Ordinal)];

    [Fact]
    public void Eham_is_one_airport_card_with_three_packages()
    {
        var card = Assert.Single(Shared.CardsAt(Code("EHAM")));

        Assert.Equal(Category.Scenery, card.Section);
        Assert.Equal(CardTab.Airports, card.Tab);
        Assert.Equal(["EHAM"], Codes(card));
        Assert.Equal(
            ["brickbybrickdesign-airport-eham-amsterdam", "duijffmedia-airport-eham-amsterdam", "duijffmedia-schiphol-amsterdam-mega-enhancement-lighting"],
            Names(card));
    }

    [Fact]
    public void Mount_mulligan_is_a_several_card_with_both_icao_codes()
    {
        var card = CardOf("sltcreations-airport-yq05-mount-mulligan");

        Assert.True(card.Several);
        Assert.Equal(["YMUZ", "YQ05"], Codes(card));
        Assert.Equal(CardTab.Airports, card.Tab);
        Assert.Equal(PackageOf("sltcreations-airport-yq05-mount-mulligan").DisplayName, card.Name);
    }

    [Fact]
    public void Asf_mission_demo_is_a_several_card()
    {
        var card = CardOf("asf-mission-demo");

        Assert.True(card.Several);
        Assert.Equal(["FEFA", "FEGK"], Codes(card));
        Assert.Equal(Category.Scenery, card.Section);
        Assert.Equal(CardTab.Airports, card.Tab);
    }

    [Fact]
    public void Alice_springs_is_a_plain_ybas_card()
    {
        var card = CardOf("tag21-airport-ybas-alice-springs");

        Assert.Equal(["YBAS"], Codes(card));
        Assert.False(card.Several);
        Assert.Equal(CardTab.Airports, card.Tab);
    }

    [Fact]
    public void Both_sal1800_170b_packages_share_one_aircraft_card()
    {
        var card = CardOf("sal1800-cessna-170b-backcountry");

        Assert.NotNull(card.Id);
        Assert.Equal(card.Id, CardOf("sal1800-cessna-super-170b-OPTIONAL").Id);
        Assert.Equal(Category.Aircraft, card.Section);
        Assert.Equal(["C170"], card.TypeDesignators);
        Assert.Contains("C170", card.Tags);
    }

    [Fact]
    public void The_crj_is_one_card_for_four_containers()
    {
        var (_, _, board, catalog) = Wall();
        var crj = catalog[new PackageName("aerosoft-crj")];
        var card = Assert.IsType<Card>(board.CardOf(PackageKey.Folder(crj.Name)));

        Assert.NotNull(card.Id);
        Assert.Equal(Category.Aircraft, card.Section);
        Assert.Contains(card.Packages, p => p.Name == new PackageName("AS_CRJ_900_SAS_New_EIGEH"));
        Assert.Equal(4, crj.Containers.Count);
        Assert.All(
            catalog.Values.Where(s => s.Containers.Any(c => crj.Containers.Contains(c, StringComparer.OrdinalIgnoreCase))),
            s => Assert.Equal(card.Id, board.CardOf(PackageKey.Folder(s.Name))?.Id));
    }

    [Fact]
    public void Asset_packs_start_on_assets()
    {
        var card = CardOf("esd-modellib-eolib");

        Assert.Equal(Category.Scenery, card.Section);
        Assert.Equal(CardTab.Assets, card.Tab);
    }

    [Fact]
    public void Photo_textures_without_bgl_are_other()
    {
        var (state, _, _, catalog) = Wall();

        Assert.Equal(Category.Other, state.Packages[new PackageName("yara-cj-aerial")].Section);
        Assert.Equal(Category.Other, catalog[new PackageName("yara-cj-aerial")].FixedSection);
        Assert.Equal(Category.Other, CardOf("yara-cj-aerial").Section);
    }

    [Fact]
    public void With_only_scenery_registered_aircraft_packages_are_not_shown()
    {
        LibraryRoot[] sceneryOnly = [new(@"C:\Sim\Addons", [Category.Scenery]), new(@"C:\Sim\Orbx Library\msfs", [Category.Scenery])];
        var (_, _, board, _) = Wall(sceneryOnly);

        // Only Library Packages are ruled out: Fixed and External Community entries are held by no Library and always get cards.
        Assert.DoesNotContain(board.Cards, c => c.Section == Category.Aircraft && c.Packages.Any(p => p.Library is not null));
        Assert.Contains(board.NotShown, n => n.Name == new PackageName("aerosoft-crj"));
        Assert.Contains(board.NotShown, n => n.Name == new PackageName("sal1800-cessna-170b-backcountry"));
    }

    [Fact]
    public void Settling_twice_changes_nothing()
    {
        var (state, input) = RickardWorld.Build();

        Assert.True(Settlement.Apply(state, input.Reanalyze(state), World.Now));
        Assert.False(Settlement.Apply(state, input.Reanalyze(state), World.Now));
    }

    [Fact]
    public void Every_package_is_on_at_most_one_card_and_no_card_is_empty()
    {
        var (state, _, board, catalog) = Wall();
        var held = board.Cards.SelectMany(c => c.Packages).Select(p => (p.Kind, p.Name)).ToList();

        Assert.Equal(held.Count, held.Distinct().Count());
        Assert.All(board.Cards, c => Assert.NotEmpty(c.Packages));
        Assert.All(state.Cards, c => Assert.NotEmpty(state.PackagesOn(c.Id)));
        Assert.Empty(board.NotShown);
        Assert.Empty(catalog.Keys.Except(held.Where(h => h.Kind == PackageKind.Folder).Select(h => h.Name)));
    }

    [Fact]
    public void Lszr_and_gran_canaria_are_landscapes()
    {
        Assert.Equal(CardTab.Landscapes, CardOf("005builds-lszrsceneryv11").Tab);
        Assert.Equal(CardTab.Landscapes, CardOf("grancanaria").Tab);
    }

    [Fact]
    public void The_asset_packs_are_exactly_the_eight_named()
    {
        var assets = Shared.On(Category.Scenery, CardTab.Assets).SelectMany(c => c.Packages).ToList();

        Assert.Equal(
            [
                "esd-modellib-eolib", "library1v14-cbj", "library2v14-cbj", "mikeaat-assetpack", "navigraph-navdata", "navigraph-navdata-base",
                "sayintentions-fly-in-library", "us-firedeptpack",
            ],
            assets.Select(p => p.Name.Value).Order(StringComparer.Ordinal));

        foreach (var locked in new[] { "navigraph-navdata", "navigraph-navdata-base", "sayintentions-fly-in-library" })
        {
            Assert.Equal(LockReason.HeldByEntry, assets.Single(p => p.Name.Value == locked).Lock);
        }
    }

    [Fact]
    public void Human_library_animated_is_a_landscape()
        => Assert.Equal(CardTab.Landscapes, CardOf("human-library-animated").Tab);

    [Theory]
    [InlineData("yara-cj-aerial")]
    [InlineData("ywbl-cj-aerial")]
    [InlineData("yhsm-txt-cj")]
    [InlineData("yhsm-txt-e-cj")]
    [InlineData("tsr-windy-things")]
    [InlineData("tsr-windy-things-AUS")]
    [InlineData("fsdreamteam-gsx-pro")]
    [InlineData("fsdreamteam-gsx-world-of-jetways")]
    [InlineData("orbx-volanta-aerocaches-jw")]
    [InlineData("neofly-neofly-objects")]
    [InlineData("neofly-neofly-smokes")]
    public void No_bgl_scenery_packages_are_in_other(string name)
        => Assert.Equal(Category.Other, CardOf(name).Section);

    [Fact]
    public void Instruments_tab_holds_the_two_instrument_packages()
    {
        var names = Shared.On(Category.Aircraft, CardTab.Instruments).SelectMany(c => c.Packages).Select(p => p.Name.Value).Order(StringComparer.Ordinal);

        Assert.Equal(["pms50-instrument-gtn750-pro", "sky4sim-os"], names);
    }

    [Fact]
    public void Mge_c152_is_a_mod_card_with_the_built_in_c152_as_its_base()
    {
        var card = CardOf("mge-asobo-aircraft-c152");

        Assert.Equal(AircraftRole.Mod, PackageOf("mge-asobo-aircraft-c152").Role);
        Assert.Contains(card.BuiltIns, b => b.Name == new PackageName("asobo-aircraft-c152") && b.Role == AircraftRole.LockedBase);
    }

    [Fact]
    public void Orbx_volanta_aerocaches_is_on_aircraft_other()
    {
        var card = CardOf("orbx-volanta-aerocaches");

        Assert.Equal(Category.Aircraft, card.Section);
        Assert.Equal(CardTab.Other, card.Tab);
    }

    [Theory]
    [InlineData("navigraph-avionics-g1000")]
    [InlineData("navigraph-avionics-g3000-g5000")]
    [InlineData("navigraph-avionics-g3x")]
    [InlineData("fsltl-traffic-base")]
    [InlineData("apronlife-jetway-helper")]
    public void Navigraph_avionics_and_air_traffic_are_in_other(string name)
    {
        var card = CardOf(name);

        Assert.Equal(Category.Other, card.Section);
        Assert.Equal(CardTab.Other, card.Tab);
    }

    [Theory]
    [InlineData("neofly-neofly-cargo")]
    [InlineData("neofly-neofly-objects")]
    [InlineData("neofly-neofly-smokes")]
    public void The_neofly_library_copies_are_one_locked_package_each(string name)
    {
        var matches = Shared.Cards.SelectMany(c => c.Packages).Where(p => p.Name == new PackageName(name)).ToList();

        var package = Assert.Single(matches);
        Assert.Equal(LockReason.HeldByEntry, package.Lock);
        Assert.Equal(EntryClass.External, package.Holder?.Class);
        Assert.StartsWith(@"C:\Sim\Neofly\addons", package.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(package.Copies, c => c.StartsWith(@"C:\Sim\Addons", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_card_comes_from_sim_managed_liveries_alone()
        => Assert.DoesNotContain(
            Shared.Cards,
            c => c.Packages.All(p => p.Source == PackageSource.SimManaged && p.Role == AircraftRole.Livery));

    [Fact]
    public void Orbx_packages_have_no_tags()
    {
        const string OrbxLibrary = @"C:\Sim\Orbx Library\msfs";
        var orbx = Shared.Cards.SelectMany(c => c.Packages)
            .Where(p => p.Path.StartsWith(OrbxLibrary, StringComparison.OrdinalIgnoreCase)
                || p.Copies.Any(c => c.StartsWith(OrbxLibrary, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.NotEmpty(orbx);
        Assert.All(orbx, p => Assert.Empty(p.Tags));
    }

    [Fact]
    public void With_sections_restricted_the_photo_textures_are_not_shown()
    {
        LibraryRoot[] restricted = [new(@"C:\Sim\Addons", [Category.Scenery, Category.Aircraft]), new(@"C:\Sim\Orbx Library\msfs", LibraryRoot.AllSections)];
        var (_, _, board, _) = Wall(restricted);

        foreach (var hidden in new[] { "yara-cj-aerial", "ywbl-cj-aerial", "yhsm-txt-cj", "yhsm-txt-e-cj", "tsr-windy-things", "tsr-windy-things-AUS" })
        {
            Assert.Contains(board.NotShown, n => n.Name == new PackageName(hidden));
        }
    }

    [Fact]
    public void A_not_shown_photo_texture_can_be_shown_in_scenery_and_then_given_an_icao_code()
    {
        LibraryRoot[] restricted = [new(@"C:\Sim\Addons", [Category.Scenery, Category.Aircraft]), new(@"C:\Sim\Orbx Library\msfs", LibraryRoot.AllSections)];
        var (state, input, _, _) = Wall(restricted);
        var editor = new CardEditor(new InMemoryUserStateStore(), TimeProvider.System);
        var name = new PackageName("yara-cj-aerial");

        var id = editor.ShowIn(state, input.Reanalyze(state), name, Category.Scenery);
        var shown = BoardBuilder.Build(state, input.Reanalyze(state));
        var card = CardOf("yara-cj-aerial", shown);
        Assert.Equal(id, card.Id);
        Assert.Equal(Category.Scenery, card.Section);
        Assert.Equal(CardTab.Landscapes, card.Tab);

        // Spec 6.5: typing never merges. On Rickard's machine the Airport card "YARA Ararat" already has YARA, so typing it is rejected
        // and the card stays put (spec 6.4's "typing the ICAO code moves the card to Airports" holds for a code no other card has).
        var taken = editor.AddCode(state, shown, id, "YARA");
        Assert.Equal(CodeResult.Taken, taken.Result);
        Assert.Equal("YARA Ararat", taken.HolderName);
        Assert.Equal(CardTab.Landscapes, CardOf("yara-cj-aerial", BoardBuilder.Build(state, input.Reanalyze(state))).Tab);

        Assert.Equal(CodeResult.Added, editor.AddCode(state, shown, id, "YAR9").Result);
        var coded = CardOf("yara-cj-aerial", BoardBuilder.Build(state, input.Reanalyze(state)));
        Assert.Equal(Category.Scenery, coded.Section);
        Assert.Equal(CardTab.Airports, coded.Tab);
        Assert.Equal(["YAR9"], Codes(coded));
    }
}
