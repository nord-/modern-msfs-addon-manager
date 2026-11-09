using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Sync;

/// <param name="Flushed">The result of applying the pending set, <c>Deferred</c> while the sim runs, or <c>null</c> when there was nothing pending. Only data from older versions can be pending: no change is made while the sim runs any more.</param>
/// <param name="Repaired">The result of re-pointing the links to moved add-ons (decision D10: of Packages that are on, and of Packages that are off but still linked, which Community turns on), or <c>null</c> when none was attempted. Failed or rejected re-points are tried again at the next discovery.</param>
/// <param name="RepairsWaiting">Links to moved add-ons were left alone because the sim runs; the next discovery re-points them.</param>
public sealed record StartupOutcome(bool FirstRun, ApplyResult? Flushed, ApplyResult? Repaired = null, bool RepairsWaiting = false);

/// <summary>The outcome of <see cref="SyncCoordinator.RegisterLibrary"/>: the repair of the stale links it adopted.</summary>
public sealed record RegisterOutcome(ApplyResult? Repaired, bool RepairsWaiting);

/// <summary>
/// The sync check at every discovery (spec 7.2 and 12): Community always wins, so every change made outside the app is adopted without
/// asking. It does not watch the sim any more, so <see cref="OnSimExited"/> is only kept for flushing a pending set stored by older versions.
/// </summary>
public sealed class SyncCoordinator(ChangeApplier applier, ICommunitySource community, IUserStateStore store)
{
    /// <summary>
    /// Adopts Community: the stored choices follow it (also while the sim runs, since only the user data changes), the snapshot is
    /// accepted, a pending set from older data is flushed, and the links to moved add-ons are re-pointed (decision D10), which waits while
    /// the sim runs.
    /// </summary>
    public StartupOutcome Start(UserState state, PackageIndex index, Board board)
    {
        using var change = store.BeginChange();
        var current = CommunityClassifier.Classify(community.Read(), index);

        if (state.Snapshot is null)
        {
            Reconciler.Adopt(state, current, index);
            applier.AcceptSnapshot(state);
            var (adoptedRepair, adoptedWaiting) = Repair(state, index, Reconciler.StaleRepairs(state, current, index));
            return new StartupOutcome(true, null, adoptedRepair, adoptedWaiting);
        }

        // Diff skips the repair set and pending Packages, so the re-points below never overlap and the pending set is left alone.
        var repairs = new Dictionary<PackageName, DesiredLink>(Reconciler.StaleRepairs(state, current, index));
        var rows = Reconciler.Diff(state, current, index, NotFoundNames(board), NotShownNames(board));
        foreach (var (name, link) in Reconciler.KeepCommunity(state, rows))
            repairs[name] = link;
        applier.AcceptSnapshot(state);

        var flushed = state.Pending.Count > 0 ? applier.FlushPending(state, index) : null;
        var (repaired, waiting) = Repair(state, index, repairs);
        return new StartupOutcome(false, flushed, repaired, waiting);
    }

    /// <summary>
    /// Re-points the links to moved add-ons (decision D10). While the sim runs nothing is touched and the repair waits; a sim that starts
    /// between the check and the call gives the same answer. A rejected or failed repair is in the result and tried again next time.
    /// </summary>
    private (ApplyResult? Repaired, bool Waiting) Repair(UserState state, PackageIndex index, IReadOnlyDictionary<PackageName, DesiredLink> repairs)
    {
        if (repairs.Count == 0)
            return (null, false);
        if (applier.IsSimRunning)
            return (null, true);

        try
        {
            return (applier.ApplyLinks(state, index, repairs), false);
        }
        catch (SimRunningException)
        {
            return (null, true);
        }
    }

    /// <summary>
    /// Registers a Library folder for one menu section after first run (spec 6.1 "Registering a Library later"). On a new path the folder
    /// is added last and its Packages are adopted from the current Community (a linked copy becomes the stored active copy); the Stale
    /// links it adopts are repaired (decision D10, or left for the next start while the sim runs). On a known path only the section is
    /// added: the folder keeps its position (the order decides which Duplicate copy is active), nothing is adopted and no stored choice
    /// changes. Cards are never touched.
    /// </summary>
    /// <remarks>
    /// Plan 5 follows a known folder gaining a section by running <c>Settlement.Apply</c> on the last completed input (re-analysed) inside one
    /// <c>BeginChange</c>, so Packages that became eligible get cards without waiting for a scan. A new folder's Packages are placed by the
    /// next scan, which the registration starts (spec 6.1, "like first run").
    /// </remarks>
    public RegisterOutcome RegisterLibrary(UserState state, string path, Category section, PackageIndex index)
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

            return new RegisterOutcome(null, false);
        }

        var library = new LibraryRoot(path, [section]);
        state.Libraries.Add(library);

        var names = index.AllCopies.Where(c => PathNormalizer.IsUnder(c.Path, library.Path)).Select(c => c.Name).ToHashSet();
        var current = CommunityClassifier.Classify(community.Read(), index);
        Reconciler.Adopt(state, current, index, names);
        applier.AcceptSnapshot(state, names);

        // A pending Package gets no repair, as in Start: its change waits for the sim to exit.
        var (repaired, waiting) = Repair(state, index, Reconciler.StaleRepairs(state, current, index, names));
        return new RegisterOutcome(repaired, waiting);
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

    /// <summary>The folder Packages the board shows as Not found: their snapshot entries are compared too, but never self-checked.</summary>
    private static HashSet<PackageName> NotFoundNames(Board board)
        => [.. board.Cards.SelectMany(c => c.Packages).Where(p => p.Kind == PackageKind.Folder && p.Presence == Presence.NotFound).Select(p => p.Name)];

    /// <summary>The folder Packages the board shows as Not shown: the app never re-points their stale links (spec 7.4).</summary>
    private static HashSet<PackageName> NotShownNames(Board board) => [.. board.NotShown.Select(p => p.Name)];

    public ApplyResult OnSimExited(UserState state, PackageIndex index) => applier.FlushPending(state, index);
}
