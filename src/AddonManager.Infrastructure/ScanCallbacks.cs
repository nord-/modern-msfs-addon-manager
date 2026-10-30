using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure;

/// <param name="Targets">Every Package folder the run scans, in load order.</param>
/// <param name="ProvisionalCategories">The Category of each Library and Community target before content is read, keyed by normalised path. Sim-managed targets are left out: they are never switched.</param>
/// <param name="PresentOutsideLibraries">The normalised memo paths of carded Packages that lie under no registered Library and whose folder exists, sorted: Not found needs them to tell "moved elsewhere" from "gone" (spec 6.1).</param>
/// <param name="Inaccessible">Folders below a reachable Library root that discovery could not list, normalised and sorted; Packages below them may exist but were not found.</param>
public sealed record DiscoveryOutcome(
    PackageIndex Index,
    IReadOnlyList<ClassifiedEntry> Community,
    IReadOnlyList<ScanTarget> Targets,
    IReadOnlyDictionary<string, Category> ProvisionalCategories,
    IReadOnlySet<string> PresentOutsideLibraries,
    IReadOnlyList<string> Inaccessible);

/// <param name="Elapsed">The wall time of the whole run, from its start (cache load included) until the analysis is done.</param>
public sealed record ScanOutcome(DiscoveryOutcome Discovery, ContentScanResult Content, ContentAnalysis Analysis, TimeSpan Elapsed)
{
    /// <summary>The GSX folder as read at the end of the run; never cached. <see cref="GsxFolderContent.Missing"/> when the service has no GSX folder.</summary>
    public GsxFolderContent Gsx { get; init; } = GsxFolderContent.Missing;
}

/// <summary>Called on the scan thread. <c>OnCached</c> runs first, before discovery, with the cached contents and their analysis, so a caller can build a <c>BoardInput</c> and re-analyse it with the current cards.</summary>
public sealed record ScanCallbacks(
    Action<IReadOnlyList<PackageContent>, ContentAnalysis>? OnCached = null,
    Action<DiscoveryOutcome>? OnDiscovered = null,
    IProgress<ScanProgress>? Progress = null);
