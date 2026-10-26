namespace AddonManager.Core.FirstRun;

/// <summary>The folder structure around link targets, as the Library proposal needs it.</summary>
public interface IFolderTree
{
    /// <summary>The parent folder, or <c>null</c> at a root.</summary>
    string? Parent(string path);

    IReadOnlyList<string> Subfolders(string path);

    bool IsPackage(string path);

    /// <summary>True when the folder is a Package or has one anywhere beneath it.</summary>
    bool HasPackageBeneath(string path);

    bool IsDriveRoot(string path);
}
