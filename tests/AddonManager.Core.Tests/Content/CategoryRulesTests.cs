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
    [InlineData("MISC", null, Category.Other)]
    [InlineData(null, null, Category.Other)]
    public void Provisional(string? type, bool? hasBgl, Category expected) => Assert.Equal(expected, CategoryRules.Provisional(type, hasBgl));

    [Theory]
    [InlineData("AIRCRAFT", false, true, true, Category.Other)] // fsltl-traffic-base: isAirTraffic=1
    [InlineData("AIRCRAFT", false, false, false, Category.Other)] // no aircraft.cfg, matched to nothing
    [InlineData("AIRCRAFT", false, false, true, Category.Aircraft)]
    [InlineData("SCENERY", false, false, false, Category.Other)]
    [InlineData("SCENERY", true, false, false, Category.Scenery)]
    [InlineData("SCENERY", true, true, false, Category.Other)] // scenery that ships AI aircraft
    public void Final(string type, bool hasBgl, bool airTraffic, bool member, Category expected)
        => Assert.Equal(expected, CategoryRules.Final(type, hasBgl, airTraffic, member));

    [Theory]
    [InlineData("AIRCRAFT", true)]
    [InlineData("livery", true)]
    [InlineData("SCENERY", false)]
    [InlineData(null, false)]
    public void IsAircraftType(string? type, bool expected) => Assert.Equal(expected, CategoryRules.IsAircraftType(type));
}
