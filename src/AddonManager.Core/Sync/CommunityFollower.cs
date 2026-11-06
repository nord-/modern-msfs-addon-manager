using AddonManager.Core.Community;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Sync;

/// <summary>
/// Community is the truth: the stored choices and the snapshot follow it in memory (plan 5a, D1). Now only the CLI uses it; the app reconciles through the sync check and changes links itself.
/// The caller saves. Cards, sections and the pending mark are left alone.
/// </summary>
public static class CommunityFollower
{
    /// <summary>Adopts the current Community for every indexed Package and replaces the snapshot; true when a stored Package or the snapshot changed.</summary>
    public static bool Follow(UserState state, IReadOnlyList<ClassifiedEntry> current, PackageIndex index)
    {
        var before = state.Packages.ToDictionary(p => p.Key, p => p.Value);
        var snapshotBefore = state.Snapshot;

        Reconciler.Adopt(state, current, index);
        state.Snapshot = SnapshotEntry.From([.. current.Select(e => e.Entry)]);

        return snapshotBefore is null
               || !snapshotBefore.SequenceEqual(state.Snapshot)
               || before.Count != state.Packages.Count
               || state.Packages.Any(p => !before.TryGetValue(p.Key, out var old) || old != p.Value);
    }
}
