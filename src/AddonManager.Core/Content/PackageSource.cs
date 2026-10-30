namespace AddonManager.Core.Content;

/// <summary>Where a Package lives. <c>Community</c> means a Fixed or External entry; <c>GsxFolder</c> a GSX profile file.</summary>
public enum PackageSource
{
    Library,
    Community,
    SimManaged,
    GsxFolder,
}
