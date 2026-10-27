using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure;

public sealed record DiscoveryResult(IReadOnlyList<PackageCopy> Packages, IReadOnlyList<LibraryRoot> Unreachable);

/// <summary>Finds Packages in Libraries (spec 6.1, scan pass 1).</summary>
public static class LibraryScanner
{
    // Setting AttributesToSkip replaces the default Hidden | System, so they are repeated.
    private static readonly EnumerationOptions Subfolders = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
    };

    public static DiscoveryResult Discover(IReadOnlyList<LibraryRoot> libraries)
    {
        var packages = new List<PackageCopy>();
        var unreachable = new List<LibraryRoot>();

        foreach (var library in libraries)
        {
            if (!Directory.Exists(library.Path))
            {
                unreachable.Add(library);
                continue;
            }

            var found = new List<PackageCopy>();
            Walk(library, found);

            // The root can vanish during the walk (an unplugged drive): what was found is then incomplete, not deleted, so the Library counts as unreachable.
            if (Directory.Exists(library.Path))
                packages.AddRange(found);
            else
                unreachable.Add(library);
        }

        return new DiscoveryResult(packages, unreachable);
    }

    private static void Walk(LibraryRoot library, List<PackageCopy> packages)
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
                    pending.Push(sub);
            }
            catch (DirectoryNotFoundException)
            {
                // The folder vanished between being found and being listed (an installer removed it): skip it.
                // The Library root itself is checked by Discover before and after the walk.
            }
        }
    }
}
