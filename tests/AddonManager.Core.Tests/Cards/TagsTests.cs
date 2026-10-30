using AddonManager.Core.Cards;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.Cards;

public class TagsTests
{
    private static readonly LibraryRoot SimAddons = new(@"C:\Sim\Addons", LibraryRoot.AllSections);

    [Fact]
    public void Folders_between_root_and_package_are_tags()
    {
        var index = TagIndex.Build(
            [SimAddons],
            [
                @"C:\Sim\Addons\Europe\Sweden\esgg",
                @"C:\Sim\Addons\Europe\Sweden\essa",
                @"C:\Sim\Addons\Europe\eham",
            ]);

        Assert.Equal(["Europe", "Sweden"], index.TagsOf(@"C:\Sim\Addons\Europe\Sweden\esgg"));
        Assert.Equal(["Europe"], index.TagsOf(@"C:\Sim\Addons\Europe\eham"));
    }

    [Fact]
    public void A_folder_holding_one_package_gives_no_tag()
    {
        var index = TagIndex.Build(
            [SimAddons],
            [
                @"C:\Sim\Addons\Europe\Solo\x\pkg",
                @"C:\Sim\Addons\Europe\y",
            ]);

        Assert.Equal(["Europe"], index.TagsOf(@"C:\Sim\Addons\Europe\Solo\x\pkg"));
    }

    [Fact]
    public void Community_and_menu_section_names_give_no_tag()
    {
        var index = TagIndex.Build(
            [SimAddons],
            [
                @"C:\Sim\Addons\Scenery\Community\a",
                @"C:\Sim\Addons\Scenery\Community\b",
                @"C:\Sim\Addons\Aircrafts\c",
                @"C:\Sim\Addons\Aircrafts\d",
            ]);

        Assert.Empty(index.TagsOf(@"C:\Sim\Addons\Scenery\Community\a"));
        Assert.Empty(index.TagsOf(@"C:\Sim\Addons\Scenery\Community\b"));
        Assert.Empty(index.TagsOf(@"C:\Sim\Addons\Aircrafts\c"));
        Assert.Empty(index.TagsOf(@"C:\Sim\Addons\Aircrafts\d"));
    }

    [Fact]
    public void Orbx_layout_collapses_to_nothing()
    {
        var orbx = new LibraryRoot(@"C:\Sim\Orbx Library\msfs", LibraryRoot.AllSections);
        var index = TagIndex.Build(
            [orbx],
            [@"C:\Sim\Orbx Library\msfs\ESGG Gothenburg\Community\orbx-esgg"]);

        Assert.Empty(index.TagsOf(@"C:\Sim\Orbx Library\msfs\ESGG Gothenburg\Community\orbx-esgg"));
    }

    [Fact]
    public void Tags_are_taken_below_the_innermost_library()
    {
        var index = TagIndex.Build(
            [SimAddons, new LibraryRoot(@"C:\Sim\Addons\Scenery", LibraryRoot.AllSections)],
            [
                @"C:\Sim\Addons\Scenery\Europe\a",
                @"C:\Sim\Addons\Scenery\Europe\b",
            ]);

        Assert.Equal(["Europe"], index.TagsOf(@"C:\Sim\Addons\Scenery\Europe\a"));
    }

    [Fact]
    public void A_path_under_no_library_has_no_tags()
    {
        var index = TagIndex.Build(
            [SimAddons],
            [
                @"C:\Sim\Addons\Europe\a",
                @"C:\Sim\Addons\Europe\b",
            ]);

        Assert.Empty(index.TagsOf(@"C:\Elsewhere\Europe\a"));
    }

    [Fact]
    public void Names_compare_case_insensitively()
    {
        var index = TagIndex.Build(
            [SimAddons],
            [
                @"C:\Sim\Addons\SCENERY\community\a",
                @"C:\Sim\Addons\SCENERY\community\b",
            ]);

        Assert.Empty(index.TagsOf(@"C:\Sim\Addons\SCENERY\community\a"));
    }

    [Fact]
    public void Package_missing_from_the_index_still_gets_its_tags()
    {
        var index = TagIndex.Build(
            [SimAddons],
            [
                @"C:\Sim\Addons\Europe\Sweden\esgg",
                @"C:\Sim\Addons\Europe\Sweden\essa",
            ]);

        Assert.Equal(["Europe", "Sweden"], index.TagsOf(@"C:\Sim\Addons\Europe\Sweden\eham"));
    }

    [Fact]
    public void A_missing_package_alone_in_its_folder_gets_no_tag_from_that_folder()
    {
        var index = TagIndex.Build(
            [SimAddons],
            [@"C:\Sim\Addons\Europe\y"]);

        Assert.Equal(["Europe"], index.TagsOf(@"C:\Sim\Addons\Europe\Solo\x\pkg"));
    }
}
