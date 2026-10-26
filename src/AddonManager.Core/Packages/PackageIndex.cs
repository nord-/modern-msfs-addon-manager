namespace AddonManager.Core.Packages;

/// <summary>All Package copies found in the registered Libraries, looked up by name or path.</summary>
public sealed class PackageIndex
{
    private readonly Dictionary<PackageName, IReadOnlyList<PackageCopy>> byName;
    private readonly Dictionary<string, PackageCopy> byPath = new(StringComparer.OrdinalIgnoreCase);

    public PackageIndex(IEnumerable<PackageCopy> copies, IReadOnlyList<LibraryRoot> libraryOrder, IReadOnlyList<LibraryRoot>? unreachable = null)
    {
        Libraries = libraryOrder;
        Unreachable = unreachable ?? [];

        // A second copy with the same path is ignored (E11).
        foreach (var copy in copies)
            byPath.TryAdd(PathNormalizer.Normalize(copy.Path), copy);

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

    public IReadOnlyCollection<PackageName> Names => byName.Keys;

    /// <summary>The copies of a Package ordered by Library registration order, then path.</summary>
    public IReadOnlyList<PackageCopy> CopiesOf(PackageName name) => byName.GetValueOrDefault(name) ?? [];

    public bool IsDuplicateCopy(PackageName name) => CopiesOf(name).Count > 1;

    public PackageCopy? FindByPath(string path) => byPath.GetValueOrDefault(PathNormalizer.Normalize(path));

    /// <summary>True when <paramref name="path"/> lies under a Library that can't be reached right now.</summary>
    public bool IsUnreachable(string path) => Unreachable.Any(root => PathNormalizer.IsUnder(path, root.Path));

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
