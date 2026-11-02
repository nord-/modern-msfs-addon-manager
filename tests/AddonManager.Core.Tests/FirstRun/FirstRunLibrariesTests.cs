using AddonManager.Core.Content;
using AddonManager.Core.FirstRun;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.FirstRun;

public class FirstRunLibrariesTests
{
    private const string Packages = @"C:\Sim\MSFS\Packages";
    private const string Community = @"C:\Sim\MSFS\Packages\Community";
    private const string CommunityRefusal = "This folder is, holds or lies inside the sim's Community folder.";
    private const string OfficialRefusal = "This folder is, holds or lies inside the sim's Official folder.";

    [Fact]
    public void The_community_folder_is_refused()
        => Assert.Equal(CommunityRefusal, FirstRunLibraries.Refusal(Community, Community, Packages));

    [Fact]
    public void A_folder_inside_community_is_refused()
        => Assert.Equal(CommunityRefusal, FirstRunLibraries.Refusal(Community + @"\MyAddon", Community, Packages));

    [Fact]
    public void A_folder_holding_community_is_refused()
    {
        Assert.Equal(CommunityRefusal, FirstRunLibraries.Refusal(@"C:\Sim", Community, Packages));
        Assert.Equal(CommunityRefusal, FirstRunLibraries.Refusal(Packages, Community, Packages));
    }

    [Fact]
    public void The_official_folder_itself_is_refused()
        => Assert.Equal(OfficialRefusal, FirstRunLibraries.Refusal(Packages + @"\Official", Community, Packages));

    [Fact]
    public void A_store_folder_under_official_is_refused()
        => Assert.Equal(OfficialRefusal, FirstRunLibraries.Refusal(Packages + @"\Official\OneStore", Community, Packages));

    [Fact]
    public void A_sibling_folder_is_allowed()
        => Assert.Null(FirstRunLibraries.Refusal(@"C:\Sim\Addons", Community, Packages));

    [Fact]
    public void One_folder_for_two_sections_is_one_library()
    {
        var libraries = FirstRunLibraries.Combine(
        [
            (@"C:\Sim\Addons", Category.Aircraft),
            (@"C:\Sim\Addons", Category.Scenery),
        ]);

        var library = Assert.Single(libraries);
        Assert.Equal([Category.Scenery, Category.Aircraft], library.Sections);
    }

    [Fact]
    public void Libraries_keep_the_order_they_were_first_added()
    {
        var libraries = FirstRunLibraries.Combine(
        [
            (@"C:\Sim\Second", Category.Other),
            (@"C:\Sim\First", Category.Aircraft),
            (@"C:\Sim\Second", Category.Aircraft),
        ]);

        Assert.Equal([@"C:\Sim\Second", @"C:\Sim\First"], libraries.Select(l => l.Path));
        Assert.Equal([Category.Aircraft, Category.Other], libraries[0].Sections);
    }

    [Fact]
    public void A_trailing_backslash_is_the_same_folder()
    {
        var libraries = FirstRunLibraries.Combine(
        [
            (@"C:\Sim\Addons\", Category.Aircraft),
            (@"C:\Sim\Addons", Category.Scenery),
        ]);

        var library = Assert.Single(libraries);
        Assert.Equal([Category.Scenery, Category.Aircraft], library.Sections);
    }
}
