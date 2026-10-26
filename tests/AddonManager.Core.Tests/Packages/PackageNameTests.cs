using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.Packages;

public class PackageNameTests
{
    [Fact]
    public void Equality_ignores_case()
    {
        Assert.Equal(new PackageName("Asobo_C152"), new PackageName("asobo_c152"));
        Assert.True(new PackageName("Asobo_C152") == new PackageName("ASOBO_C152"));
        Assert.Equal(new PackageName("A").GetHashCode(), new PackageName("a").GetHashCode());
    }

    [Fact]
    public void ToString_keeps_the_original_spelling()
        => Assert.Equal("Asobo_C152", new PackageName("Asobo_C152").ToString());
}
