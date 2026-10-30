using AddonManager.Core.Community;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Sync;

/// <summary>Compares the last accepted Community snapshot with the current Community (spec 6.8, 7.1, 7.4).</summary>
public static class Reconciler
{
    /// <summary>
    /// First run: every indexed Package is on exactly when it has a Managed or Stale entry. With <paramref name="only"/>
    /// (a Library registered later) just those Packages are adopted; a pending Package is skipped and a stored active copy that still stands is kept.
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
                    var keepStored = subset && StoredCopyStands(state, name, index);
                    state.SetOn(name, true, keepStored ? null : entry.Package!.Path);
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

    /// <summary>One <see cref="SyncReason.Moved"/> row per Package whose entry is Stale, sorted by name; limited to <paramref name="only"/> when given.</summary>
    public static IReadOnlyList<SyncRow> StaleRows(UserState state, IReadOnlyList<ClassifiedEntry> current, PackageIndex index, IReadOnlyCollection<PackageName>? only = null)
        => (only ?? index.Names)
            .OrderBy(n => n.Value, StringComparer.OrdinalIgnoreCase)
            .Select(n => MovedRow(state, n, PackageFacts.EntryFor(current, n), index))
            .OfType<SyncRow>()
            .ToList();

    /// <summary>A Managed entry whose link points at another copy than the stored active copy; one row per such Package, sorted by name.</summary>
    public static IReadOnlyList<SyncRow> RetargetedRows(UserState state, IReadOnlyList<ClassifiedEntry> current, PackageIndex index, IReadOnlyCollection<PackageName> names)
    {
        var rows = new List<SyncRow>();
        foreach (var name in names.OrderBy(n => n.Value, StringComparer.OrdinalIgnoreCase))
        {
            var stored = state.Packages.GetValueOrDefault(name)?.ActiveCopyPath;
            var entry = PackageFacts.EntryFor(current, name);
            if (stored is not null && StoredCopyStands(state, name, index) && entry is { Class: EntryClass.Managed, Package: { } linked }
                && !PathNormalizer.AreEqual(stored, linked.Path))
                rows.Add(new SyncRow(name, SyncReason.LinkRetargeted, linked.Path));
        }
        return rows;
    }

    /// <summary>
    /// The stored active copy is still the Package's copy: it is indexed, or it lies under an unreachable Library. A stored copy that
    /// vanished from a reachable Library doesn't stand, so <see cref="Adopt"/> takes the linked copy and no re-point row is offered.
    /// </summary>
    private static bool StoredCopyStands(UserState state, PackageName name, PackageIndex index)
        => state.Packages.GetValueOrDefault(name)?.ActiveCopyPath is { } stored && (index.FindByPath(stored) is not null || index.IsUnreachable(stored));

    /// <summary>
    /// One row per Package that differs from the snapshot or fails the self-check; none on first run. <paramref name="notFound"/> are the
    /// folder Packages the board shows as Not found: they have no copy to link, so their snapshot entries are compared too but never
    /// self-checked, and their rows can't be restored (spec 6.1, 7.4). <paramref name="notShown"/> are the folder Packages the board shows
    /// as Not shown (no card, no Library registered for their section holds them): they have copies, but the app doesn't manage their
    /// link, so their rows can't be restored either. A Reconciler has no content, so the board supplies both sets.
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

        var rows = new List<SyncRow>();
        foreach (var name in index.Names.Union(notFound).OrderBy(n => n.Value, StringComparer.OrdinalIgnoreCase))
        {
            if (state.Pending.Contains(name)
                || PackageFacts.IsLibraryNotFound(state, name, index, current))
                continue;

            var now = PackageFacts.EntryFor(current, name);
            var isNotFound = notFound.Contains(name) && index.CopiesOf(name).Count == 0;
            // A name held by a Fixed or External entry is locked: a turn-on would be rejected, so no self-check row.
            var row = CompareWithSnapshot(state, name, now, index, isNotFound) ?? (IsLocked(now) || isNotFound ? null : SelfCheck(state, name, now));
            if (row is not null)
                rows.Add(isNotFound ? row with { Restorable = false, NotFound = true } : notShown.Contains(name) ? row with { Restorable = false } : row);
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
            if (!row.Restorable)
                choice = SyncChoice.KeepCommunity;

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
                // A Not found Package's link target is gone, so it never becomes the stored copy. Any other row keeps its target, also a
                // Not shown Package's (spec 7.4): that is a real copy.
                state.SetOn(row.Package, linked, linked && !row.NotFound ? row.NewTarget : null);
            }
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
            (_, null) => new SyncRow(name, SyncReason.LinkDisappeared, null) { Restorable = !IsLocked(now) && !notFound },
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

    private static SyncRow? MovedRow(UserState state, PackageName name, ClassifiedEntry? now, PackageIndex index)
    {
        if (now?.Class != EntryClass.Stale)
            return null;

        // The user's chosen copy wins over the first copy the classifier found.
        var copy = ActiveCopySelector.Select(name, index, null, state.Packages.GetValueOrDefault(name)?.ActiveCopyPath) ?? now.Package!;
        return new SyncRow(name, SyncReason.Moved, copy.Path);
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
