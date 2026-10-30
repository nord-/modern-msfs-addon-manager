using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
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
    public StartupOutcome Start(UserState state, PackageIndex index, Board board)
    {
        using var change = store.BeginChange();
        var current = CommunityClassifier.Classify(community.Read(), index);

        if (state.Snapshot is null)
        {
            Reconciler.Adopt(state, current, index);
            applier.AcceptSnapshot(state);
            return new StartupOutcome(true, Reconciler.StaleRows(state, current, index), null);
        }

        var rows = Reconciler.Diff(state, current, index, NotFoundNames(board), NotShownNames(board));
        if (rows.Count == 0)
            applier.AcceptSnapshot(state); // records silent External and Fixed changes
        else
            store.Save(state);

        // Diff skips pending Packages, so the pending set never overlaps a row awaiting the user's decision.
        var flushed = state.Pending.Count > 0 ? applier.FlushPending(state, index) : null;
        return new StartupOutcome(false, rows, flushed);
    }

    /// <summary>
    /// Registers a Library folder for one menu section after first run (spec 6.1 "Registering a Library later"). On a new path the folder
    /// is added last and its Packages are adopted from the current Community; the rows returned are the decisions left to the user: Stale
    /// links to re-point, and links to another copy than the stored active copy. On a known path only the section is added: the folder
    /// keeps its position (the order decides which Duplicate copy is active), nothing is adopted, no rows are returned and no stored
    /// choice changes. Cards are never touched.
    /// </summary>
    /// <remarks>
    /// Plan 5 follows a known folder gaining a section by running <c>Settlement.Apply</c> on the last completed input (re-analysed) inside one
    /// <c>BeginChange</c>, so Packages that became eligible get cards without waiting for a scan. A new folder's Packages are placed by the
    /// next scan, which the registration starts (spec 6.1, "like first run").
    /// </remarks>
    public IReadOnlyList<SyncRow> RegisterLibrary(UserState state, string path, Category section, PackageIndex index)
    {
        using var change = store.BeginChange();
        var known = state.Libraries.FindIndex(l => PathNormalizer.AreEqual(l.Path, path));
        if (known >= 0)
        {
            if (!state.Libraries[known].IsRegisteredFor(section))
            {
                state.Libraries[known] = state.Libraries[known] with { Sections = [.. state.Libraries[known].Sections, section] };
                store.Save(state);
            }

            return [];
        }

        var library = new LibraryRoot(path, [section]);
        state.Libraries.Add(library);

        var names = index.AllCopies.Where(c => PathNormalizer.IsUnder(c.Path, library.Path)).Select(c => c.Name).ToHashSet();
        var current = CommunityClassifier.Classify(community.Read(), index);
        Reconciler.Adopt(state, current, index, names);
        applier.AcceptSnapshot(state, names);

        // A pending Package gets no row, as in Diff: its change waits for the sim to exit.
        var decidable = names.Where(n => !state.Pending.Contains(n)).ToList();
        return [.. Reconciler.StaleRows(state, current, index, decidable), .. Reconciler.RetargetedRows(state, current, index, decidable)];
    }

    /// <summary>
    /// Removes one menu section from a Library folder (spec 6.1 "Removing a Library"); the folder is dropped from the stored list when
    /// no section is left, and its Managed entries classify as External from then on. Cards and the user data of its Packages stay, and
    /// nothing in Community changes. A path or section that isn't registered changes nothing.
    /// </summary>
    /// <remarks>Plan 5 runs <c>Settlement.Apply</c> on the last completed input (re-analysed) inside one <c>BeginChange</c> after a folder loses a section but stays registered.</remarks>
    public void UnregisterLibrary(UserState state, string path, Category section)
    {
        using var change = store.BeginChange();
        var at = state.Libraries.FindIndex(l => PathNormalizer.AreEqual(l.Path, path));
        if (at < 0 || !state.Libraries[at].IsRegisteredFor(section))
            return;

        var left = state.Libraries[at].Sections.Where(s => s != section).ToList();
        if (left.Count == 0)
            state.Libraries.RemoveAt(at);
        else
            state.Libraries[at] = state.Libraries[at] with { Sections = left };

        store.Save(state);
    }

    /// <summary>
    /// Applies the sync-dialog choices, then accepts the resolved Packages so their rows don't come back. A Package whose
    /// link change failed, and any change made while the dialog was open, keeps its old snapshot entry and is offered again.
    /// </summary>
    public ApplyResult Resolve(UserState state, PackageIndex index, IReadOnlyList<SyncRow> rows, IReadOnlyDictionary<PackageName, SyncChoice> choices, Board board)
    {
        using var change = store.BeginChange();
        var desired = Reconciler.Resolve(state, rows, choices);
        var result = applier.ApplyLinks(state, index, desired);

        var failed = result.Failed.Select(f => f.Package).ToHashSet();
        applier.AcceptSnapshot(state, rows.Select(r => r.Package).Where(p => !failed.Contains(p)).ToList());

        // Nothing left undecided: also record the silent entries, as Start does.
        if (Reconciler.Diff(state, CommunityClassifier.Classify(community.Read(), index), index, NotFoundNames(board), NotShownNames(board)).Count == 0)
            applier.AcceptSnapshot(state);
        return result;
    }

    /// <summary>The folder Packages the board shows as Not found: their snapshot entries are compared too, but never self-checked.</summary>
    private static HashSet<PackageName> NotFoundNames(Board board)
        => [.. board.Cards.SelectMany(c => c.Packages).Where(p => p.Kind == PackageKind.Folder && p.Presence == Presence.NotFound).Select(p => p.Name)];

    /// <summary>The folder Packages the board shows as Not shown: their rows offer only "Keep Community" (spec 7.4).</summary>
    private static HashSet<PackageName> NotShownNames(Board board) => [.. board.NotShown.Select(p => p.Name)];

    public ApplyResult OnSimExited(UserState state, PackageIndex index) => applier.FlushPending(state, index);
}
