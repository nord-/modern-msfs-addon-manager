using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Content;

public class ContentAnalyzerTests
{
    private static PackageContent AirTraffic(string name) => Scenery(name, isAirTraffic: true);

    [Fact]
    public void Gsx_pro_without_bgl_is_other_and_air_traffic_is_other()
    {
        var a = ContentAnalyzer.Analyze([Scenery("fsdreamteam-gsx-pro", hasBgl: false), AirTraffic("fsltl-traffic-base")], new());

        Assert.Equal(2, a.Categories.Count);
        Assert.All(a.Categories.Values, c => Assert.Equal(Category.Other, c));
    }

    [Fact]
    public void Scenery_with_bgl_is_scenery_and_categories_are_keyed_case_insensitively()
    {
        var scenery = Scenery("some-scenery");
        var a = ContentAnalyzer.Analyze([scenery], new());

        Assert.Equal(Category.Scenery, a.Categories[scenery.Path.ToUpperInvariant()]);
    }

    [Fact]
    public void Unmatched_aircraft_package_without_cfg_stays_aircraft()
    {
        var matched = Aircraft("matched-aircraft", cfgs: [("c172", null)]);
        var unmatched = Aircraft("unmatched-aircraft");
        var a = ContentAnalyzer.Analyze([matched, unmatched], new());

        Assert.Equal(Category.Aircraft, a.Categories[matched.Path]);
        Assert.Equal(Category.Aircraft, a.Categories[unmatched.Path]);
    }

    [Fact]
    public void Excluded_lists_packages_over_the_threshold_with_records()
    {
        var settings = new ContentSettings { AirportThreshold = 2 };
        var many = Scenery("b-many", ["AAA", "BBB", "CCC"]);
        var pattern = Scenery("neofly-pois", ["DDD"]);
        var patternNoRecords = Scenery("neofly-empty");
        var few = Scenery("few", ["EEE", "FFF"]);
        var optedIn = Scenery("opted-in", ["GGG", "HHH", "III"]);
        settings.OptedIn.Add(optedIn.Name);

        var a = ContentAnalyzer.Analyze([pattern, many, patternNoRecords, few, optedIn], settings);

        Assert.Equal(["b-many", "neofly-pois"], a.Excluded.Select(c => c.Name.Value));
    }

    [Fact]
    public void Analysis_wires_links_airports_and_aircraft_together()
    {
        var scenery = Scenery("eham-schiphol", ["EHAM"]);
        var aircraft = Aircraft("some-plane", cfgs: [("c172", null)]);
        var a = ContentAnalyzer.Analyze([aircraft, scenery], new());

        Assert.True(a.Airports[Code("EHAM")].HasRealScenery);
        Assert.Contains(a.AirportLinks, l => l.Package == scenery && l.Code == Code("EHAM"));
        Assert.Equal(AircraftRole.Provider, a.Aircraft.RoleOf(aircraft));
        Assert.Empty(a.Excluded);
    }

    [Fact]
    public void A_remembered_record_names_an_airport_no_content_knows()
    {
        var a = ContentAnalyzer.Analyze([], new(), null, [Rec("YQ05", "Mount Mulligan", -16.8, 144.8)]);

        Assert.Equal("Mount Mulligan", a.Airports[Code("YQ05")].Name);
        Assert.Equal(new GeoPoint(-16.8, 144.8), a.Airports[Code("YQ05")].Position);
        Assert.False(a.Airports[Code("YQ05")].HasRealScenery);
    }

    [Fact]
    public void A_remembered_record_does_not_replace_the_name_a_content_record_gives()
    {
        var a = ContentAnalyzer.Analyze([Scenery("eham-x", fullRecords: [Rec("EHAM", "Schiphol", 52.3, 4.7)])], new(), null, [Rec("EHAM", "Old name", 1, 1)]);

        Assert.Equal("Schiphol", a.Airports[Code("EHAM")].Name);
        Assert.Equal(new GeoPoint(52.3, 4.7), a.Airports[Code("EHAM")].Position);
    }

    [Fact]
    public void A_remembered_record_fills_in_the_name_and_position_a_content_record_lacks()
    {
        var bare = Scenery("eham-bare", records: ["EHAM"]);
        var nameOnly = Scenery("ehrd-name", fullRecords: [Rec("EHRD", "Rotterdam")]);
        var excluded = Scenery("microsoft-airport-voloport", fullRecords: [Rec("EHLE", "Wrong", 1, 1)], source: PackageSource.SimManaged);

        var a = ContentAnalyzer.Analyze(
            [bare, nameOnly, excluded],
            new(),
            null,
            [Rec("EHAM", "Schiphol", 52.3, 4.7), Rec("EHRD", "Old name", 51.9, 4.4), Rec("EHLE", "Lelystad", 52.5, 5.5)]);

        Assert.Equal("Schiphol", a.Airports[Code("EHAM")].Name);
        Assert.Equal(new GeoPoint(52.3, 4.7), a.Airports[Code("EHAM")].Position);
        Assert.Equal("Rotterdam", a.Airports[Code("EHRD")].Name);
        Assert.Equal(new GeoPoint(51.9, 4.4), a.Airports[Code("EHRD")].Position);
        Assert.Equal("Lelystad", a.Airports[Code("EHLE")].Name);
    }

    [Fact]
    public void A_remembered_icao_code_counts_as_known_for_the_name_token_rule()
    {
        var a = ContentAnalyzer.Analyze([Scenery("yq05-photo", hasBgl: true)], new(), null, [Rec("YQ05", "Mount Mulligan", -16.8, 144.8)]);

        var link = Assert.Single(a.AirportLinks);
        Assert.Equal((Code("YQ05"), AirportLinkReason.Token), (link.Code, link.Reason));
    }
}
