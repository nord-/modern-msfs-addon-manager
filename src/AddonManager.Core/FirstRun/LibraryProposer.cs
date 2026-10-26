using AddonManager.Core.Community;
using AddonManager.Core.Packages;

namespace AddonManager.Core.FirstRun;

public sealed record LibraryProposal(string Path, bool IsToolRoot);

/// <summary>Proposes Libraries from the targets of existing junctions (spec 7.1 step 3).</summary>
public static class LibraryProposer
{
    private static readonly string[] ToolRoots = [@"\Orbx Library\msfs", @"\FSDT Addon Manager\MSFS", @"\Neofly\addons"];

    public static IReadOnlyList<LibraryProposal> Propose(IReadOnlyList<CommunityEntryInfo> community, IFolderTree tree)
    {
        // Step 0: only existing junction targets; symlinks come from other tools' installers.
        var proposals = community
            .Where(e => e is { Kind: EntryKind.Junction, TargetExists: true, Target: not null })
            .Select(e => ProposeFor(PathNormalizer.Normalize(e.Target!), tree))
            .OfType<LibraryProposal>()
            .DistinctBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // E11: no nested Libraries.
        return proposals
            .Where(p => !proposals.Any(o => o != p && PathNormalizer.IsUnder(p.Path, o.Path)))
            .OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static LibraryProposal? ProposeFor(string target, IFolderTree tree)
    {
        // Step 1: a target under a well-known tool root proposes that root.
        foreach (var root in ToolRoots)
        {
            var at = (target + @"\").IndexOf(root + @"\", StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
                return new LibraryProposal(target[..(at + root.Length)], IsToolRoot: true);
        }

        // Step 2: move up from the target's parent while every subfolder of the next parent holds Packages.
        var current = tree.Parent(target);
        if (current is null || tree.IsDriveRoot(current))
            return null;

        while (tree.Parent(current) is { } next
               && !tree.IsDriveRoot(next)
               && !IsToolRoot(next)
               && !tree.IsPackage(next)
               && tree.Subfolders(next).All(tree.HasPackageBeneath))
        {
            current = next;
        }

        return new LibraryProposal(current, IsToolRoot: false);
    }

    private static bool IsToolRoot(string path) => ToolRoots.Any(r => path.EndsWith(r, StringComparison.OrdinalIgnoreCase));
}
