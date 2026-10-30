using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure;

/// <param name="Inaccessible">Folders below a reachable Library root that could not be listed (access denied or an I/O error), normalised, sorted and distinct. Packages below them are not found.</param>
public sealed record DiscoveryResult(IReadOnlyList<PackageCopy> Packages, IReadOnlyList<LibraryRoot> Unreachable, IReadOnlyList<string> Inaccessible);

/// <summary>Finds Packages in Libraries (spec 6.1, scan pass 1).</summary>
public static class LibraryScanner
{
    // Setting AttributesToSkip replaces the default Hidden | System, so they are repeated.
    // IgnoreInaccessible defaults to true; it is turned off so that Walk sees the exception and records the folder.
    private static readonly EnumerationOptions Subfolders = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = false,
    };

    public static DiscoveryResult Discover(IReadOnlyList<LibraryRoot> libraries)
    {
        var packages = new List<PackageCopy>();
        var unreachable = new List<LibraryRoot>();
        var inaccessible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in libraries)
        {
            if (!Directory.Exists(library.Path))
            {
                unreachable.Add(library);
                continue;
            }

            var found = new List<PackageCopy>();
            var denied = new List<string>();
            var inner = libraries
                .Where(other => !other.Equals(library))
                .Select(other => PathNormalizer.Normalize(other.Path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Walk(library, inner, found, denied);

            // The root can vanish during the walk (an unplugged drive): what was found is then incomplete, not deleted, so the Library counts as unreachable.
            // A root that exists but cannot be listed is as unreachable as a missing one: nothing in it could be seen.
            if (Directory.Exists(library.Path) && !denied.Contains(PathNormalizer.Normalize(library.Path), StringComparer.OrdinalIgnoreCase))
            {
                packages.AddRange(found);
                inaccessible.UnionWith(denied);
            }
            else
            {
                unreachable.Add(library);
            }
        }

        return new DiscoveryResult(packages, unreachable, [.. inaccessible.Order(StringComparer.OrdinalIgnoreCase)]);
    }

    private static void Walk(LibraryRoot library, HashSet<string> otherLibraries, List<PackageCopy> packages, List<string> inaccessible)
    {
        var pending = new Stack<string>();
        pending.Push(library.Path);

        while (pending.TryPop(out var folder))
        {
            var manifest = Path.Combine(folder, "manifest.json");
            if (File.Exists(manifest))
            {
                // A Package: never descend into it.
                packages.Add(new PackageCopy(new PackageName(Path.GetFileName(folder)), folder, library, ManifestReader.Read(manifest)));
                continue;
            }

            try
            {
                foreach (var sub in Directory.EnumerateDirectories(folder, "*", Subfolders))
                {
                    // A nested Library is walked as its own Library, so its Packages carry it and are found once.
                    if (!otherLibraries.Contains(PathNormalizer.Normalize(sub)))
                        pending.Push(sub);
                }
            }
            catch (DirectoryNotFoundException)
            {
                // The folder vanished between being found and being listed (an installer removed it): skip it.
                // The Library root itself is checked by Discover before and after the walk.
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                // Recorded instead of skipped silently: Packages below it are missing from the result, and the caller can say so.
                inaccessible.Add(PathNormalizer.Normalize(folder));
            }
        }
    }
}
