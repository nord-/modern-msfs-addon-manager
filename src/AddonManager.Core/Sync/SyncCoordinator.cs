using AddonManager.Core.Community;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Sync;

/// <param name="Flushed">The result of applying the pending set, <c>Deferred</c> while the sim runs, or <c>null</c> when there was nothing pending.</param>
public sealed record StartupOutcome(bool FirstRun, IReadOnlyList<SyncRow> Rows, ApplyResult? Flushed);

/// <summary>
/// The startup, sync-dialog and sim-exit sequence (spec 7.2 and 12). The UI calls <see cref="Start"/> at launch,
/// <see cref="Resolve"/> with the user's sync-dialog choices, and <see cref="OnSimExited"/> when the sim process ends.
/// </summary>
public sealed class SyncCoordinator(ChangeApplier applier, ICommunitySource community, IUserStateStore store)
{
    public StartupOutcome Start(UserState state, PackageIndex index)
    {
        var current = CommunityClassifier.Classify(community.Read(), index);

        if (state.Snapshot is null)
        {
            Reconciler.Adopt(state, current, index);
            applier.AcceptSnapshot(state);
            return new StartupOutcome(true, [], null);
        }

        Reconciler.FollowLocked(state, current, index);
        var rows = Reconciler.Diff(state, current, index);
        if (rows.Count == 0)
            applier.AcceptSnapshot(state); // records silent External, Fixed and locked changes
        else
            store.Save(state);

        // Diff skips pending Packages, so the pending set never overlaps a row awaiting the user's decision.
        var flushed = state.Pending.Count > 0 ? applier.FlushPending(state, index) : null;
        return new StartupOutcome(false, rows, flushed);
    }

    /// <summary>
    /// Applies the sync-dialog choices, then accepts the resolved Packages so their rows don't come back. A Package whose
    /// link change failed, and any change made while the dialog was open, keeps its old snapshot entry and is offered again.
    /// </summary>
    public ApplyResult Resolve(UserState state, PackageIndex index, IReadOnlyList<SyncRow> rows, IReadOnlyDictionary<PackageName, SyncChoice> choices)
    {
        var desired = Reconciler.Resolve(state, rows, choices);
        var result = applier.ApplyLinks(state, index, desired);

        var failed = result.Failed.Select(f => f.Package).ToHashSet();
        applier.AcceptSnapshot(state, rows.Select(r => r.Package).Where(p => !failed.Contains(p)).ToList());

        // Nothing left undecided: also record the silent entries, as Start does.
        if (Reconciler.Diff(state, CommunityClassifier.Classify(community.Read(), index), index).Count == 0)
            applier.AcceptSnapshot(state);
        return result;
    }

    public ApplyResult OnSimExited(UserState state, PackageIndex index) => applier.FlushPending(state, index);
}
