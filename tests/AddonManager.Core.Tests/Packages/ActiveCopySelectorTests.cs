using AddonManager.Core.Packages;
using static AddonManager.Core.Tests.TestData;

namespace AddonManager.Core.Tests.Packages;

public class ActiveCopySelectorTests
{
    [Fact]
    public void Active_copy_prefers_user_choice_then_community_then_library_order()
    {
        var a = Copy("x", lib: SimAddons);
        var b = Copy("x", lib: Neofly);
        var index = new PackageIndex([b, a], [SimAddons, Neofly]);

        Assert.Equal(b, ActiveCopySelector.Select(new("x"), index, communityTarget: a.Path, chosenPath: b.Path));
        Assert.Equal(b, ActiveCopySelector.Select(new("x"), index, communityTarget: b.Path + @"\", chosenPath: null));
        Assert.Equal(a, ActiveCopySelector.Select(new("x"), index, null, null));
    }

    [Fact]
    public void Paths_that_match_no_copy_are_ignored()
    {
        var a = Copy("x");
        var index = new PackageIndex([a], [SimAddons]);

        Assert.Equal(a, ActiveCopySelector.Select(new("x"), index, @"C:\gone\x", @"C:\other\x"));
    }

    [Fact]
    public void Unknown_package_has_no_active_copy()
        => Assert.Null(ActiveCopySelector.Select(new("x"), new PackageIndex([], [SimAddons]), null, null));
}
