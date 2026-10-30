using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.Scenarios;

/// <summary>Content rules against the scan of Rickard's real install (2026-10). Packages are found by name; names may have several copies (C8).</summary>
public class ContentScenarioTests
{
    private static readonly IReadOnlyList<PackageContent> Contents = ContentFixtureLoader.Load("rickard-content");

    private static readonly ContentAnalysis A = ContentAnalyzer.Analyze(Contents, new ContentSettings());

    private static AirportCode Code(string text) => Content.ContentBuilder.Code(text);

    private static IReadOnlyList<PackageContent> Copies(string name)
    {
        var copies = Contents.Where(c => c.Name == new PackageName(name)).ToList();
        Assert.NotEmpty(copies);
        return copies;
    }

    private static List<string> LinksOf(string name)
        => A.AirportLinks.Where(l => l.Package.Name == new PackageName(name)).Select(l => l.Code.Value).Distinct().Order().ToList();

    private static IEnumerable<string> LinkedCodes(string name) => LinksOf(name);

    private static List<string> LinkedNames(string code, PackageSource source)
        => A.AirportLinks
            .Where(l => l.Code == Code(code) && l.Package.Source == source)
            .Select(l => l.Package.Name.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static Category Cat(string name) => A.Categories[Copies(name)[0].Path];

    private static AircraftRole Role(string name) => A.Aircraft.RoleOf(Copies(name)[0]);

    [Fact]
    public void Eham_has_its_three_library_packages()
        => Assert.Equal(
            ["brickbybrickdesign-airport-eham-amsterdam", "duijffmedia-airport-eham-amsterdam", "duijffmedia-schiphol-amsterdam-mega-enhancement-lighting"],
            LinkedNames("EHAM", PackageSource.Library));

    [Fact]
    public void Lszr_and_gran_canaria_have_no_airport()
    {
        Assert.Empty(LinksOf("005builds-lszrsceneryv11"));
        Assert.Empty(LinksOf("grancanaria"));
    }

    [Fact]
    public void Voloport_and_neofly_are_kept_out()
    {
        Assert.DoesNotContain(A.AirportLinks, l => l.Code.Value.StartsWith("VOLC") || l.Code.Value == "NEOF2");
        Assert.DoesNotContain(A.Airports.Keys, c => c.Value.StartsWith("VOLC") || c.Value == "NEOF2");
    }

    [Fact]
    public void Verified_large_packages_are_excluded()
        => Assert.Superset(
            new[] { "fs-base-genericairports", "fs-base-nav", "navigraph-navdata", "fsdreamteam-gsx-world-of-jetways-2020", "microsoft-nordics2-point-of-interest" }.ToHashSet(StringComparer.OrdinalIgnoreCase),
            A.Excluded.Select(c => c.Name.Value).ToHashSet(StringComparer.OrdinalIgnoreCase));

    [Theory]
    [InlineData("asobo-airport-eddf-frankfurt", "EDDF")]
    [InlineData("asobo-airport-egll-heathrow", "EGLL")]
    [InlineData("asobo-airport-kden-denver", "KDEN")]
    [InlineData("asobo-airport-lemd-adolfo-suarez", "LEMD")]
    [InlineData("asobo-airport-omdb-dubai-intl", "OMDB")]
    [InlineData("asobo-airport-heca-cairo", "HECA")]
    [InlineData("asobo-airport-fact-cape-town", "FACT")]
    [InlineData("asobo-airport-lfcs-saucats", "LFCS")]
    public void Encrypted_official_airports_have_code_name_and_position(string package, string code)
    {
        Assert.Contains(code, LinkedCodes(package));
        Assert.NotNull(A.Airports[Code(code)].Name);
        Assert.DoesNotContain("TT:", A.Airports[Code(code)].Name);
        Assert.NotNull(A.Airports[Code(code)].Position);
    }

    [Fact]
    public void Five_character_codes_link_by_record()
        => Assert.Contains(A.AirportLinks, l => l.Code.Value == "YALR1" && l.Reason == AirportLinkReason.Record);

    [Fact]
    public void Other_category_cases()
    {
        Assert.Equal(Category.Other, Cat("fsdreamteam-gsx-pro"));
        Assert.Equal(Category.Other, Cat("neofly-neofly-smokes"));
    }

    [Theory, InlineData("pms50-instrument-gtn750-pro"), InlineData("sky4sim-os"), InlineData("orbx-volanta-aerocaches")]
    public void These_are_aircraft(string name) => Assert.Equal(Category.Aircraft, Cat(name));

    [Fact]
    public void Air_traffic_stays_other() => Assert.Equal(Category.Other, Cat("fsltl-traffic-base"));

    [Fact]
    public void Sal1800_packages_share_one_group()
        => Assert.Equal(2, A.Aircraft.Groups["Cessna-170B-Backcountry"].Providers.Select(p => p.Name).Distinct().Count());

    [Fact]
    public void Mge_c152_is_a_mod_of_the_official_c152()
        => Assert.Equal(AircraftRole.Mod, Role("mge-asobo-aircraft-c152"));

    [Fact]
    public void Packages_without_cfg_attach_to_their_aircraft()
    {
        Assert.Equal(AircraftRole.Attached, Role("flybywire_A320Neo_Cabin"));
        Assert.Equal(AircraftRole.Attached, Role("simflight-C152-interior-navtablet"));
    }

    [Fact]
    public void Sayintentions_template_airport_at_0_0_is_not_an_airport()
    {
        Assert.DoesNotContain(A.AirportLinks, l => l.Code.Value == "ICAO");
        Assert.DoesNotContain(A.Airports.Keys, c => c.Value == "ICAO");
        Assert.Empty(LinksOf("sayintentions-fly-in-library"));
    }

    [Fact]
    public void A_library_aircraft_and_its_external_copy_are_not_mods_of_each_other()
    {
        var copies = Copies("bagolu-c152-neoflybannertow");
        Assert.Equal(2, copies.Count);
        Assert.All(copies, c => Assert.Equal(AircraftRole.Provider, A.Aircraft.RoleOf(c)));
    }
}
