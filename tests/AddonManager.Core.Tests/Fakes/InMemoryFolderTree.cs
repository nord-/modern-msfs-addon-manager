using AddonManager.Core.FirstRun;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.Fakes;

/// <summary>A folder tree made of Package folders and empty folders, with all their ancestors.</summary>
internal sealed class InMemoryFolderTree : IFolderTree
{
    private readonly HashSet<string> folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> packages = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryFolderTree(IEnumerable<string> packages, IEnumerable<string>? emptyFolders = null)
    {
        foreach (var p in packages)
        {
            var path = PathNormalizer.Normalize(p);
            this.packages.Add(path);
            AddWithAncestors(path);
        }
        foreach (var f in emptyFolders ?? [])
            AddWithAncestors(PathNormalizer.Normalize(f));
    }

    public string? Parent(string path) => Path.GetDirectoryName(PathNormalizer.Normalize(path));

    public IReadOnlyList<string> Subfolders(string path)
    {
        var p = PathNormalizer.Normalize(path);
        return folders.Where(f => string.Equals(Parent(f), p, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool IsPackage(string path) => packages.Contains(PathNormalizer.Normalize(path));

    public bool HasPackageBeneath(string path) => packages.Any(p => PathNormalizer.IsUnder(p, path));

    public bool IsDriveRoot(string path) => Parent(path) is null;

    private void AddWithAncestors(string path)
    {
        for (var p = path; p is not null; p = Path.GetDirectoryName(p))
            folders.Add(p);
    }
}
