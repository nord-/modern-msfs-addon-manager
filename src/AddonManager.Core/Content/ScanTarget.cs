using AddonManager.Core.Packages;

namespace AddonManager.Core.Content;

/// <summary>A Package folder to scan for content.</summary>
public sealed record ScanTarget(PackageName Name, string Path, PackageSource Source, Manifest Manifest);
