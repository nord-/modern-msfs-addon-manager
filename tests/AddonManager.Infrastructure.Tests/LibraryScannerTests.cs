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

        var result = LibraryScanner.Discover([new LibraryRoot(Lib, LibraryRoot.AllSections)]);

        Assert.Equal(["c172", "lgsk-airport"], result.Packages.Select(p => p.Name.Value).Order());
        Assert.All(result.Packages, p => Assert.Equal(new LibraryRoot(Lib, LibraryRoot.AllSections), p.Library));
    }

    [Fact]
    public void A_folder_discovery_cannot_list_is_reported_and_the_rest_is_found()
    {
        temp.MakePackage(@"lib\open\a");
        temp.MakePackage(@"lib\closed\hidden");
        temp.MakePackage(@"lib\after\b");
        var closed = Path.Combine(Lib, "closed");
        var info = new DirectoryInfo(closed);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            user,
            System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        var security = info.GetAccessControl();
        security.AddAccessRule(deny);
        info.SetAccessControl(security);

        try
        {
            var result = LibraryScanner.Discover([new LibraryRoot(Lib, LibraryRoot.AllSections)]);

            Assert.Equal(["a", "b"], result.Packages.Select(p => p.Name.Value).Order());
            Assert.Equal([PathNormalizer.Normalize(closed)], result.Inaccessible);
        }
        finally
        {
            security = info.GetAccessControl();
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
        }
    }

    [Fact]
    public void A_library_root_that_cannot_be_listed_is_unreachable_and_not_inaccessible()
    {
        temp.MakePackage(@"lib\a");
        var info = new DirectoryInfo(Lib);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            user,
            System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        var security = info.GetAccessControl();
        security.AddAccessRule(deny);
        info.SetAccessControl(security);

        try
        {
            var root = new LibraryRoot(Lib, LibraryRoot.AllSections);
            var result = LibraryScanner.Discover([root]);

            Assert.Equal([root], result.Unreachable);
            Assert.Empty(result.Packages);
            Assert.Empty(result.Inaccessible);
        }
        finally
        {
            security = info.GetAccessControl();
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
        }
    }

    [Fact]
    public void An_inner_library_is_walked_once_and_its_packages_carry_it()
    {
        temp.MakePackage(@"lib\a");
        temp.MakePackage(@"lib\inner\b");
        var outer = new LibraryRoot(Lib, LibraryRoot.AllSections);
        var inner = new LibraryRoot(Path.Combine(Lib, "inner"), LibraryRoot.AllSections);

        var result = LibraryScanner.Discover([outer, inner]);

        Assert.Equal(2, result.Packages.Count);
        Assert.Equal(outer, result.Packages.Single(p => p.Name.Value == "a").Library);
        Assert.Equal(inner, result.Packages.Single(p => p.Name.Value == "b").Library);
    }

    [Fact]
    public void Invalid_manifest_still_yields_a_package_named_by_folder()
    {
        WriteManifest(@"lib\broken", "{ not json");
        WriteManifest(@"lib\bom", "﻿{\"title\":\"With BOM\",\"content_type\":\"SCENERY\"}");

        var byName = LibraryScanner.Discover([new LibraryRoot(Lib, LibraryRoot.AllSections)]).Packages.ToDictionary(p => p.Name.Value);

        Assert.Equal("broken", byName["broken"].DisplayName);
        Assert.Equal("With BOM", byName["bom"].DisplayName);
    }

    [Fact]
    public void Unreachable_library_is_reported_not_thrown()
    {
        temp.MakePackage(@"lib\pkg");
        var missing = new LibraryRoot(Path.Combine(temp.Root, "unplugged", "Addons"), LibraryRoot.AllSections);

        var result = LibraryScanner.Discover([missing, new LibraryRoot(Lib, LibraryRoot.AllSections)]);

        Assert.Equal([missing], result.Unreachable);
        Assert.Single(result.Packages);
    }

    [Fact]
    public async Task A_subfolder_that_vanishes_while_scanning_is_skipped()
    {
        temp.MakePackage(@"lib\keep");
        var many = Enumerable.Range(0, 2000).Select(i => Directory.CreateDirectory(Path.Combine(Lib, "sub", $"d{i:D4}")).FullName).ToList();
        var started = new ManualResetEventSlim();

        // Installers remove folders while a scan runs; deleting from the other end races the walk until it hits a vanished one.
        var deleter = Task.Run(() =>
        {
            started.Wait();
            foreach (var folder in Enumerable.Reverse(many))
                Directory.Delete(folder);
        });

        started.Set();
        var result = LibraryScanner.Discover([new LibraryRoot(Lib, LibraryRoot.AllSections)]);
        await deleter.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(["keep"], result.Packages.Select(p => p.Name.Value));
        Assert.Empty(result.Unreachable);
    }

    [Fact]
    public void Reparse_points_inside_a_library_are_not_followed()
    {
        var outside = temp.MakePackage(@"elsewhere\pkg");
        Directory.CreateDirectory(Path.Combine(Lib, "sub"));
        new JunctionFileSystem().CreateJunction(Path.Combine(Lib, "sub", "loop"), outside);

        Assert.Empty(LibraryScanner.Discover([new LibraryRoot(Lib, LibraryRoot.AllSections)]).Packages);
    }
}
