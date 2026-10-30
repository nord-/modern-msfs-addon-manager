using AddonManager.Core.Content;

namespace AddonManager.Core.Packages;

/// <summary>Resolves which registered Libraries contain a path; Libraries may be nested (spec 6.1).</summary>
public static class LibraryRoots
{
    /// <summary>The innermost Library containing <paramref name="path"/>, reachable or not; null when none does.</summary>
    public static LibraryRoot? Innermost(IReadOnlyList<LibraryRoot> libraries, string path)
        => libraries
            .Where(lib => PathNormalizer.IsUnder(path, lib.Path))
            .MaxBy(lib => PathNormalizer.Normalize(lib.Path).Length);

    /// <summary>The sorted union of the sections of every Library containing <paramref name="path"/>; empty when none does.</summary>
    public static IReadOnlyList<Category> SectionsOf(IReadOnlyList<LibraryRoot> libraries, string path)
        => libraries
            .Where(lib => PathNormalizer.IsUnder(path, lib.Path))
            .SelectMany(lib => lib.Sections)
            .Distinct()
            .Order()
            .ToList();
}
