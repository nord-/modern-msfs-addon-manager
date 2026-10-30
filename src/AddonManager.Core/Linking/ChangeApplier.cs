using AddonManager.Core.Community;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Linking;

public sealed record ApplyResult(
    IReadOnlyList<PackageName> Applied,
    IReadOnlyList<Rejection> Rejected,
    IReadOnlyList<(PackageName Package, string Error)> Failed,
    bool Deferred)
{
    public static ApplyResult DeferredEmpty { get; } = new([], [], [], true);
}

/// <summary>
/// The one apply path for every link change (spec 6.8): the link is changed first, then choice and snapshot
/// are saved in one write. While the sim runs, nothing is written to Community and the change is Pending. Every entry point that saves
/// opens <see cref="IUserStateStore.BeginChange"/> first, so a locked file refuses the change before any link is touched.
/// </summary>
public sealed class ChangeApplier(ILinkFileSystem fs, ICommunitySource community, IUserStateStore store, ISimProcessMonitor sim, string communityPath)
{
    public ApplyResult SetChoices(UserState state, PackageIndex index, IReadOnlyDictionary<PackageName, bool> choices)
        => Apply(state, index, choices.ToDictionary(c => c.Key, c => new DesiredLink(c.Value)), Mode.Choices);

    /// <summary>Re-derives links from the stored choice and active copy.</summary>
    public ApplyResult Relink(UserState state, PackageIndex index, IEnumerable<PackageName> packages)
        => ApplyLinks(state, index, packages.Distinct().ToDictionary(p => p, p => new DesiredLink(state.IsOn(p))));

    /// <summary>Applies the link changes of resolved sync rows (RestoreApp, Moved). Nothing is stored until its link has changed or the change is deferred.</summary>
    public ApplyResult ApplyLinks(UserState state, PackageIndex index, IReadOnlyDictionary<PackageName, DesiredLink> desired)
        => Apply(state, index, desired.ToDictionary(d => d.Key, d => d.Value), Mode.Relink);

    public ApplyResult SetActiveCopy(UserState state, PackageIndex index, PackageName package, string copyPath)
        => Apply(state, index, new Dictionary<PackageName, DesiredLink> { [package] = new(state.IsOn(package), copyPath) }, Mode.Choices);

    /// <summary>Applies the pending set once the sim has exited; Offline Packages stay pending (E3).</summary>
    public ApplyResult FlushPending(UserState state, PackageIndex index)
    {
        if (sim.IsRunning)
            return ApplyResult.DeferredEmpty;

        return Apply(state, index, state.Pending.ToDictionary(p => p, p => new DesiredLink(state.IsOn(p))), Mode.Flush);
    }

    /// <remarks>While the sim runs nothing is recorded: the result is <see cref="ApplyResult.DeferredEmpty"/> and the caller should disable the action instead of promising a later removal.</remarks>
    public ApplyResult RemoveBrokenLink(UserState state, PackageIndex index, string entryName)
    {
        if (sim.IsRunning)
            return ApplyResult.DeferredEmpty;

        using var change = store.BeginChange();
        var name = new PackageName(entryName);
        var entry = CommunityClassifier.Classify(community.Read(), index).FirstOrDefault(e => e.Name == name);
        if (entry?.Class != EntryClass.Broken)
            throw new InvalidOperationException($"'{entryName}' is not a Broken entry.");

        fs.RemoveLink(Path.Combine(communityPath, entry.Entry.Name));
        UpdateSnapshot(state, [name]);
        store.Save(state);
        return new ApplyResult([name], [], [], false);
    }

    /// <summary>Replaces the whole snapshot with the current Community and saves.</summary>
    public void AcceptSnapshot(UserState state)
    {
        using var change = store.BeginChange();
        state.Snapshot = SnapshotEntry.From(community.Read());
        store.Save(state);
    }

    /// <summary>Accepts the current Community only for the named Packages and saves; every other entry keeps its accepted state.</summary>
    public void AcceptSnapshot(UserState state, IReadOnlyCollection<PackageName> packages)
    {
        using var change = store.BeginChange();
        UpdateSnapshot(state, packages);
        store.Save(state);
    }

    private enum Mode { Choices, Relink, Flush }

    private ApplyResult Apply(UserState state, PackageIndex index, Dictionary<PackageName, DesiredLink> desired, Mode mode)
    {
        // Before any link or state change: a locked file refuses the whole change here.
        using var change = store.BeginChange();
        var current = CommunityClassifier.Classify(community.Read(), index);
        var plan = LinkPlanner.Plan(desired, state, index, current, communityPath);

        // E5: a rejected turn-on is never stored as On; when re-deriving a stored choice it is turned off.
        if (mode != Mode.Choices)
        {
            foreach (var r in plan.Rejections.Where(r => r.Reason == RejectReason.NameTaken))
                state.SetOn(r.Package, false);
        }

        if (sim.IsRunning)
            return Defer(state, desired, plan);

        var applied = new List<PackageName>();
        var failed = new List<(PackageName, string)>();
        var changed = new List<PackageName>();

        foreach (var op in plan.Ops)
        {
            try
            {
                Execute(op, current);
                changed.Add(op.Package);
                applied.Add(op.Package);
                // E2: the linked copy is stored after every successful Create or Repoint.
                state.SetOn(op.Package, desired[op.Package].On, op.TargetPath ?? desired[op.Package].CopyPath);
            }
            catch (Exception ex)
            {
                // Any failure is reported, so the links already changed in this run are still saved.
                failed.Add((op.Package, ex.Message));
            }
        }

        // E4: choices that need no operation are stored at once.
        foreach (var name in plan.NoOp)
        {
            state.SetOn(name, desired[name].On, desired[name].CopyPath);
            applied.Add(name);
        }

        if (mode == Mode.Flush)
        {
            var offline = plan.Rejections.Where(r => r.Reason == RejectReason.Offline).Select(r => r.Package).ToHashSet();
            // A failed Package stays pending: its choice is stored, so dropping it would make the next start offer to undo it.
            var keep = offline.Union(failed.Select(f => f.Item1)).ToHashSet();
            state.Pending.RemoveWhere(p => !keep.Contains(p));
        }
        else
        {
            state.Pending.ExceptWith(applied);
        }

        UpdateSnapshot(state, changed);
        store.Save(state);
        return new ApplyResult(applied, plan.Rejections, failed, false);
    }

    private ApplyResult Defer(UserState state, Dictionary<PackageName, DesiredLink> desired, LinkPlan plan)
    {
        foreach (var op in plan.Ops)
        {
            state.SetOn(op.Package, desired[op.Package].On, desired[op.Package].CopyPath);
            state.Pending.Add(op.Package);
        }
        foreach (var name in plan.NoOp)
            state.SetOn(name, desired[name].On, desired[name].CopyPath);

        store.Save(state);
        return new ApplyResult(plan.Ops.Select(o => o.Package).Concat(plan.NoOp).ToList(), plan.Rejections, [], true);
    }

    private void Execute(LinkOp op, IReadOnlyList<ClassifiedEntry> current)
    {
        switch (op.Kind)
        {
            case LinkOpKind.Create:
                fs.CreateJunction(op.LinkPath, op.TargetPath!);
                break;
            case LinkOpKind.Remove:
                fs.RemoveLink(op.LinkPath);
                break;
            case LinkOpKind.Repoint:
                var oldTarget = current.First(e => e.Name == op.Package).Entry.Target!;
                fs.RemoveLink(op.LinkPath);
                try
                {
                    fs.CreateJunction(op.LinkPath, op.TargetPath!);
                }
                catch (Exception ex)
                {
                    // E9: put the old link back, then report the failure.
                    try
                    {
                        fs.CreateJunction(op.LinkPath, oldTarget);
                    }
                    catch (Exception restoreEx)
                    {
                        throw new AggregateException($"Could not re-point '{op.LinkPath}' and could not restore its previous link.", ex, restoreEx);
                    }
                    throw;
                }
                break;
        }
    }

    /// <summary>E8: only the snapshot entries of the changed names are updated.</summary>
    private void UpdateSnapshot(UserState state, IReadOnlyCollection<PackageName> changed)
    {
        if (state.Snapshot is null || changed.Count == 0)
            return;

        var now = community.Read().Where(e => changed.Contains(new PackageName(e.Name)));
        state.Snapshot.RemoveAll(s => changed.Contains(new PackageName(s.Name)));
        state.Snapshot.AddRange(SnapshotEntry.From(now.ToList()));
    }
}
