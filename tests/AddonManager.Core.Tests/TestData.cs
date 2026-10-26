using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests;

/// <summary>Small builders shared by the Core tests.</summary>
internal static class TestData
{
    public static readonly LibraryRoot Addons = new(@"C:\Sim\Addons");
    public static readonly LibraryRoot Neofly = new(@"C:\Sim\Neofly\addons");

    public static PackageCopy Copy(string name, string? path = null, LibraryRoot? lib = null, string? title = null)
    {
        lib ??= Addons;
        return new PackageCopy(new PackageName(name), path ?? Path.Combine(lib.Path, name), lib,
            new Manifest(title, null, null, null, []));
    }
}
