using AddonManager.Core.Community;

namespace AddonManager.Infrastructure;

/// <summary>Reads the top-level entries of Community without ever following a link.</summary>
public sealed class CommunityReader(string communityPath) : ICommunitySource
{
    // The default would hide Hidden and System entries.
    private static readonly EnumerationOptions AllEntries = new() { AttributesToSkip = 0 };

    public IReadOnlyList<CommunityEntryInfo> Read()
        => new DirectoryInfo(communityPath).EnumerateFileSystemInfos("*", AllEntries).Select(Describe).ToList();

    private static CommunityEntryInfo Describe(FileSystemInfo info)
    {
        switch (NativeMethods.GetReparseTag(info.FullName))
        {
            case NativeMethods.JunctionTag:
                return DescribeLink(info, EntryKind.Junction);
            case NativeMethods.SymlinkTag:
                return DescribeLink(info, EntryKind.Symlink);
        }

        return info is DirectoryInfo
            ? new CommunityEntryInfo(info.Name, EntryKind.Directory, null, false, HasManifest(info.FullName), info.FullName)
            : new CommunityEntryInfo(info.Name, EntryKind.File, null, false, false);
    }

    private static CommunityEntryInfo DescribeLink(FileSystemInfo info, EntryKind kind)
    {
        var target = JunctionFileSystem.ReadLinkTarget(info.FullName);
        var exists = target is not null && Directory.Exists(target);
        return new CommunityEntryInfo(info.Name, kind, target, exists, exists && HasManifest(target!));
    }

    private static bool HasManifest(string folder) => File.Exists(Path.Combine(folder, "manifest.json"));
}
