using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure.Tests;

public class LibraryArgumentTests
{
    [Fact]
    public void A_path_alone_gets_all_types()
    {
        Assert.True(LibraryArgument.TryParse(@"C:\Sim\Addons", out var library, out var error));
        Assert.Null(error);
        Assert.Equal(@"C:\Sim\Addons", library!.Path);
        Assert.Equal(LibraryRoot.AllSections, library.Sections);
    }

    [Fact]
    public void Types_after_the_last_equals_sign_are_parsed_case_insensitively()
    {
        Assert.True(LibraryArgument.TryParse(@"C:\Sim\Addons=Scenery,aircraft", out var library, out _));
        Assert.Equal(@"C:\Sim\Addons", library!.Path);
        Assert.Equal([Category.Scenery, Category.Aircraft], library.Sections);
    }

    [Fact]
    public void An_equals_sign_followed_by_a_path_character_is_part_of_the_path()
    {
        Assert.True(LibraryArgument.TryParse(@"C:\Odd=Name\x", out var library, out _));
        Assert.Equal(@"C:\Odd=Name\x", library!.Path);
        Assert.Equal(LibraryRoot.AllSections, library.Sections);
    }

    [Fact]
    public void An_unknown_type_is_an_error()
    {
        Assert.False(LibraryArgument.TryParse(@"C:\Temp=Bogus", out var library, out var error));
        Assert.Null(library);
        Assert.Equal("Unknown Library type 'Bogus'. Use Scenery, Aircraft or Other.", error);
    }

    [Theory]
    [InlineData(@"C:\Temp=1")]
    [InlineData(@"C:\Temp=Scenery,7")]
    public void A_number_is_not_a_type(string value)
    {
        Assert.False(LibraryArgument.TryParse(value, out _, out var error));
        Assert.StartsWith("Unknown Library type", error);
    }

    [Theory]
    [InlineData(@"C:\Temp=")]
    [InlineData(@"C:\Temp=Scenery,")]
    public void An_empty_type_gets_its_own_message(string value)
    {
        Assert.False(LibraryArgument.TryParse(value, out _, out var error));
        Assert.StartsWith("Missing Library type", error);
    }
}
