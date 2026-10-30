using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class GsxCardTests
{
    private const string EpkkIni = @"GSX-Profile-EPKK\epkk-x.ini";
    private const string EpkkScript = @"GSX-Profile-EPKK\epkk-x.py";

    private static World NewWorld()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons");
        return w;
    }

    private static Board Wall(World w)
    {
        w.Place();
        return BoardBuilder.Build(w.State, w.Input());
    }

    private static Card OnlyCard(Board board) => Assert.Single(board.Cards);

    [Fact]
    public void An_airport_card_with_one_profile_is_marked_installed()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Profile("eham", "EHAM");

        var card = OnlyCard(Wall(w));

        Assert.Equal(GsxMark.Installed, card.Gsx);
        Assert.Equal([new PackageName("eham")], Assert.Single(card.GsxAirports).Active);
    }

    [Fact]
    public void An_airport_card_without_a_profile_is_marked_missing_with_a_search_link()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));

        var card = OnlyCard(Wall(w));

        Assert.Equal(GsxMark.Missing, card.Gsx);
        Assert.Equal("https://flightsim.to/others/gsx-pro/search/eham", Assert.Single(card.GsxAirports).SearchLink);
    }

    [Fact]
    public void Two_profiles_mark_the_card_and_warn()
    {
        var w = NewWorld();
        w.Add(Scenery("lfpg-x", ["LFPG"]));
        w.Profile("lfpg-b", "LFPG");
        w.Profile("lfpg-a", "LFPG");

        var card = OnlyCard(Wall(w));

        Assert.Equal(GsxMark.TwoProfiles, card.Gsx);
        var warning = Assert.Single(card.Warnings, x => x.Kind == WarningKind.TwoGsxProfiles);
        Assert.Equal((Code("LFPG"), new PackageName("lfpg-a")), (warning.Code, warning.Package));
    }

    [Fact]
    public void A_card_sharing_an_icao_code_with_two_profiles_warns_too()
    {
        var w = NewWorld();
        w.Add(Scenery("yq05-x", ["YQ05"]));
        w.Add(Scenery("pair", ["YMUZ", "YQ05"]));
        w.Profile("yq05-a", "YQ05");
        w.Profile("yq05-b", "YQ05");

        var board = Wall(w);

        Assert.Equal(2, board.Cards.Count);
        foreach (var card in board.Cards)
        {
            var warning = Assert.Single(card.Warnings, x => x.Kind == WarningKind.TwoGsxProfiles);
            Assert.Equal(Code("YQ05"), warning.Code);
        }
    }

    [Fact]
    public void A_several_card_is_marked_by_its_first_icao_code()
    {
        var w = NewWorld();
        w.Add(Scenery("pair", ["YMUZ", "YQ05"]));
        w.Place();
        w.Profile("yq05", "YQ05");

        var card = OnlyCard(Wall(w));

        Assert.Equal(GsxMark.Missing, card.Gsx);
        Assert.Equal([Code("YMUZ"), Code("YQ05")], card.GsxAirports.Select(a => a.Code));
        Assert.Equal([GsxMark.Missing, GsxMark.Installed], card.GsxAirports.Select(a => a.Status));
    }

    [Fact]
    public void A_card_without_an_icao_code_has_no_gsx_mark()
    {
        var w = NewWorld();
        w.Add(Scenery("landscape", hasBgl: true));

        var card = OnlyCard(Wall(w));

        Assert.Empty(card.Codes);
        Assert.Equal(GsxMark.None, card.Gsx);
        Assert.Empty(card.GsxAirports);
    }

    [Fact]
    public void A_gsx_folder_that_cannot_be_read_shows_no_mark_and_no_warning()
    {
        var w = NewWorld();
        w.Add(Scenery("eham-x", ["EHAM"]));
        w.Add(Scenery("lfpg-x", ["LFPG"]));
        w.Profile("eham", "EHAM");
        w.Profile("lfpg-a", "LFPG");
        w.Profile("lfpg-b", "LFPG");
        w.Place();
        w.WithoutProfile("eham");
        w.WithoutProfile("lfpg-a");
        w.WithoutProfile("lfpg-b");
        w.GsxFolderFound = false;

        var board = BoardBuilder.Build(w.State, w.Input());

        Assert.Equal(2, board.Cards.Count);
        Assert.All(board.Cards, card =>
        {
            Assert.Equal(GsxMark.None, card.Gsx);
            Assert.Empty(card.GsxAirports);
            Assert.DoesNotContain(card.Warnings, x => x.Kind == WarningKind.TwoGsxProfiles);
        });
        Assert.True(board.Banner.GsxFolderMissing);
    }

    [Fact]
    public void A_shipped_profile_is_listed_with_its_scripts_and_does_not_count()
    {
        var w = NewWorld();
        w.Add(Scenery("epkk-pkg", ["EPKK"]) with { GsxFiles = [EpkkIni, EpkkScript] });

        var card = OnlyCard(Wall(w));

        Assert.Equal(GsxMark.Missing, card.Gsx);
        var shipped = Assert.Single(Assert.Single(card.GsxAirports).Shipped);
        Assert.Equal((Code("EPKK"), new PackageName("epkk-pkg"), @"C:\Sim\Addons\epkk-pkg\" + EpkkIni, false), (shipped.Code, shipped.Package, shipped.IniPath, shipped.InGsxFolder));
        Assert.Equal([@"C:\Sim\Addons\epkk-pkg\" + EpkkScript], shipped.ScriptPaths);
    }

    [Fact]
    public void A_shipped_profile_for_another_icao_code_is_not_listed()
    {
        var w = NewWorld();
        w.Add(Scenery("epkk-pkg", ["EPKK"]) with { GsxFiles = [@"GSX\test.ini", @"GSX\epkr-x.ini"] });

        var card = OnlyCard(Wall(w));

        Assert.Empty(Assert.Single(card.GsxAirports).Shipped);
    }

    [Fact]
    public void An_aircraft_package_with_an_icao_like_ini_shows_no_gsx_data_and_makes_no_card()
    {
        var w = NewWorld();
        w.Add(Aircraft("ac", [("Cessna", null)]) with { GsxFiles = ["A320.ini"] });

        var board = Wall(w);

        var card = OnlyCard(board);
        Assert.Equal(Category.Aircraft, card.Section);
        Assert.Equal(GsxMark.None, card.Gsx);
        Assert.Empty(card.GsxAirports);
        Assert.Empty(w.State.Profiles);
    }

    [Fact]
    public void A_shipped_profile_of_a_package_whose_library_is_not_found_is_not_listed()
    {
        var w = NewWorld();
        w.Add(Scenery("epkk-pkg", ["EPKK"]) with { GsxFiles = [EpkkIni] });
        w.Place();
        w.Unreachable(w.State.Libraries[0]);

        var card = OnlyCard(BoardBuilder.Build(w.State, w.Input()));

        Assert.Equal(Presence.LibraryNotFound, card.Packages[0].Presence);
        Assert.Empty(Assert.Single(card.GsxAirports).Shipped);
    }

    [Fact]
    public void A_shipped_profile_already_in_the_gsx_folder_is_marked()
    {
        var w = NewWorld();
        w.Add(Scenery("epkk-pkg", ["EPKK"]) with { GsxFiles = [EpkkIni] });
        w.Profile("epkk-x", "EPKK");

        var card = OnlyCard(Wall(w));

        var airport = Assert.Single(card.GsxAirports);
        Assert.True(Assert.Single(airport.Shipped).InGsxFolder);
        Assert.Equal(GsxMark.Installed, airport.Status);
    }

    [Fact]
    public void A_shipped_profile_whose_off_file_is_in_the_gsx_folder_is_marked()
    {
        var w = NewWorld();
        w.Add(Scenery("epkk-pkg", ["EPKK"]) with { GsxFiles = [EpkkIni] });
        w.Profile("epkk-x", "EPKK", disabled: true);

        var airport = Assert.Single(OnlyCard(Wall(w)).GsxAirports);

        Assert.True(Assert.Single(airport.Shipped).InGsxFolder);
        Assert.Equal(GsxMark.Missing, airport.Status);
    }

    [Fact]
    public void Scripts_are_listed_with_every_card_at_the_icao_code()
    {
        var w = NewWorld();
        w.Add(Scenery("yq05-x", ["YQ05"]));
        w.Add(Scenery("pair", ["YMUZ", "YQ05"]));
        w.Script("yq05-x.py", "YQ05");

        var board = Wall(w);

        Assert.Equal(2, board.Cards.Count);
        foreach (var card in board.Cards)
        {
            Assert.Equal([@"C:\GSX\yq05-x.py"], card.GsxAirports.Single(a => a.Code == Code("YQ05")).Scripts);
        }
    }
}
