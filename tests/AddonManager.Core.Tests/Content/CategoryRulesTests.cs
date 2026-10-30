using AddonManager.Core.Content;

namespace AddonManager.Core.Tests.Content;

public class CategoryRulesTests
{
    [Theory]
    [InlineData("SCENERY", null, Category.Scenery)]
    [InlineData("SCENERY", true, Category.Scenery)]
    [InlineData("SCENERY", false, Category.Other)] // fsdreamteam-gsx-pro, neofly-neofly-smokes
    [InlineData("aircraft", null, Category.Aircraft)]
    [InlineData("LIVERY", null, Category.Aircraft)]
    [InlineData("INSTRUMENTS", null, Category.Aircraft)]
    [InlineData("MISC", null, Category.Other)]
    [InlineData(null, null, Category.Other)]
    public void Provisional(string? type, bool? hasBgl, Category expected) => Assert.Equal(expected, CategoryRules.Provisional(type, hasBgl));

    [Theory]
    [InlineData("AIRCRAFT", false, true, Category.Other)] // fsltl-traffic-base: isAirTraffic=1
    [InlineData("AIRCRAFT", false, false, Category.Aircraft)] // no aircraft.cfg, matched to nothing: stays Aircraft
    [InlineData("INSTRUMENTS", false, false, Category.Aircraft)]
    [InlineData("SCENERY", false, false, Category.Other)]
    [InlineData("SCENERY", true, false, Category.Scenery)]
    [InlineData("SCENERY", true, true, Category.Other)] // scenery that ships AI aircraft
    public void Final(string type, bool hasBgl, bool airTraffic, Category expected)
        => Assert.Equal(expected, CategoryRules.Final(type, hasBgl, airTraffic));

    [Fact]
    public void Instruments_are_aircraft()
        => Assert.Equal(Category.Aircraft, CategoryRules.Final("INSTRUMENTS", hasBgl: false, isAirTraffic: false));

    [Fact]
    public void An_aircraft_package_without_an_aircraft_stays_aircraft()
        => Assert.Equal(Category.Aircraft, CategoryRules.Final("AIRCRAFT", false, false));

    [Theory]
    [InlineData("INSTRUMENTS", true)]
    [InlineData("instruments", true)]
    [InlineData("AIRCRAFT", false)]
    [InlineData(null, false)]
    public void IsInstruments(string? type, bool expected) => Assert.Equal(expected, CategoryRules.IsInstruments(type));

    [Theory]
    [InlineData("AIRCRAFT", true)]
    [InlineData("livery", true)]
    [InlineData("SCENERY", false)]
    [InlineData(null, false)]
    public void IsAircraftType(string? type, bool expected) => Assert.Equal(expected, CategoryRules.IsAircraftType(type));
}
