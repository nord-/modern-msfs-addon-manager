using AddonManager.Core.Community;
using AddonManager.Core.Packages;

namespace AddonManager.Core.State;

/// <summary>Facts about one Package that combine stored state, the index and the current Community.</summary>
internal static class PackageFacts
{
    /// <summary>The Community entry holding the Package's name, of any class.</summary>
    public static ClassifiedEntry? EntryFor(IReadOnlyList<ClassifiedEntry> community, PackageName name)
        => community.FirstOrDefault(e => e.Name == name);

    /// <summary>Stored path, then the copy a Managed entry points to, then Library order (E2).</summary>
    public static PackageCopy? ActiveCopy(UserState state, PackageName name, PackageIndex index, IReadOnlyList<ClassifiedEntry> community)
    {
        var entry = EntryFor(community, name);
        var communityTarget = entry?.Class == EntryClass.Managed ? entry.Entry.Target : null;
        return ActiveCopySelector.Select(name, index, communityTarget, state.Packages.GetValueOrDefault(name)?.ActiveCopyPath);
    }

    /// <summary>
    /// E3: the Package's copy (<paramref name="copyPath"/>, else the stored one) lies under an unreachable Library,
    /// or its name is held by an Offline entry.
    /// </summary>
    public static bool IsLibraryNotFound(UserState state, PackageName name, PackageIndex index, IReadOnlyList<ClassifiedEntry> community, string? copyPath = null)
    {
        var path = copyPath ?? state.Packages.GetValueOrDefault(name)?.ActiveCopyPath;
        return (path is not null && index.IsUnreachable(path)) || EntryFor(community, name)?.Class == EntryClass.Offline;
    }
}
