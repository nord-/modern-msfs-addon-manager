using AddonManager.Core.FirstRun;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure;

/// <summary>The real folder tree for the Library proposal. Memoises answers for one run; never follows links.</summary>
public sealed class DiskFolderTree : IFolderTree
{
    // Same rules as LibraryScanner, so a folder counts as holding Packages exactly when discovery would find them.
    private static readonly EnumerationOptions Subfolder = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
    };

    private readonly Dictionary<string, bool> hasPackageBeneath = new(StringComparer.OrdinalIgnoreCase);

    public string? Parent(string path) => Path.GetDirectoryName(PathNormalizer.Normalize(path));

    public IReadOnlyList<string> Subfolders(string path)
        => Directory.Exists(path) ? Directory.EnumerateDirectories(path, "*", Subfolder).ToList() : [];

    public bool IsPackage(string path) => File.Exists(Path.Combine(path, "manifest.json"));

    public bool HasPackageBeneath(string path)
    {
        var key = PathNormalizer.Normalize(path);
        if (!hasPackageBeneath.TryGetValue(key, out var result))
        {
            result = IsPackage(key) || Subfolders(key).Any(HasPackageBeneath);
            hasPackageBeneath[key] = result;
        }
        return result;
    }

    public bool IsDriveRoot(string path) => Parent(path) is null;
}
