using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.Packages;

public class PathNormalizerTests
{
    [Theory]
    [InlineData(@"\\?\C:\Sim\Addons\x\", @"C:\Sim\Addons\x")]
    [InlineData(@"\??\C:\Sim\Addons\x", @"C:\Sim\Addons\x")]
    [InlineData(@"C:/Sim/Addons/x/", @"C:\Sim\Addons\x")]
    [InlineData(@"C:\", @"C:\")]
    public void Normalize_strips_prefixes_and_trailing_separator(string input, string expected)
        => Assert.Equal(expected, PathNormalizer.Normalize(input));

    [Fact]
    public void Normalize_maps_unc_and_keeps_volume_guid_paths()
    {
        Assert.Equal(@"\\nas\games\pkg", PathNormalizer.Normalize(@"\??\UNC\nas\games\pkg"));
        Assert.Equal(@"\\?\Volume{0b1c2d3e-0000-0000-0000-000000000000}\pkg",
            PathNormalizer.Normalize(@"\??\Volume{0b1c2d3e-0000-0000-0000-000000000000}\pkg", @"C:\Community"));
    }

    [Fact]
    public void Normalize_resolves_relative_paths_against_base()
        => Assert.Equal(@"C:\lib\pkg", PathNormalizer.Normalize(@"..\lib\pkg", @"C:\Community"));

    [Fact]
    public void AreEqual_ignores_case_and_trailing_separator()
        => Assert.True(PathNormalizer.AreEqual(@"C:\Sim\Addons\X\", @"c:\sim\addons\x"));

    [Fact]
    public void IsUnder_is_case_insensitive_and_segment_aware()
    {
        Assert.True(PathNormalizer.IsUnder(@"C:\Sim\Addons\Scenery\x", @"c:\sim\addons\"));
        Assert.False(PathNormalizer.IsUnder(@"C:\Sim\AddonsOld\x", @"C:\Sim\Addons"));
        Assert.True(PathNormalizer.IsUnder(@"C:\x", @"C:\"));
    }
}
