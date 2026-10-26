using AddonManager.Core.Community;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Sync;

/// <summary>Compares the last accepted Community snapshot with the current Community (spec 6.8, 7.1, 7.4).</summary>
public static class Reconciler
{
    /// <summary>First run: every indexed Package is on exactly when it has a Managed or Stale entry.</summary>
    public static void Adopt(UserState state, IReadOnlyList<ClassifiedEntry> current, PackageIndex index)
    {
        foreach (var name in index.Names)
        {
            var entry = PackageFacts.EntryFor(current, name);
            switch (entry?.Class)
            {
                case EntryClass.Managed:
                    state.SetOn(name, true, entry.Package!.Path);
                    break;
                case EntryClass.Stale:
                    state.SetOn(name, true);
                    break;
                default:
                    state.SetOn(name, false);
                    break;
            }
        }
    }

    /// <summary>Managed elsewhere Packages: the stored choice silently follows Community.</summary>
    public static void FollowLocked(UserState state, IReadOnlyList<ClassifiedEntry> current, PackageIndex index)
    {
        // An Offline Package keeps its choice until its Library returns.
        foreach (var name in index.Names.Where(n => PackageFacts.IsLocked(state, n, index, current) && !PackageFacts.IsOffline(state, n, index, current)))
            state.SetOn(name, PackageFacts.EntryFor(current, name)?.Class == EntryClass.Managed);
    }

    /// <summary>One row per Package that differs from the snapshot or fails the self-check; none on first run.</summary>
    public static IReadOnlyList<SyncRow> Diff(UserState state, IReadOnlyList<ClassifiedEntry> current, PackageIndex index)
    {
        if (state.Snapshot is null)
            return [];

        var rows = new List<SyncRow>();
        foreach (var name in index.Names.OrderBy(n => n.Value, StringComparer.OrdinalIgnoreCase))
        {
            if (state.Pending.Contains(name)
                || PackageFacts.IsOffline(state, name, index, current)
                || PackageFacts.IsLocked(state, name, index, current))
                continue;

            var now = PackageFacts.EntryFor(current, name);
            var row = CompareWithSnapshot(state, name, now, index) ?? SelfCheck(state, name, now);
            if (row is not null)
                rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// Applies KeepCommunity rows to the stored choices and returns the link changes still to apply:
    /// RestoreApp rows, and every Moved row (re-pointed, or its stale link removed). Those are not stored here;
    /// the apply path stores them once the link has changed (or the change is deferred).
    /// </summary>
    public static IReadOnlyDictionary<PackageName, DesiredLink> Resolve(UserState state, IReadOnlyList<SyncRow> rows, IReadOnlyDictionary<PackageName, SyncChoice> choices)
    {
        var relink = new Dictionary<PackageName, DesiredLink>();
        foreach (var row in rows)
        {
            var choice = choices.TryGetValue(row.Package, out var c) ? c : row.DefaultChoice;

            if (row.Reason == SyncReason.Moved)
            {
                relink[row.Package] = choice == SyncChoice.RestoreApp ? new DesiredLink(true, row.NewTarget) : new DesiredLink(false);
            }
            else if (choice == SyncChoice.RestoreApp)
            {
                relink[row.Package] = new DesiredLink(state.IsOn(row.Package));
            }
            else
            {
                var linked = row.Reason is SyncReason.LinkAppeared or SyncReason.LinkRetargeted or SyncReason.OffButLinked;
                state.SetOn(row.Package, linked, linked ? row.NewTarget : null);
            }
        }
        return relink;
    }

    private static SyncRow? CompareWithSnapshot(UserState state, PackageName name, ClassifiedEntry? now, PackageIndex index)
    {
        var snapshot = state.Snapshot!;
        if (now?.Class == EntryClass.Stale)
        {
            // The user's chosen copy wins over the first copy the classifier found.
            var copy = ActiveCopySelector.Select(name, index, null, state.Packages.GetValueOrDefault(name)?.ActiveCopyPath) ?? now.Package!;
            return new SyncRow(name, SyncReason.Moved, copy.Path);
        }

        var nowTarget = now?.Class == EntryClass.Managed ? now.Entry.Target : null;
        var before = snapshot.FirstOrDefault(s => new PackageName(s.Name) == name);
        var beforeTarget = before is not null && WasManaged(before, name, now, index) ? before.Target : null;

        return (beforeTarget, nowTarget) switch
        {
            (null, null) => null,
            (null, _) => new SyncRow(name, SyncReason.LinkAppeared, nowTarget),
            (_, null) => new SyncRow(name, SyncReason.LinkDisappeared, null),
            _ => PathNormalizer.AreEqual(beforeTarget, nowTarget) ? null : new SyncRow(name, SyncReason.LinkRetargeted, nowTarget),
        };
    }

    /// <summary>
    /// E7: a snapshot entry counts as Managed when its target is a copy of the Package, or no longer exists.
    /// Without disk access, "no longer exists" means: the target lies in a reachable Library where no Package
    /// is found, and Community doesn't show the same link with an existing target.
    /// </summary>
    private static bool WasManaged(SnapshotEntry before, PackageName name, ClassifiedEntry? now, PackageIndex index)
    {
        if (before.Kind is not (EntryKind.Junction or EntryKind.Symlink) || before.Target is null)
            return false;

        // A target that existed and held no manifest was never a Package, so removing the link proves nothing.
        if (before.TargetWasPackage == false)
            return false;

        var copy = index.FindByPath(before.Target);
        if (copy is not null)
            return copy.Name == name;

        var stillThere = now is { Entry: { TargetExists: true, Target: { } target } } && PathNormalizer.AreEqual(target, before.Target);
        var inReachableLibrary = index.Libraries.Any(l => PathNormalizer.IsUnder(before.Target, l.Path)) && !index.IsUnreachable(before.Target);
        return !stillThere && inReachableLibrary;
    }

    private static SyncRow? SelfCheck(UserState state, PackageName name, ClassifiedEntry? now)
    {
        var linked = now?.Class == EntryClass.Managed;
        var on = state.IsOn(name);
        if (on && !linked)
            return new SyncRow(name, SyncReason.OnButNotLinked, null);
        if (!on && linked)
            return new SyncRow(name, SyncReason.OffButLinked, now!.Entry.Target);
        return null;
    }
}
