namespace AddonManager.Infrastructure.Tests;

public sealed class JunctionFileSystemTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly JunctionFileSystem fs = new();

    public void Dispose() => temp.Dispose();

    private string MakePackage(string relativePath) => temp.MakePackage(relativePath);

    private string Link(string name)
    {
        var community = Directory.CreateDirectory(Path.Combine(temp.Root, "community")).FullName;
        return Path.Combine(community, name);
    }

    [Fact]
    public void Create_makes_a_junction_to_the_target()
    {
        var target = MakePackage("pkg");
        fs.CreateJunction(Link("pkg"), target);
        Assert.True(File.Exists(Path.Combine(Link("pkg"), "manifest.json")));
        Assert.Equal(target, JunctionFileSystem.ReadLinkTarget(Link("pkg")), ignoreCase: true);
    }

    [Fact]
    public void Create_handles_non_ascii_and_long_paths()
    {
        var target = MakePackage(Path.Combine("EPGD Gdańsk Lech Wałęsa Airport", "Community", new string('x', 120), "drzewieckidesign-epgd"));
        fs.CreateJunction(Link("drzewieckidesign-epgd"), target);
        Assert.Equal(target, JunctionFileSystem.ReadLinkTarget(Link("drzewieckidesign-epgd")), ignoreCase: true);
        fs.RemoveLink(Link("drzewieckidesign-epgd"));
        Assert.True(File.Exists(Path.Combine(target, "manifest.json")));
    }

    [Fact]
    public void Remove_leaves_the_target_untouched()
    {
        var target = MakePackage("pkg");
        fs.CreateJunction(Link("pkg"), target);
        fs.RemoveLink(Link("pkg"));
        Assert.False(Directory.Exists(Link("pkg")));
        Assert.True(File.Exists(Path.Combine(target, "manifest.json")));
    }

    [Fact]
    public void Remove_refuses_a_real_folder()
    {
        var real = MakePackage(Path.Combine("community", "real"));
        Assert.Throws<InvalidOperationException>(() => fs.RemoveLink(real));
        Assert.True(File.Exists(Path.Combine(real, "manifest.json")));
    }

    [Fact]
    public void Create_refuses_an_existing_name()
    {
        var existing = MakePackage(Path.Combine("community", "pkg"));
        Assert.Throws<IOException>(() => fs.CreateJunction(existing, MakePackage("other")));
        Assert.Null(JunctionFileSystem.ReadLinkTarget(existing));
    }

    [Fact]
    public void Failed_creation_leaves_no_empty_folder()
    {
        var failing = new JunctionFileSystem(setReparsePoint: (_, _) => false);
        Assert.Throws<IOException>(() => failing.CreateJunction(Link("pkg"), MakePackage("pkg")));
        Assert.False(Directory.Exists(Link("pkg")));
    }

    [Fact]
    public void Create_refuses_an_existing_dangling_junction()
    {
        var gone = MakePackage("gone");
        fs.CreateJunction(Link("pkg"), gone);
        Directory.Delete(gone, true);
        Assert.Throws<IOException>(() => fs.CreateJunction(Link("pkg"), MakePackage("other")));
        Assert.Equal(gone, JunctionFileSystem.ReadLinkTarget(Link("pkg")), ignoreCase: true);
    }
}
