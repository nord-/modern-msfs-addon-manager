namespace AddonManager.Infrastructure.Tests;

public sealed class DiskFolderTreeTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly DiskFolderTree tree = new();

    public void Dispose() => temp.Dispose();

    private string P(string relative) => Path.Combine(temp.Root, relative);

    [Fact]
    public void Finds_packages_beneath_and_ignores_empty_folders()
    {
        temp.MakePackage(@"Addons\Scenery\Europe\a");
        Directory.CreateDirectory(P("Tool"));

        Assert.True(tree.HasPackageBeneath(P("Addons")));
        Assert.True(tree.HasPackageBeneath(P(@"Addons\Scenery\Europe\a")));
        Assert.False(tree.HasPackageBeneath(P("Tool")));
        Assert.True(tree.IsPackage(P(@"Addons\Scenery\Europe\a")));
        Assert.Equal([P("Addons"), P("Tool")], tree.Subfolders(temp.Root).Order());
    }

    [Fact]
    public void Junctions_are_not_followed()
    {
        var outside = temp.MakePackage(@"elsewhere\pkg");
        Directory.CreateDirectory(P("lib"));
        new JunctionFileSystem().CreateJunction(P(@"lib\loop"), outside);

        Assert.False(tree.HasPackageBeneath(P("lib")));
        Assert.Empty(tree.Subfolders(P("lib")));
    }

    [Fact]
    public void Nonexistent_paths_are_empty_not_errors()
    {
        Assert.Empty(tree.Subfolders(P("none")));
        Assert.False(tree.HasPackageBeneath(P("none")));
        Assert.False(tree.IsPackage(P("none")));
    }

    [Fact]
    public void Drive_roots_have_no_parent()
    {
        Assert.True(tree.IsDriveRoot(@"C:\"));
        Assert.False(tree.IsDriveRoot(temp.Root));
        Assert.Null(tree.Parent(@"C:\"));
        Assert.Equal(temp.Root, tree.Parent(P("x")));
    }
}
