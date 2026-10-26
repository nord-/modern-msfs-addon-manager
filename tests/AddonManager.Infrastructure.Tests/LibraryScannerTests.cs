using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure.Tests;

public sealed class LibraryScannerTests : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private string Lib => Path.Combine(temp.Root, "lib");

    private void WriteManifest(string relativeFolder, string json)
    {
        var folder = Directory.CreateDirectory(Path.Combine(temp.Root, relativeFolder)).FullName;
        File.WriteAllText(Path.Combine(folder, "manifest.json"), json);
    }

    [Fact]
    public void Finds_packages_at_any_depth_and_does_not_descend_into_them()
    {
        temp.MakePackage(@"lib\Scenery\Europe\SKIATHOS V1.2\lgsk-airport");
        temp.MakePackage(@"lib\Scenery\Europe\SKIATHOS V1.2\lgsk-airport\nested\inner");
        temp.MakePackage(@"lib\Aircraft\c172");

        var result = LibraryScanner.Discover([new LibraryRoot(Lib)]);

        Assert.Equal(["c172", "lgsk-airport"], result.Packages.Select(p => p.Name.Value).Order());
        Assert.All(result.Packages, p => Assert.Equal(new LibraryRoot(Lib), p.Library));
    }

    [Fact]
    public void Invalid_manifest_still_yields_a_package_named_by_folder()
    {
        WriteManifest(@"lib\broken", "{ not json");
        WriteManifest(@"lib\bom", "﻿{\"title\":\"With BOM\",\"content_type\":\"SCENERY\"}");

        var byName = LibraryScanner.Discover([new LibraryRoot(Lib)]).Packages.ToDictionary(p => p.Name.Value);

        Assert.Equal("broken", byName["broken"].DisplayName);
        Assert.Equal("With BOM", byName["bom"].DisplayName);
    }

    [Fact]
    public void Unreachable_library_is_reported_not_thrown()
    {
        temp.MakePackage(@"lib\pkg");
        var missing = new LibraryRoot(Path.Combine(temp.Root, "unplugged", "Addons"));

        var result = LibraryScanner.Discover([missing, new LibraryRoot(Lib)]);

        Assert.Equal([missing], result.Unreachable);
        Assert.Single(result.Packages);
    }

    [Fact]
    public void Reparse_points_inside_a_library_are_not_followed()
    {
        var outside = temp.MakePackage(@"elsewhere\pkg");
        Directory.CreateDirectory(Path.Combine(Lib, "sub"));
        new JunctionFileSystem().CreateJunction(Path.Combine(Lib, "sub", "loop"), outside);

        Assert.Empty(LibraryScanner.Discover([new LibraryRoot(Lib)]).Packages);
    }
}
