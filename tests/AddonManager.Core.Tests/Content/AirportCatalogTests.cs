using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Content;

public class AirportCatalogTests
{
    private const PackageSource SimManaged = PackageSource.SimManaged;

    private readonly ContentSettings settings = new();

    private static string[] Many(int n) => Enumerable.Range(0, n).Select(i => $"K{i:D3}").ToArray();

    private PackageContent Generic()
        => Scenery(
            "fs-base-genericairports",
            fullRecords: [Rec("EDDF", "Frankfurt Main", 50.03, 8.57), .. Many(10).Select(c => Rec(c, "Generic " + c, 1, 2))],
            source: SimManaged);

    private IReadOnlyDictionary<AirportCode, Airport> Build(params PackageContent[] contents)
        => Build(AirportLinkOverrides.None, contents);

    private IReadOnlyDictionary<AirportCode, Airport> Build(AirportLinkOverrides overrides, params PackageContent[] contents)
        => AirportCatalog.Build(contents, AirportLinker.Link(contents, settings, overrides), settings);

    private IReadOnlyList<BuiltInReplacement> Replaces(params PackageContent[] contents)
        => Replaces(AirportLinkOverrides.None, contents);

    private IReadOnlyList<BuiltInReplacement> Replaces(AirportLinkOverrides overrides, params PackageContent[] contents)
    {
        var links = AirportLinker.Link(contents, settings, overrides);
        return AirportCatalog.ReplacesBuiltIn(contents, links, AirportCatalog.Build(contents, links, settings), settings);
    }

    private static (string, string)[] Pairs(IEnumerable<BuiltInReplacement> r) => r.Select(x => (x.Package.Name.Value, x.Code.Value)).ToArray();

    [Fact]
    public void Encrypted_official_airport_takes_name_and_position_from_the_generic_record()
    {
        var eddf = Scenery("asobo-airport-eddf-frankfurt", hasBgl: false, source: SimManaged);
        var airport = Build(Generic(), eddf)[Code("EDDF")];
        Assert.Equal("Frankfurt Main", airport.Name);
        Assert.Equal(50.03, airport.Position!.Value.Latitude, 2);
        Assert.True(airport.HasRealScenery);
        Assert.True(airport.HasBuiltIn);
    }

    [Fact]
    public void Generic_only_airport_has_no_real_scenery()
        => Assert.False(Build(Generic())[Code("EDDF")].HasRealScenery);

    [Fact]
    public void A_token_link_from_an_excluded_package_is_not_real_scenery()
    {
        var big = Scenery("eddf-library", records: Many(11));
        Assert.False(Build(Generic(), big)[Code("EDDF")].HasRealScenery);
    }

    [Fact]
    public void Pattern_excluded_records_create_no_airports()
        => Assert.False(Build(Scenery("microsoft-airport-voloport", records: ["VOLC1"], source: SimManaged)).ContainsKey(Code("VOLC1")));

    [Fact]
    public void Linked_record_name_wins_over_generic()
    {
        var addon = Scenery("addon-eddf-frankfurt", fullRecords: [Rec("EDDF", "Frankfurt Addon", 50.1, 8.6)]);
        var airport = Build(Generic(), addon)[Code("EDDF")];
        Assert.Equal("Frankfurt Addon", airport.Name);
        Assert.Equal(50.1, airport.Position!.Value.Latitude, 2);
    }

    [Fact]
    public void Position_is_searched_independently_of_name()
    {
        var addon = Scenery("addon-eddf-frankfurt", fullRecords: [Rec("EDDF", null, 50.1, 8.6)]);
        var airport = Build(Generic(), addon)[Code("EDDF")];
        Assert.Equal("Frankfurt Main", airport.Name);
        Assert.Equal(50.1, airport.Position!.Value.Latitude, 2);
    }

    [Fact]
    public void Localisation_key_is_not_a_name()
    {
        var official = Scenery("asobo-airport-eham", fullRecords: [Rec("EHAM", "TT:AIRPORTEU.EHAM.name", 52.31, 4.76)], source: SimManaged);
        var addon = Scenery("brickbybrickdesign-airport-eham-amsterdam", fullRecords: [Rec("EHAM", "Amsterdam Schiphol", 52.31, 4.76)]);
        Assert.Equal("Amsterdam Schiphol", Build(official, addon)[Code("EHAM")].Name);
        Assert.Null(Build(official)[Code("EHAM")].Name);
    }

    [Fact]
    public void World_update_partner_airport_is_built_in_because_it_is_sim_managed()
        => Assert.True(Build(Scenery("microsoft-airport-edny-friedrichshafen", records: ["EDNY"], creator: "Partner", source: SimManaged))[Code("EDNY")].HasBuiltIn);

    [Fact]
    public void Library_package_at_a_built_in_airport_replaces_built_in()
    {
        var eddf = Scenery("asobo-airport-eddf-frankfurt", hasBgl: false, source: SimManaged);
        var lib = Scenery("lib-eddf", records: ["EDDF"]);
        Assert.Equal([("lib-eddf", "EDDF")], Pairs(Replaces(Generic(), eddf, lib)));
    }

    [Fact]
    public void Dependency_on_the_official_package_is_an_extra_signal()
    {
        var eddf = Scenery("asobo-airport-eddf-frankfurt", hasBgl: false, source: SimManaged);
        var patch = Scenery("frankfurt-fix", hasBgl: true, dependencies: ["asobo-airport-eddf-frankfurt"]);
        Assert.Equal([("frankfurt-fix", "EDDF")], Pairs(Replaces(Generic(), eddf, patch)));
    }

    [Fact]
    public void Dependency_on_an_unlinked_sim_managed_package_gives_nothing()
    {
        var other = Scenery("asobo-misc-thing", hasBgl: false, source: SimManaged);
        var patch = Scenery("frankfurt-fix", hasBgl: true, dependencies: ["asobo-misc-thing"]);
        Assert.Empty(Replaces(Generic(), other, patch));
    }

    [Fact]
    public void Pattern_excluded_records_supply_no_name_or_position()
    {
        // The only EDDF record belongs to a pattern-excluded Package; a Manual link makes the Airport exist anyway.
        var wrong = Scenery("microsoft-airport-voloport", fullRecords: [Rec("EDDF", "Wrong", 1, 1)], source: SimManaged);
        var airport = Build(Overrides(manual: [("microsoft-airport-voloport", "EDDF")]), wrong)[Code("EDDF")];
        Assert.Null(airport.Name);
        Assert.Null(airport.Position);
    }

    [Fact]
    public void Manual_link_from_an_excluded_package_is_real_scenery()
    {
        var big = Scenery("eddf-library", records: Many(11));
        Assert.True(Build(Overrides(manual: [("eddf-library", "EDDF")]), Generic(), big)[Code("EDDF")].HasRealScenery);
    }

    [Fact]
    public void Manual_link_from_an_excluded_package_replaces_built_in()
    {
        var eddf = Scenery("asobo-airport-eddf-frankfurt", hasBgl: false, source: SimManaged);
        var big = Scenery("eddf-library", records: Many(11));
        Assert.Equal([("eddf-library", "EDDF")], Pairs(Replaces(Overrides(manual: [("eddf-library", "EDDF")]), Generic(), eddf, big)));
    }

    [Fact]
    public void Excluded_library_package_replaces_nothing()
    {
        var eddf = Scenery("asobo-airport-eddf-frankfurt", hasBgl: false, source: SimManaged);
        var big = Scenery("eddf-library", records: Many(11));
        Assert.Empty(Replaces(Generic(), eddf, big));
    }

    [Fact]
    public void Two_library_packages_without_official_scenery_replace_nothing()
    {
        var a = Scenery("lib-eham-a", records: ["EHAM"]);
        var b = Scenery("lib-eham-b", records: ["EHAM"]);
        Assert.Empty(Replaces(a, b));
    }
}
