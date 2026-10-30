using AddonManager.Core.Packages;

namespace AddonManager.Core.Sync;

public enum SyncReason { LinkAppeared, LinkDisappeared, LinkRetargeted, Moved, OnButNotLinked, OffButLinked }

public enum SyncChoice { KeepCommunity, RestoreApp }

/// <summary>One row of the sync dialog (spec 7.4).</summary>
/// <param name="NewTarget">The current link target, or for <see cref="SyncReason.Moved"/> the copy found at the new location.</param>
public sealed record SyncRow(PackageName Package, SyncReason Reason, string? NewTarget)
{
    /// <summary>
    /// <c>false</c> when restoring the app's stored choice would always be rejected (the name is held by a link the app doesn't manage);
    /// the only meaningful choice is then <see cref="SyncChoice.KeepCommunity"/>.
    /// </summary>
    public bool Restorable { get; init; } = true;

    /// <summary>
    /// <c>true</c> for a Package the board shows as Not found: its link target is gone, so keeping the Community link never stores that
    /// target as the active copy. (A Not shown Package has real copies; its target is stored like any other.)
    /// </summary>
    public bool NotFound { get; init; }

    public SyncChoice DefaultChoice => Reason == SyncReason.Moved ? SyncChoice.RestoreApp : SyncChoice.KeepCommunity;
}
