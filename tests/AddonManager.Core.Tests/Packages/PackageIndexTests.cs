using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using static AddonManager.Core.Tests.TestData;

namespace AddonManager.Core.Tests.Packages;

public class PackageIndexTests
{
    [Fact]
    public void DisplayName_falls_back_to_folder_name()
    {
        Assert.Equal("grancanaria", Copy("grancanaria", title: "  ").DisplayName);
        Assert.Equal("Gran Canaria", Copy("grancanaria", title: "Gran Canaria").DisplayName);
    }

    [Fact]
    public void Same_name_in_two_places_is_a_duplicate_copy_and_lookup_ignores_case()
    {
        var index = new PackageIndex([Copy("Neofly-Neofly-Cargo", lib: Neofly), Copy("neofly-neofly-cargo", lib: SimAddons)], [SimAddons, Neofly]);

        Assert.True(index.IsDuplicateCopy(new PackageName("NEOFLY-NEOFLY-CARGO")));
        Assert.Equal(SimAddons, index.CopiesOf(new("neofly-neofly-cargo"))[0].Library);
        Assert.Single(index.Names);
    }

    private static readonly LibraryRoot Outer = new(@"C:\Sim\Addons", [Category.Other]);
    private static readonly LibraryRoot Inner = new(@"C:\Sim\Addons\Scenery", [Category.Scenery]);

    [Fact]
    public void A_copy_seen_through_two_libraries_belongs_to_the_innermost()
    {
        var path = @"C:\Sim\Addons\Scenery\esgg";
        var index = new PackageIndex([Copy("esgg", path, Outer), Copy("esgg", path, Inner)], [Outer, Inner]);

        Assert.Equal(Inner, index.FindByPath(path)!.Library);
        Assert.Single(index.CopiesOf(new("esgg")));
    }

    [Fact]
    public void Sections_of_a_path_are_the_union_over_containing_libraries()
    {
        var index = new PackageIndex([], [Outer, Inner]);

        Assert.Equal([Category.Scenery, Category.Other], index.SectionsOf(@"C:\Sim\Addons\Scenery\esgg"));
        Assert.Equal([Category.Other], index.SectionsOf(@"C:\Sim\Addons\x"));
        Assert.Empty(index.SectionsOf(@"D:\y"));
    }

    [Fact]
    public void LibraryOf_works_for_a_path_that_is_not_indexed()
    {
        var index = new PackageIndex([], [Outer, Inner]);

        Assert.Equal(Inner, index.LibraryOf(@"C:\Sim\Addons\Scenery\gone"));
        Assert.Null(index.LibraryOf(@"D:\y"));
    }

    [Fact]
    public void A_second_copy_with_the_same_path_is_ignored()
    {
        var index = new PackageIndex([Copy("x", @"C:\Sim\Addons\x"), Copy("x", @"c:\sim\addons\X\")], [SimAddons]);

        Assert.False(index.IsDuplicateCopy(new("x")));
    }

    [Fact]
    public void FindByPath_ignores_case_and_trailing_separator()
    {
        var copy = Copy("x");
        var index = new PackageIndex([copy], [SimAddons]);

        Assert.Equal(copy, index.FindByPath(@"c:\sim\addons\X\"));
        Assert.Null(index.FindByPath(@"C:\Sim\Addons\y"));
    }

    [Fact]
    public void An_inaccessible_folder_is_answered_separately_from_an_unreachable_library()
    {
        var unplugged = new LibraryRoot(@"E:\Addons", LibraryRoot.AllSections);
        var index = new PackageIndex([], [SimAddons, unplugged], [unplugged], [@"C:\Sim\Addons\Locked"]);

        Assert.Equal([@"C:\Sim\Addons\Locked"], index.Inaccessible);
        Assert.True(index.IsInaccessible(@"c:\sim\addons\locked\x"));
        Assert.False(index.IsInaccessible(@"C:\Sim\Addons\Open\x"));
        Assert.False(index.IsUnreachable(@"C:\Sim\Addons\Locked\x"));
        Assert.True(index.IsUnreachable(@"E:\Addons\x"));
        Assert.False(index.IsInaccessible(@"E:\Addons\x"));
        Assert.Empty(new PackageIndex([], [SimAddons]).Inaccessible);
    }

    [Fact]
    public void Library_root_equality_uses_the_normalised_path_only()
    {
        Assert.Equal(new LibraryRoot(@"C:\Sim\Addons", LibraryRoot.AllSections), new LibraryRoot(@"c:\sim\addons\", [Category.Aircraft]));
        Assert.Equal(new LibraryRoot(@"C:\Sim\Addons", LibraryRoot.AllSections).GetHashCode(), new LibraryRoot(@"c:\sim\addons\", LibraryRoot.AllSections).GetHashCode());
    }
}
