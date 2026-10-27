using AddonManager.Core.Content;

namespace AddonManager.Sim2020.Content;

/// <summary>The MSFS 2020 Official folder: each direct subfolder with a <c>manifest.json</c> is a Package (WASM cache folders have none).</summary>
public sealed class OfficialPackages(string? officialPath) : ISimManagedPackages
{
    public IReadOnlyList<string> PackageFolders()
    {
        if (officialPath is null || !Directory.Exists(officialPath))
            return [];

        try
        {
            return Directory.EnumerateDirectories(officialPath)
                .Where(folder => File.Exists(Path.Combine(folder, "manifest.json")))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
