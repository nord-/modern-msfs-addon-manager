using AddonManager.Core.Packages;

namespace AddonManager.Core.Sync;

public enum SyncReason { LinkAppeared, LinkDisappeared, LinkRetargeted, Moved, OnButNotLinked, OffButLinked }

public enum SyncChoice { KeepCommunity, RestoreApp }

/// <summary>One row of the sync dialog (spec 7.4).</summary>
/// <param name="NewTarget">The current link target, or for <see cref="SyncReason.Moved"/> the copy found at the new location.</param>
public sealed record SyncRow(PackageName Package, SyncReason Reason, string? NewTarget)
{
    public SyncChoice DefaultChoice => Reason == SyncReason.Moved ? SyncChoice.RestoreApp : SyncChoice.KeepCommunity;
}
