using AddonManager.Core.Packages;
using AddonManager.Core.Sync;

namespace AddonManager.App.Session;

/// <summary>
/// Names one row of the sync dialog. A choice applies only to a row with the same Package and reason, so a discovery that changed a
/// row's reason after the dialog's last update gives that row its default choice, never the old reason's choice (plan 5b, D5).
/// </summary>
public sealed record SyncKey(PackageName Package, SyncReason Reason);
