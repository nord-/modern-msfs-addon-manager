using AddonManager.Core.Content;

namespace AddonManager.Core.Packages;

/// <summary>All Package copies found in the registered Libraries, looked up by name or path.</summary>
public sealed class PackageIndex
{
    private readonly Dictionary<PackageName, IReadOnlyList<PackageCopy>> byName;
    private readonly Dictionary<string, PackageCopy> byPath = new(StringComparer.OrdinalIgnoreCase);

    public PackageIndex(
        IEnumerable<PackageCopy> copies,
        IReadOnlyList<LibraryRoot> libraryOrder,
        IReadOnlyList<LibraryRoot>? unreachable = null,
        IReadOnlyList<string>? inaccessible = null)
    {
        Libraries = libraryOrder;
        Unreachable = unreachable ?? [];
        Inaccessible = inaccessible ?? [];

        // A path seen through nested Libraries is one copy that belongs to the innermost Library; otherwise the first one wins.
        foreach (var copy in copies)
        {
            var key = PathNormalizer.Normalize(copy.Path);
            if (!byPath.TryGetValue(key, out var existing) || Depth(copy.Library) > Depth(existing.Library))
                byPath[key] = copy;
        }

        byName = byPath.Values
            .GroupBy(c => c.Name)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PackageCopy>)g
                    .OrderBy(c => LibraryRank(c.Library))
                    .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    public IReadOnlyList<LibraryRoot> Libraries { get; }

    public IReadOnlyList<LibraryRoot> Unreachable { get; }

    /// <summary>Folders below a Library root that answers but that discovery could not read, so what lies in them is neither gone nor seen.</summary>
    public IReadOnlyList<string> Inaccessible { get; }

    public IReadOnlyCollection<PackageName> Names => byName.Keys;

    /// <summary>Every copy, ordered by Library registration order, then normalised path.</summary>
    public IReadOnlyList<PackageCopy> AllCopies => byPath
        .OrderBy(p => LibraryRank(p.Value.Library))
        .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
        .Select(p => p.Value)
        .ToList();

    /// <summary>The copies of a Package ordered by Library registration order, then path.</summary>
    public IReadOnlyList<PackageCopy> CopiesOf(PackageName name) => byName.GetValueOrDefault(name) ?? [];

    public bool IsDuplicateCopy(PackageName name) => CopiesOf(name).Count > 1;

    public PackageCopy? FindByPath(string path) => byPath.GetValueOrDefault(PathNormalizer.Normalize(path));

    /// <summary>True when <paramref name="path"/> lies under a Library that can't be reached right now.</summary>
    public bool IsUnreachable(string path) => Unreachable.Any(root => PathNormalizer.IsUnder(path, root.Path));

    /// <summary>True when <paramref name="path"/> lies under a folder discovery could not read; checked before <see cref="IsUnreachable"/>.</summary>
    public bool IsInaccessible(string path) => Inaccessible.Any(folder => PathNormalizer.IsUnder(path, folder));

    /// <summary>The innermost registered Library containing <paramref name="path"/>, whether or not the path is indexed.</summary>
    public LibraryRoot? LibraryOf(string path) => LibraryRoots.Innermost(Libraries, path);

    /// <summary>The sorted union of the sections of every registered Library containing <paramref name="path"/>.</summary>
    public IReadOnlyList<Category> SectionsOf(string path) => LibraryRoots.SectionsOf(Libraries, path);

    private static int Depth(LibraryRoot library) => PathNormalizer.Normalize(library.Path).Length;

    private int LibraryRank(LibraryRoot library)
    {
        for (var i = 0; i < Libraries.Count; i++)
        {
            if (Libraries[i].Equals(library))
                return i;
        }
        return int.MaxValue;
    }
}
