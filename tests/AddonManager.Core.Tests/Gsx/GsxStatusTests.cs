using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;
using AddonManager.Core.Tests.Cards;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Gsx;

public sealed class GsxStatusTests
{
    private static World NewWorld()
    {
        var w = new World();
        w.Library(@"C:\Sim\Addons");
        return w;
    }

    private static IReadOnlyList<PackageSummary> Profiles(World w) => ProfileCatalog.Build(w.State, w.Input());

    private static GsxAirport At(string code, IReadOnlyList<PackageSummary> profiles, IReadOnlyList<GsxScript>? scripts = null, IReadOnlyList<ShippedProfile>? shipped = null)
        => GsxStatus.For(Code(code), profiles, scripts ?? [], shipped ?? []);

    [Fact]
    public void One_active_profile_is_installed()
    {
        var w = NewWorld();
        w.Profile("eham", "EHAM");

        var airport = At("EHAM", Profiles(w));

        Assert.Equal(GsxMark.Installed, airport.Status);
        Assert.Equal([new PackageName("eham")], airport.Active);
    }

    [Fact]
    public void No_profile_is_missing()
    {
        var w = NewWorld();
        w.Profile("lppt", "LPPT");

        var airport = At("EHAM", Profiles(w));

        Assert.Equal(GsxMark.Missing, airport.Status);
        Assert.Empty(airport.Active);
    }

    [Fact]
    public void Two_active_profiles_are_two_profiles()
    {
        var w = NewWorld();
        w.Profile("ldsp-b", "LDSP");
        w.Profile("ldsp-a", "LDSP");

        var airport = At("LDSP", Profiles(w));

        Assert.Equal(GsxMark.TwoProfiles, airport.Status);
        Assert.Equal([new PackageName("ldsp-a"), new PackageName("ldsp-b")], airport.Active);
    }

    [Fact]
    public void A_disabled_profile_does_not_count()
    {
        var w = NewWorld();
        w.Profile("lpma", "LPMA", disabled: true);

        var airport = At("LPMA", Profiles(w));

        Assert.Equal(GsxMark.Missing, airport.Status);
        Assert.Equal([@"C:\GSX\lpma.off"], airport.Disabled);
    }

    [Fact]
    public void An_off_variant_is_listed_as_disabled_and_does_not_count()
    {
        var w = NewWorld();
        w.Profile("lpma", "LPMA", offVariant: true);

        var airport = At("LPMA", Profiles(w));

        Assert.Equal(GsxMark.Installed, airport.Status);
        Assert.Equal([@"C:\GSX\lpma.off"], airport.Disabled);
    }

    [Fact]
    public void A_not_found_profile_does_not_count()
    {
        var w = NewWorld();
        w.Profile("lpma", "LPMA");
        w.Place();
        w.WithoutProfile("lpma");

        var profiles = Profiles(w);
        var airport = At("LPMA", profiles);

        Assert.Equal(Presence.NotFound, Assert.Single(profiles).Presence);
        Assert.Equal(GsxMark.Missing, airport.Status);
        Assert.Empty(airport.Active);
    }

    [Fact]
    public void Scripts_alone_are_missing()
    {
        var airport = At("ESSA", [], [new GsxScript(Code("ESSA"), @"C:\GSX\essa-orbx.py"), new GsxScript(Code("ESGG"), @"C:\GSX\esgg.py")]);

        Assert.Equal(GsxMark.Missing, airport.Status);
        Assert.Equal([@"C:\GSX\essa-orbx.py"], airport.Scripts);
    }

    [Fact]
    public void A_shipped_profile_does_not_count()
    {
        var shipped = new ShippedProfile(Code("EPKK"), new PackageName("epkk-pkg"), @"C:\Sim\Addons\epkk-pkg\GSX\epkk-x.ini", [], false);

        var airport = At("EPKK", [], shipped: [shipped, shipped with { Code = Code("EPKR") }]);

        Assert.Equal(GsxMark.Missing, airport.Status);
        Assert.Equal([shipped], airport.Shipped);
    }

    [Fact]
    public void Search_link_uses_the_lower_case_icao_code()
    {
        Assert.Equal("https://flightsim.to/others/gsx-pro/search/eham", GsxStatus.SearchLink(Code("EHAM")));
        Assert.Equal("https://flightsim.to/others/gsx-pro/search/eham", At("EHAM", []).SearchLink);
    }
}
