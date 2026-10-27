using AddonManager.Core.Content;
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
    public void Unmatched_aircraft_package_without_cfg_is_other()
    {
        var matched = Aircraft("matched-aircraft", cfgs: [("c172", null)]);
        var unmatched = Aircraft("unmatched-aircraft");
        var a = ContentAnalyzer.Analyze([matched, unmatched], new());

        Assert.Equal(Category.Aircraft, a.Categories[matched.Path]);
        Assert.Equal(Category.Other, a.Categories[unmatched.Path]);
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
}
