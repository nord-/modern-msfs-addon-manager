using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests;

/// <summary>Small builders shared by the Core tests.</summary>
internal static class TestData
{
    public static readonly LibraryRoot SimAddons = new(@"C:\Sim\Addons", LibraryRoot.AllSections);
    public static readonly LibraryRoot Neofly = new(@"C:\Sim\Neofly\addons", LibraryRoot.AllSections);

    public static PackageCopy Copy(string name, string? path = null, LibraryRoot? lib = null, string? title = null)
    {
        lib ??= SimAddons;
        return new PackageCopy(new PackageName(name), path ?? Path.Combine(lib.Path, name), lib,
            new Manifest(title, null, null, null, []));
    }
}
