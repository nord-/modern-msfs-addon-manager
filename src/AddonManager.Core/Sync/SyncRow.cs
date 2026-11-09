using AddonManager.Core.Packages;

namespace AddonManager.Core.Sync;

public enum SyncReason { LinkAppeared, LinkDisappeared, LinkRetargeted, Moved, OnButNotLinked, OffButLinked }

/// <summary>One Package whose stored choice differs from Community (spec 7.4); Community always wins (<see cref="Reconciler.KeepCommunity"/>).</summary>
/// <param name="NewTarget">The current link target, or for <see cref="SyncReason.Moved"/> the copy found at the new location.</param>
public sealed record SyncRow(PackageName Package, SyncReason Reason, string? NewTarget)
{
    /// <summary>
    /// <c>true</c> for a Package the board shows as Not found: its link target is gone, so keeping the Community link never stores that
    /// target as the active copy. (A Not shown Package has real copies; its target is stored like any other.)
    /// </summary>
    public bool NotFound { get; init; }
}
