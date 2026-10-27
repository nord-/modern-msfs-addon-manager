using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure;

/// <param name="Targets">Every Package folder the run scans, in load order.</param>
/// <param name="ProvisionalCategories">The Category of each Library and Community target before content is read, keyed by normalised path. Sim-managed targets are left out: they are never switched.</param>
public sealed record DiscoveryOutcome(
    PackageIndex Index,
    IReadOnlyList<ClassifiedEntry> Community,
    IReadOnlyList<ScanTarget> Targets,
    IReadOnlyDictionary<string, Category> ProvisionalCategories);

/// <param name="Elapsed">The wall time of the whole run, from its start (cache load included) until the analysis is done.</param>
public sealed record ScanOutcome(DiscoveryOutcome Discovery, ContentScanResult Content, ContentAnalysis Analysis, TimeSpan Elapsed);

/// <summary>Called on the scan thread. <c>OnCached</c> runs first, before discovery.</summary>
public sealed record ScanCallbacks(
    Action<ContentAnalysis>? OnCached = null,
    Action<DiscoveryOutcome>? OnDiscovered = null,
    IProgress<ScanProgress>? Progress = null);
