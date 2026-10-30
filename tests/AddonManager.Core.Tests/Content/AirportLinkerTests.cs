using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Content;

public class AirportLinkerTests
{
    private readonly ContentSettings settings = new();

    private IReadOnlyList<AirportLink> Link(params PackageContent[] contents) => AirportLinker.Link(contents, settings, AirportLinkOverrides.None);

    private IReadOnlyList<AirportLink> Link(AirportLinkOverrides overrides, params PackageContent[] contents) => AirportLinker.Link(contents, settings, overrides);

    private static string[] Codes(IEnumerable<AirportLink> links) => links.Select(l => l.Code.Value).ToArray();

    private static (string, AirportLinkReason)[] Pairs(IEnumerable<AirportLink> links) => links.Select(l => (l.Code.Value, l.Reason)).ToArray();

    private static string[] Many(int n) => Enumerable.Range(0, n).Select(i => $"K{i:D3}").ToArray();

    [Fact]
    public void Records_link_their_codes()
        => Assert.Equal(["EHAM"], Codes(Link(Scenery("brickbybrickdesign-airport-eham-amsterdam", records: ["EHAM"]))));

    [Fact]
    public void More_than_threshold_codes_exclude_the_package_unless_opted_in()
    {
        var nav = Scenery("fs-base-nav", records: Many(11), source: PackageSource.SimManaged);
        Assert.Empty(Link(nav));
        settings.OptedIn.Add(nav.Name);
        Assert.Equal(11, Link(nav).Count);
    }

    [Fact]
    public void Exactly_threshold_codes_are_counted()
        => Assert.Equal(10, Link(Scenery("some-airports", records: Many(10))).Count);

    [Fact]
    public void Voloport_and_neofly_records_are_excluded()
        => Assert.Empty(Link(Scenery("microsoft-airport-voloport", records: ["VOLC1", "VOLC2"]), Scenery("neofly-neofly-cargo", records: ["NEOF2"])));

    [Fact]
    public void Opted_in_pattern_package_is_linked()
    {
        var volo = Scenery("microsoft-airport-voloport", records: ["VOLC1"]);
        settings.OptedIn.Add(volo.Name);
        Assert.False(AirportLinker.IsExcluded(volo, settings));
        Assert.False(AirportLinker.IsExcludedByPattern(volo, settings));
        Assert.Equal(["VOLC1"], Codes(Link(volo)));
    }

    [Theory]
    [InlineData("u60-u60", null, "U60")]
    [InlineData("my-u60-scenery", null, null)]
    [InlineData("aerosoft-scenery-esgj", null, "ESGJ")]
    [InlineData("x-pack", "EHAM Pack", "EHAM")]
    [InlineData("ldsp-split", null, "LDSP")]
    public void Tokens_link_only_known_codes(string name, string? title, string? expected)
    {
        var known = Scenery("fs-base-genericairports", records: ["U60", "ESGJ", "EHAM", "LDSP", "PACK", "MY0"], source: PackageSource.SimManaged);
        var links = Link(known, Scenery(name, title: title, hasBgl: true)).Where(l => l.Package.Name.Value == name);
        Assert.Equal(expected is null ? [] : [expected], Codes(links));
    }

    [Fact]
    public void Token_rule_skips_packages_with_a_counted_record_and_non_scenery()
    {
        var known = Scenery("fs-base-genericairports", records: [.. Many(11), "ESGJ", "LDSP"], source: PackageSource.SimManaged);
        var counted = Scenery("esgj-scenery", records: ["ABCD"]);
        var noBgl = Scenery("ldsp-gsx", hasBgl: false);
        var traffic = Scenery("ldsp-traffic", isAirTraffic: true);
        var aircraft = Build("ldsp-plane", "AIRCRAFT", hasBgl: true);
        Assert.Equal([("ABCD", AirportLinkReason.Record)], Pairs(Link(known, counted, noBgl, traffic, aircraft)));
    }

    [Fact]
    public void Token_rule_skips_sim_managed_packages()
    {
        var known = Scenery("fs-base-genericairports", records: [.. Many(11), "ESGJ"], source: PackageSource.SimManaged);
        var landmark = Scenery("asobo-landmarks-esgj", source: PackageSource.SimManaged);
        Assert.Empty(Link(known, landmark));
    }

    [Fact]
    public void Token_rule_applies_when_the_only_records_are_excluded_by_pattern()
    {
        var neofly = Scenery("neofly-x", records: ["ESGJ"]);
        var known = Scenery("fs-base-genericairports", records: [.. Many(11), "EHAM"], source: PackageSource.SimManaged);
        var target = Scenery("eham-thing");
        Assert.Equal([("EHAM", AirportLinkReason.Token)], Pairs(Link(neofly, known, target)));
    }

    [Fact]
    public void Encrypted_official_airport_links_by_package_name()
    {
        var generic = Scenery("fs-base-genericairports", records: [.. Many(11), "EDDF", "LFCS"], source: PackageSource.SimManaged);
        var eddf = Scenery("asobo-airport-eddf-frankfurt", hasBgl: false, source: PackageSource.SimManaged);
        var lfcs = Scenery("asobo-airport-lfcs", hasBgl: true, source: PackageSource.SimManaged);
        var fake = Scenery("x-airport-pack-y", hasBgl: false, source: PackageSource.SimManaged);
        Assert.Equal([("EDDF", AirportLinkReason.PackageName), ("LFCS", AirportLinkReason.PackageName)], Pairs(Link(generic, eddf, lfcs, fake)));
    }

    [Fact]
    public void Pattern_excluded_records_are_not_known_codes()
    {
        var volo = Scenery("microsoft-airport-voloport", records: ["VOLC1"], source: PackageSource.SimManaged);
        Assert.DoesNotContain(Code("VOLC1"), AirportLinker.KnownCodes([volo], settings));
        Assert.Empty(Link(volo, Scenery("volc1-vertiport", hasBgl: true)));
    }

    [Fact]
    public void A_record_at_exactly_0_0_is_a_template_and_not_an_airport()
    {
        var template = Scenery("sayintentions-fly-in-library", fullRecords: [Rec("ICAO", "name", 0, 0)]);
        var tokenBait = Scenery("icao-some-scenery");
        var contents = new[] { template, tokenBait };

        Assert.Empty(Link(template, tokenBait));
        Assert.DoesNotContain(Code("ICAO"), AirportLinker.KnownCodes(contents, settings));
        Assert.DoesNotContain(Code("ICAO"), AirportCatalog.Build(contents, Link(contents), settings).Keys);
    }

    [Fact]
    public void A_record_at_zero_latitude_but_a_real_longitude_still_counts()
    {
        var island = Scenery("gulf-airport", fullRecords: [Rec("GULF", "Gulf", 0, 0.5)]);

        Assert.Equal(["GULF"], Codes(Link(island)));
        Assert.Contains(Code("GULF"), AirportLinker.KnownCodes([island], settings));
    }

    [Fact]
    public void Threshold_excluded_records_are_known_codes()
        => Assert.Contains(Code("K001"), AirportLinker.KnownCodes([Scenery("fs-base-nav", records: Many(11))], settings));

    [Fact]
    public void Manual_links_win_over_removal_and_removal_drops_automatic_links()
    {
        var a = Scenery("a-scenery", records: ["EHAM", "ESGJ"]);
        Assert.Equal(["ESGJ"], Codes(Link(Overrides(removed: [("a-scenery", "EHAM")]), a)));

        Assert.Equal([("EHAM", AirportLinkReason.Manual), ("ESGJ", AirportLinkReason.Record)], Pairs(Link(Overrides(manual: [("a-scenery", "EHAM")]), a)));

        // A removal next to a manual link for the same ICAO code still lets the manual link through.
        var both = Overrides(manual: [("a-scenery", "EHAM")], removed: [("a-scenery", "EHAM")]);
        Assert.Equal([("EHAM", AirportLinkReason.Manual), ("ESGJ", AirportLinkReason.Record)], Pairs(Link(both, a)));
    }

    [Fact]
    public void Removal_applies_to_sim_managed_packages()
    {
        var eddf = Scenery("asobo-airport-eddf-frankfurt", hasBgl: false, source: PackageSource.SimManaged);
        var generic = Scenery("fs-base-genericairports", records: [.. Many(11), "EDDF"], source: PackageSource.SimManaged);
        Assert.Equal(["EDDF"], Codes(Link(generic, eddf)));
        Assert.Empty(Link(Overrides(removed: [("asobo-airport-eddf-frankfurt", "EDDF")]), generic, eddf));
    }

    [Fact]
    public void Manual_links_apply_to_sim_managed_packages()
    {
        var mkt = Scenery("somedev-lowi-innsbruck", hasBgl: true, source: PackageSource.SimManaged);
        Assert.Equal([("LOWI", AirportLinkReason.Manual)], Pairs(Link(Overrides(manual: [("somedev-lowi-innsbruck", "LOWI")]), mkt)));
    }

    [Fact]
    public void Manual_links_apply_to_every_copy_with_that_name()
    {
        var one = Scenery("dup") with { Path = @"C:\A\dup" };
        var two = Scenery("dup") with { Path = @"C:\B\dup" };
        Assert.Equal([@"C:\A\dup", @"C:\B\dup"], Link(Overrides(manual: [("dup", "EHAM")]), two, one).Select(l => l.Package.Path).ToArray());
    }

    [Fact]
    public void Result_does_not_depend_on_input_order()
    {
        var x = Scenery("x-scenery", records: ["EHAM", "ESGJ"]);
        var y = Scenery("fs-base-genericairports", records: [.. Many(11), "LDSP"], source: PackageSource.SimManaged);
        var z = Scenery("ldsp-split");
        Assert.Equal(3, Link(x, y, z).Count);
        Assert.Equal(Link(x, y, z), Link(z, y, x));
    }

    [Fact]
    public void Results_are_sorted_by_name_path_then_code()
    {
        var b = Scenery("b-scenery", records: ["ZZZZ", "AAAA"]);
        var a = Scenery("A-scenery", records: ["MMMM"]);
        Assert.Equal(
            [("A-scenery", "MMMM"), ("b-scenery", "AAAA"), ("b-scenery", "ZZZZ")],
            Link(b, a).Select(l => (l.Package.Name.Value, l.Code.Value)).ToArray());
    }

    [Fact]
    public void Real_cases_without_record_or_token_stay_unlinked()
        => Assert.Empty(Link(Scenery("005builds-lszrsceneryv11", hasBgl: true), Scenery("grancanaria", hasBgl: true)));

    [Fact]
    public void Link_results_are_unchanged()
    {
        var c = Scenery("sltcreations-airport-yq05-mount-mulligan", records: ["YQ05", "YMUZ"]);
        Assert.Equal([("YMUZ", AirportLinkReason.Record), ("YQ05", AirportLinkReason.Record)], Pairs(Link(c)));
    }

    [Fact]
    public void Token_candidates_report_why_a_known_code_is_not_linked()
    {
        var known = new HashSet<AirportCode> { Code("PACK"), Code("U60"), Code("EHAM") };
        var c = Scenery("my-u60-pack", title: "EHAM Pack");
        var result = AirportLinker.TokenCandidates(c, settings, known).Select(t => (t.Source, t.Code.Value, t.Linked, t.Reason)).ToArray();
        Assert.Equal(
            [("name", "U60", false, "not leading"), ("name", "PACK", false, "stop word"), ("title", "EHAM", true, (string?)null), ("title", "PACK", false, "stop word")],
            result);

        Assert.All(AirportLinker.TokenCandidates(Scenery("eham-x", source: PackageSource.SimManaged), settings, known), t => Assert.Equal("sim-managed", t.Reason));
        Assert.All(AirportLinker.TokenCandidates(Scenery("eham-x", records: ["ABCD"]), settings, known), t => Assert.Equal("counted record", t.Reason));
        Assert.All(AirportLinker.TokenCandidates(Scenery("eham-x", hasBgl: false), settings, known), t => Assert.Equal("not scenery", t.Reason));
        Assert.All(AirportLinker.TokenCandidates(Scenery("eham-x", isAirTraffic: true), settings, known), t => Assert.Equal("air traffic", t.Reason));
    }
}
