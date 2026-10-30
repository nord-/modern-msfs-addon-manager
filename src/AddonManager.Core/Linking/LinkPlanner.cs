using AddonManager.Core.Community;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Linking;

/// <param name="CopyPath">Overrides the active copy (used by SetActiveCopy and Moved rows before anything is stored).</param>
public sealed record DesiredLink(bool On, string? CopyPath = null);

public enum LinkOpKind { Create, Remove, Repoint }

public sealed record LinkOp(LinkOpKind Kind, PackageName Package, string LinkPath, string? TargetPath);

public enum RejectReason { NameTaken, NotInLibrary, Offline }

/// <param name="HeldBy">For <see cref="RejectReason.NameTaken"/>: the class of the entry holding the name.</param>
public sealed record Rejection(PackageName Package, RejectReason Reason, EntryClass? HeldBy = null);

public sealed record LinkPlan(IReadOnlyList<LinkOp> Ops, IReadOnlyList<Rejection> Rejections, IReadOnlyList<PackageName> NoOp);

/// <summary>Turns desired on/off states into link operations without touching disk or state.</summary>
public static class LinkPlanner
{
    public static LinkPlan Plan(IReadOnlyDictionary<PackageName, DesiredLink> desired, UserState state, PackageIndex index,
        IReadOnlyList<ClassifiedEntry> community, string communityPath)
    {
        var ops = new List<LinkOp>();
        var rejections = new List<Rejection>();
        var noOp = new List<PackageName>();

        foreach (var (name, want) in desired)
        {
            // Rule 1 (E3): checked first, so a pending change on an unplugged Library stays pending.
            if (PackageFacts.IsLibraryNotFound(state, name, index, community, want.CopyPath))
            {
                rejections.Add(new Rejection(name, RejectReason.Offline));
                continue;
            }

            var entry = PackageFacts.EntryFor(community, name);

            // Rule 2 (spec 6.1): while a Fixed or External entry holds the name, no Library copy can be made active.
            if (want.CopyPath is not null && entry?.Class is EntryClass.Fixed or EntryClass.External)
            {
                rejections.Add(new Rejection(name, RejectReason.NameTaken, entry.Class));
                continue;
            }

            // Rule 3: an explicit copy must be a copy of this Package, whether it is switched on or off.
            var copy = want.CopyPath is null ? PackageFacts.ActiveCopy(state, name, index, community) : index.FindByPath(want.CopyPath);
            if (want.CopyPath is not null ? copy is null || copy.Name != name : want.On && copy is null)
            {
                rejections.Add(new Rejection(name, RejectReason.NotInLibrary));
                continue;
            }

            var linkPath = Path.Combine(communityPath, name.Value);
            switch (entry?.Class)
            {
                // Rule 4.
                case EntryClass.Managed or EntryClass.Stale:
                    if (!want.On)
                        ops.Add(new LinkOp(LinkOpKind.Remove, name, linkPath, null));
                    else if (entry.Class == EntryClass.Managed && PathNormalizer.AreEqual(entry.Entry.Target!, copy!.Path))
                        noOp.Add(name);
                    else
                        ops.Add(new LinkOp(LinkOpKind.Repoint, name, linkPath, copy!.Path));
                    break;

                // Rule 5.
                case { } held:
                    if (want.On)
                        rejections.Add(new Rejection(name, RejectReason.NameTaken, held));
                    else
                        noOp.Add(name);
                    break;

                // Rule 6.
                default:
                    if (want.On)
                        ops.Add(new LinkOp(LinkOpKind.Create, name, linkPath, copy!.Path));
                    else
                        noOp.Add(name);
                    break;
            }
        }

        return new LinkPlan(ops, rejections, noOp);
    }
}
