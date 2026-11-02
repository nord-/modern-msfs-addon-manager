using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.FirstRun;

/// <summary>The rules for the Libraries the user adds on the first run (decision D6).</summary>
public static class FirstRunLibraries
{
    private const string CommunityRefusal = "This folder is, holds or lies inside the sim's Community folder.";
    private const string OfficialRefusal = "This folder is, holds or lies inside the sim's Official folder.";

    /// <summary>
    /// Why <paramref name="folder"/> can't be a Library, or null when it can. A folder that is, holds or lies inside the Community
    /// folder or the Official folder under <paramref name="packagesPath"/> is refused.
    /// </summary>
    public static string? Refusal(string folder, string communityPath, string packagesPath)
    {
        if (Overlaps(folder, communityPath))
            return CommunityRefusal;

        var official = Path.Combine(packagesPath, "Official");
        return Overlaps(folder, official) ? OfficialRefusal : null;
    }

    /// <summary>
    /// One Library per normalised folder, in the order the folder was first added, with its sections in <see cref="Category"/> order
    /// and without duplicates. A folder added for several sections is one Library with all of them.
    /// </summary>
    public static IReadOnlyList<LibraryRoot> Combine(IEnumerable<(string Path, Category Section)> added)
    {
        var sectionsByFolder = new Dictionary<string, List<Category>>(StringComparer.OrdinalIgnoreCase);
        var folders = new List<string>();

        foreach (var (path, section) in added)
        {
            var folder = PathNormalizer.Normalize(path);
            if (!sectionsByFolder.TryGetValue(folder, out var sections))
            {
                sections = [];
                sectionsByFolder[folder] = sections;
                folders.Add(folder);
            }

            sections.Add(section);
        }

        return [.. folders.Select(folder => new LibraryRoot(folder, sectionsByFolder[folder]))];
    }

    /// <summary>True when the folders are the same, one lies under the other.</summary>
    private static bool Overlaps(string folder, string other)
        => PathNormalizer.IsUnder(folder, other) || PathNormalizer.IsUnder(other, folder);
}
