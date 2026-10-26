using AddonManager.Core.Community;

namespace AddonManager.Core.State;

/// <summary>One entry of the last accepted Community snapshot (spec 7.4).</summary>
/// <param name="TargetWasPackage">For a link whose target existed when accepted: whether it held a manifest. <c>null</c> for a dangling link or a non-link.</param>
public sealed record SnapshotEntry(string Name, EntryKind Kind, string? Target, bool? TargetWasPackage = null)
{
    public static List<SnapshotEntry> From(IReadOnlyList<CommunityEntryInfo> entries)
        => entries.Select(e => new SnapshotEntry(e.Name, e.Kind, e.Target, e.IsLink && e.TargetExists ? e.HasManifest : null)).ToList();
}
