using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.Packages;

public class LibraryRootTests
{
    [Fact]
    public void All_sections_are_scenery_aircraft_and_other()
        => Assert.Equal([Category.Scenery, Category.Aircraft, Category.Other], new LibraryRoot(@"C:\Sim\Addons", LibraryRoot.AllSections).Sections);

    [Fact]
    public void A_library_without_sections_is_invalid()
        => Assert.Throws<ArgumentException>(() => new LibraryRoot(@"C:\L", []));

    [Fact]
    public void Sections_are_sorted_and_distinct()
        => Assert.Equal([Category.Scenery, Category.Other], new LibraryRoot(@"C:\L", [Category.Other, Category.Scenery, Category.Other]).Sections);

    [Fact]
    public void Equality_ignores_sections()
        => Assert.Equal(new LibraryRoot(@"C:\L\", [Category.Other]), new LibraryRoot(@"c:\l", LibraryRoot.AllSections));

    [Fact]
    public void A_with_expression_is_normalised_and_validated_too()
    {
        var library = new LibraryRoot(@"C:\L", [Category.Other]);

        Assert.Equal([Category.Scenery, Category.Other], (library with { Sections = [Category.Other, Category.Scenery] }).Sections);
        Assert.Throws<ArgumentException>(() => library with { Sections = [] });
    }

    [Fact]
    public void IsRegisteredFor_checks_membership()
    {
        var library = new LibraryRoot(@"C:\L", [Category.Aircraft]);

        Assert.True(library.IsRegisteredFor(Category.Aircraft));
        Assert.False(library.IsRegisteredFor(Category.Scenery));
    }
}
