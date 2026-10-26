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
            if (Directory.Exists(library.Path))
                Walk(library, packages);
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

            foreach (var sub in Directory.EnumerateDirectories(folder, "*", Subfolders))
                pending.Push(sub);
        }
    }
}
