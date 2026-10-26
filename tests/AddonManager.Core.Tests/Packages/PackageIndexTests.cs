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
        var index = new PackageIndex([Copy("Neofly-Neofly-Cargo", lib: Neofly), Copy("neofly-neofly-cargo", lib: Addons)], [Addons, Neofly]);

        Assert.True(index.IsDuplicateCopy(new PackageName("NEOFLY-NEOFLY-CARGO")));
        Assert.Equal(Addons, index.CopiesOf(new("neofly-neofly-cargo"))[0].Library);
        Assert.Single(index.Names);
    }

    [Fact]
    public void A_second_copy_with_the_same_path_is_ignored()
    {
        var index = new PackageIndex([Copy("x", @"C:\Sim\Addons\x"), Copy("x", @"c:\sim\addons\X\")], [Addons]);

        Assert.False(index.IsDuplicateCopy(new("x")));
    }

    [Fact]
    public void FindByPath_ignores_case_and_trailing_separator()
    {
        var copy = Copy("x");
        var index = new PackageIndex([copy], [Addons]);

        Assert.Equal(copy, index.FindByPath(@"c:\sim\addons\X\"));
        Assert.Null(index.FindByPath(@"C:\Sim\Addons\y"));
    }

    [Fact]
    public void Library_root_equality_uses_the_normalised_path_only()
    {
        Assert.Equal(new LibraryRoot(@"C:\Sim\Addons"), new LibraryRoot(@"c:\sim\addons\", ManagedElsewhere: true));
        Assert.Equal(new LibraryRoot(@"C:\Sim\Addons").GetHashCode(), new LibraryRoot(@"c:\sim\addons\").GetHashCode());
    }
}
