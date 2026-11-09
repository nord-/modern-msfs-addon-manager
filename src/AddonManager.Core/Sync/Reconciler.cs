using AddonManager.Core.Community;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Sync;

/// <summary>
/// Compares the last accepted Community snapshot with the current Community (spec 6.8, 7.1, 7.4). Community always wins: every difference
/// is adopted as the stored choice (decision 2026-11-09, no sync dialog).
/// </summary>
public static class Reconciler
{
    /// <summary>
    /// First run: every indexed Package is on exactly when it has a Managed or Stale entry, and a Managed entry's copy becomes the
    /// stored active copy. With <paramref name="only"/> (a Library registered later) just those Packages are adopted and a pending
    /// Package is skipped.
    /// </summary>
    public static void Adopt(UserState state, IReadOnlyList<ClassifiedEntry> current, PackageIndex index, IReadOnlyCollection<PackageName>? only = null)
    {
        var subset = only is not null;
        foreach (var name in only ?? index.Names)
        {
            if (subset && state.Pending.Contains(name))
                continue;

            var entry = PackageFacts.EntryFor(current, name);
            switch (entry?.Class)
            {
                case EntryClass.Managed:
                    state.SetOn(name, true, entry.Package!.Path);
                    break;
                case EntryClass.Stale:
                    state.SetOn(name, true);
                    break;
                case EntryClass.Offline:
                    // A link into an unreachable Library is still a link: it counts as on on first run, and a later
                    // registration keeps the stored choice (spec 6.3: Offline counts with its stored choice).
                    if (!subset)
                        state.SetOn(name, true);
                    break;
                default:
                    state.SetOn(name, false);
                    break;
            }
        }
    }

    /// <summary>
    /// The links to repair without asking (decision D10): every Package that is on, not pending, and whose entry is Stale (its link
    /// points where the add-on used to be), re-pointed at its active copy. Limited to <paramref name="only"/> when given.
    /// <see cref="Diff"/> gives these Packages no row of any reason.
    /// </summary>
    public static IReadOnlyDictionary<PackageName, DesiredLink> StaleRepairs(UserState state, IReadOnlyList<ClassifiedEntry> current, PackageIndex index, IReadOnlyCollection<PackageName>? only = null)
        => (only ?? index.Names)
            .Where(n => state.IsOn(n) && !state.Pending.Contains(n))
            .OrderBy(n => n.Value, StringComparer.OrdinalIgnoreCase)
            .Select(n => (Name: n, Copy: MovedCopy(state, n, PackageFacts.EntryFor(current, n), index)))
            .Where(r => r.Copy is not null)
            .ToDictionary(r => r.Name, r => new DesiredLink(true, r.Copy!.Path));

    /// <summary>
    /// One row per Package that differs from the snapshot or fails the self-check; none on first run. <paramref name="notFound"/> are the
    /// folder Packages the board shows as Not found: they have no copy to link, so their snapshot entries are compared too but never
    /// self-checked (spec 6.1, 7.4). <paramref name="notShown"/> are the folder Packages the board shows as Not shown (no card, no Library
    /// registered for their section holds them): they have copies, but the app doesn't manage their link, so a stale link of one that is
    /// off gives no <see cref="SyncReason.Moved"/> row (re-pointing it would be the app's change). A Reconciler has no content, so the board
    /// supplies both sets.
    /// </summary>
    public static IReadOnlyList<SyncRow> Diff(
        UserState state,
        IReadOnlyList<ClassifiedEntry> current,
        PackageIndex index,
        IReadOnlySet<PackageName> notFound,
        IReadOnlySet<PackageName> notShown)
    {
        if (state.Snapshot is null)
            return [];

        // Rows are computed before the repair, so a Package about to be repaired would only give a row that undoes it.
        var repairs = StaleRepairs(state, current, index);
        var rows = new List<SyncRow>();
        foreach (var name in index.Names.Union(notFound).OrderBy(n => n.Value, StringComparer.OrdinalIgnoreCase))
        {
            if (state.Pending.Contains(name)
                || repairs.ContainsKey(name)
                || PackageFacts.IsLibraryNotFound(state, name, index, current))
                continue;

            var now = PackageFacts.EntryFor(current, name);
            var isNotFound = notFound.Contains(name) && index.CopiesOf(name).Count == 0;
            // A name held by a Fixed or External entry is locked: a turn-on would be rejected, so no self-check row.
            var row = CompareWithSnapshot(state, name, now, index, isNotFound) ?? (IsLocked(now) || isNotFound ? null : SelfCheck(state, name, now));
            if (row is null || (row.Reason == SyncReason.Moved && notShown.Contains(name)))
                continue;
            rows.Add(isNotFound ? row with { NotFound = true } : row);
        }
        return rows;
    }

    /// <summary>
    /// Adopts Community for every row: a Package is on exactly when Community links it, and a linked copy becomes the stored active copy
    /// (never a Not found Package's vanished target). Returns the link changes still to apply: every <see cref="SyncReason.Moved"/> row
    /// (an off Package whose link points where the add-on used to be) is turned on by re-pointing its link at the new location. Those are
    /// not stored here; the apply path stores them once the link has changed.
    /// </summary>
    public static IReadOnlyDictionary<PackageName, DesiredLink> KeepCommunity(UserState state, IReadOnlyList<SyncRow> rows)
    {
        var relink = new Dictionary<PackageName, DesiredLink>();
        foreach (var row in rows)
        {
            if (row.Reason == SyncReason.Moved)
            {
                relink[row.Package] = new DesiredLink(true, row.NewTarget);
                continue;
            }

            var linked = row.Reason is SyncReason.LinkAppeared or SyncReason.LinkRetargeted or SyncReason.OffButLinked;
            state.SetOn(row.Package, linked, linked && !row.NotFound ? row.NewTarget : null);
        }
        return relink;
    }

    private static SyncRow? CompareWithSnapshot(UserState state, PackageName name, ClassifiedEntry? now, PackageIndex index, bool notFound)
    {
        var snapshot = state.Snapshot!;
        var moved = MovedRow(state, name, now, index);
        if (moved is not null)
            return moved;

        // A Not found Package's link is Broken, not Managed: it is still the link the snapshot knew while its target stays the same.
        var linked = now?.Class == EntryClass.Managed || (notFound && now?.Class == EntryClass.Broken);
        var nowTarget = linked ? now!.Entry.Target : null;
        var before = snapshot.FirstOrDefault(s => new PackageName(s.Name) == name);

        // The same link to the same vanished target is unchanged, whatever WasManaged says (an External target was never a Library copy).
        if (notFound && before?.Target is { } known && nowTarget is not null && PathNormalizer.AreEqual(known, nowTarget))
            return null;

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

    private static bool IsLocked(ClassifiedEntry? entry) => entry?.Class is EntryClass.Fixed or EntryClass.External;

    /// <summary>The copy a Stale entry is re-pointed at: the user's chosen copy wins over the first copy the classifier found. <c>null</c> when the entry isn't Stale.</summary>
    private static PackageCopy? MovedCopy(UserState state, PackageName name, ClassifiedEntry? now, PackageIndex index)
        => now?.Class == EntryClass.Stale
            ? ActiveCopySelector.Select(name, index, null, state.Packages.GetValueOrDefault(name)?.ActiveCopyPath) ?? now.Package!
            : null;

    /// <summary>A Stale entry of a Package that is off: Community links it, so it is turned on and re-pointed. An on Package is repaired instead.</summary>
    private static SyncRow? MovedRow(UserState state, PackageName name, ClassifiedEntry? now, PackageIndex index)
        => !state.IsOn(name) && MovedCopy(state, name, now, index) is { } copy ? new SyncRow(name, SyncReason.Moved, copy.Path) : null;

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
