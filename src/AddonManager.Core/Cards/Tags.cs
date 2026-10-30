using AddonManager.Core.Packages;

namespace AddonManager.Core.Cards;

/// <summary>
/// Derives Tags from the folders between a Library root and a Package folder (spec 6.4).
/// Works on path strings only; it never touches the file system.
/// </summary>
public sealed class TagIndex
{
    // Folder names that describe the layout of a Library rather than its content.
    private static readonly HashSet<string> LayoutFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Community", "Scenery", "Aircraft", "Aircrafts", "Other",
    };

    private readonly IReadOnlyList<LibraryRoot> _libraries;
    private readonly HashSet<string> _packages;
    private readonly Dictionary<string, int> _packagesBeneath;

    private TagIndex(IReadOnlyList<LibraryRoot> libraries, HashSet<string> packages, Dictionary<string, int> packagesBeneath)
    {
        _libraries = libraries;
        _packages = packages;
        _packagesBeneath = packagesBeneath;
    }

    /// <summary>Counts, for every folder, the distinct Package paths beneath it.</summary>
    public static TagIndex Build(IReadOnlyList<LibraryRoot> libraries, IEnumerable<string> packagePaths)
    {
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packagesBeneath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in packagePaths)
        {
            var package = PathNormalizer.Normalize(path);
            if (!packages.Add(package))
                continue;

            for (var folder = Path.GetDirectoryName(package); folder is not null; folder = Path.GetDirectoryName(folder))
                packagesBeneath[folder] = packagesBeneath.GetValueOrDefault(folder) + 1;
        }

        return new TagIndex(libraries, packages, packagesBeneath);
    }

    /// <summary>Tags of a Package, from the root down. Also works for a Package that is not in the index.</summary>
    public IReadOnlyList<string> TagsOf(string packagePath)
    {
        var package = PathNormalizer.Normalize(packagePath);
        var library = LibraryRoots.Innermost(_libraries, package);
        // A Package that is itself a Library root has no folders between it and the root.
        if (library is null || PathNormalizer.AreEqual(package, library.Path))
            return [];

        var root = PathNormalizer.Normalize(library.Path);
        // A Package that is not indexed still counts itself when deciding whether a folder holds more than one Package.
        var extraPackage = _packages.Contains(package) ? 0 : 1;
        var tags = new List<string>();

        for (var folder = Path.GetDirectoryName(package);
             folder is not null && !string.Equals(folder, root, StringComparison.OrdinalIgnoreCase);
             folder = Path.GetDirectoryName(folder))
        {
            var name = Path.GetFileName(folder);
            if (LayoutFolderNames.Contains(name))
                continue;
            if (_packagesBeneath.GetValueOrDefault(folder) + extraPackage < 2)
                continue;

            tags.Add(name);
        }

        tags.Reverse();
        return tags;
    }
}
