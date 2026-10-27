using AddonManager.Sim2020.Content;

namespace AddonManager.Sim2020.Tests.Content;

public sealed class OfficialPackagesTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mmam-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Package(string name, bool withManifest = true)
    {
        var path = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        if (withManifest)
            File.WriteAllText(Path.Combine(path, "manifest.json"), "{}");
        return path;
    }

    [Fact]
    public void Lists_direct_subfolders_with_a_manifest_sorted_by_name()
    {
        var b = Package("b-pkg");
        var a = Package("A-pkg");

        Assert.Equal([a, b], new OfficialPackages(root).PackageFolders());
    }

    [Fact]
    public void Official_root_folders_without_manifest_are_not_listed()
    {
        var real = Package("fs-base");
        Package("fs-wasm-cache", withManifest: false);

        Assert.Equal([real], new OfficialPackages(root).PackageFolders());
    }

    [Fact]
    public void Nested_manifests_are_not_listed()
    {
        Package("outer", withManifest: false);
        Directory.CreateDirectory(Path.Combine(root, "outer", "inner"));
        File.WriteAllText(Path.Combine(root, "outer", "inner", "manifest.json"), "{}");

        Assert.Empty(new OfficialPackages(root).PackageFolders());
    }

    [Fact]
    public void Missing_official_folder_lists_nothing()
    {
        Assert.Empty(new OfficialPackages(null).PackageFolders());
        Assert.Empty(new OfficialPackages(Path.Combine(root, "missing")).PackageFolders());
    }
}
