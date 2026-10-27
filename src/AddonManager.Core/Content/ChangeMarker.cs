namespace AddonManager.Core.Content;

/// <summary>Timestamps and sizes of a Package's <c>manifest.json</c> and <c>layout.json</c>; a change in either means the Package needs a rescan.</summary>
public sealed record ChangeMarker(long ManifestTicks, long ManifestSize, long? LayoutTicks, long? LayoutSize);
