using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;
using AddonManager.Core.Tests.Cards;

namespace AddonManager.Core.Tests.Scenarios;

/// <summary>The GSX status over Rickard's sanitised content (spec 8, 13): the GSX folder and the Packages' shipped profiles of the real machine.</summary>
public sealed class GsxScenarioTests
{
    private static readonly (BoardInput Input, Board Board) Wall = Build();

    private static (BoardInput, Board) Build()
    {
        var (state, input) = RickardWorld.Build();
        Settlement.Apply(state, input.Reanalyze(state), World.Now);
        var settled = input.Reanalyze(state);
        return (settled, BoardBuilder.Build(state, settled));
    }

    private static AirportCode Code(string text) => Assert.IsType<AirportCode>(AirportCode.TryParse(text, out var c) ? c : null);

    private static Card CardOf(string name)
        => Assert.IsType<Card>(Wall.Board.CardOf(PackageKey.Folder(new PackageName(name))));

    private static GsxAirport Airport(string code)
        => Assert.Single(Wall.Board.CardsAt(Code(code))).GsxAirports.Single(a => a.Code == Code(code));

    [Theory]
    [InlineData("LDSP")]
    [InlineData("LFMN")]
    [InlineData("LFPG")]
    public void Two_profiles_at_ldsp_lfmn_and_lfpg(string code)
    {
        var card = Assert.Single(Wall.Board.CardsAt(Code(code)));

        Assert.Equal(GsxMark.TwoProfiles, card.Gsx);
        Assert.Equal(2, Airport(code).Active.Count);
        var warning = Assert.Single(card.Warnings, w => w.Kind == WarningKind.TwoGsxProfiles);
        Assert.Equal(Code(code), warning.Code);
    }

    [Fact]
    public void Shipped_profiles_at_essa_esgg_eddf_and_epkk()
    {
        var essa = Assert.Single(Airport("ESSA").Shipped);
        Assert.Equal(new PackageName("orbx-airport-essa-stockholm-arlanda"), essa.Package);
        Assert.EndsWith(@"GSX\essa-bwib1r.ini", essa.IniPath);
        Assert.EndsWith(@"GSX\essa-orbx.py", Assert.Single(essa.ScriptPaths));
        Assert.False(essa.InGsxFolder);

        var esgg = Assert.Single(Airport("ESGG").Shipped);
        Assert.Equal(new PackageName("orbx-airport-esgg-gothenburg"), esgg.Package);
        Assert.EndsWith(@"GSX\esgg-4k1lpd.ini", esgg.IniPath);
        Assert.False(esgg.InGsxFolder);
        Assert.Equal(GsxMark.Installed, Airport("ESGG").Status);

        // Orbx lists neither of these in layout.json, so they are found by walking the Package.
        var eddf = Assert.Single(Airport("EDDF").Shipped);
        Assert.Equal(new PackageName("avioteksoftwarevce-airport-eddf-frankfurt-main"), eddf.Package);
        Assert.EndsWith(@"GSX-PROFILE\EDDF-7g8nwm.ini", eddf.IniPath);

        var epkk = Assert.Single(Airport("EPKK").Shipped);
        Assert.Equal(new PackageName("papadelta-epkk"), epkk.Package);
        Assert.EndsWith(@"GSX-Profile-EPKK\epkk-papadelta.ini", epkk.IniPath);
        Assert.EndsWith(@"GSX-Profile-EPKK\epkk-papadelta.py", Assert.Single(epkk.ScriptPaths));
        Assert.Equal(GsxMark.Missing, Airport("EPKK").Status);
    }

    [Fact]
    public void Every_profile_only_card_is_on_airports()
    {
        var profilePackages = Wall.Board.Cards.SelectMany(c => c.Packages).Count(p => p.Kind == PackageKind.GsxProfile);
        var profileOnly = Wall.Board.Cards.Where(c => c.Packages.All(p => p.Kind == PackageKind.GsxProfile)).ToList();

        Assert.Equal(34, profilePackages);
        Assert.NotEmpty(profileOnly);
        Assert.All(profileOnly, c => Assert.Equal((Category.Scenery, CardTab.Airports), (c.Section, c.Tab)));
    }

    [Fact]
    public void No_gsx_data_on_aerosoft_crj()
    {
        var card = CardOf("aerosoft-crj");

        Assert.Equal(GsxMark.None, card.Gsx);
        Assert.Empty(card.GsxAirports);
    }

    [Fact]
    public void Panel_state_ini_files_of_aircraft_are_no_gsx_profiles()
    {
        // cold_and_dark.ini and apu_running.ini yield the ICAO-like codes COLD and APU, so they are cached as shipped files.
        Assert.NotEmpty(Wall.Input.Contents.Single(c => c.Name == new PackageName("inibuilds-aircraft-a350")).GsxFiles);
        Assert.NotEmpty(Wall.Input.Contents.Single(c => c.Name == new PackageName("microsoft-aircraft-a310-300")).GsxFiles);

        var card = CardOf("inibuilds-aircraft-a350");

        Assert.Equal(GsxMark.None, card.Gsx);
        Assert.Empty(card.GsxAirports);
        Assert.Null(Wall.Board.CardOf(PackageKey.Folder(new PackageName("microsoft-aircraft-a310-300"))));
        Assert.DoesNotContain(Wall.Board.Cards, c => c.Codes.Any(code => code.Value is "COLD" or "APU"));
    }

    [Fact]
    public void The_gsx_folder_is_found_and_everything_in_it_is_recognised()
    {
        Assert.False(Wall.Board.Banner.GsxFolderMissing);
        Assert.Empty(Wall.Board.GsxNotRecognised);
    }
}
