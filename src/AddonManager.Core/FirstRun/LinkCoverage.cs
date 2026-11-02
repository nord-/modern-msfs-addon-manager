using AddonManager.Core.Community;
using AddonManager.Core.Packages;

namespace AddonManager.Core.FirstRun;

/// <summary>Junctions to Packages that are counted, how many sit under a registered Library, the counted links no Library covers, and the links not counted.</summary>
public sealed record CoverageReport(int Covered, int Counted, IReadOnlyList<CommunityEntryInfo> NotCovered, IReadOnlyList<CommunityEntryInfo> NotCounted);

/// <summary>Measures how many of the existing Community links the registered Libraries cover (spec 7.1 steps 3-4).</summary>
public static class LinkCoverage
{
    // FSDT's Addon Manager creates and owns its Community junctions and keeps a huge backup tree here: never counted.
    private const string FsdtRoot = @"\FSDT Addon Manager\MSFS";

    public static CoverageReport Measure(IReadOnlyList<CommunityEntryInfo> community, IReadOnlyList<LibraryRoot> libraries)
    {
        int covered = 0, counted = 0;
        var notCovered = new List<CommunityEntryInfo>();
        var notCounted = new List<CommunityEntryInfo>();

        foreach (var entry in community.Where(e => e.IsLink))
        {
            if (!IsCounted(entry))
            {
                notCounted.Add(entry);
                continue;
            }

            counted++;
            if (libraries.Any(l => PathNormalizer.IsUnder(entry.Target!, l.Path)))
                covered++;
            else
                notCovered.Add(entry);
        }

        return new CoverageReport(covered, counted, ByName(notCovered), ByName(notCounted));
    }

    private static List<CommunityEntryInfo> ByName(List<CommunityEntryInfo> entries)
        => entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private static bool IsCounted(CommunityEntryInfo entry)
        => entry is { Kind: EntryKind.Junction, TargetExists: true, HasManifest: true, Target: not null }
           && !(PathNormalizer.Normalize(entry.Target) + @"\").Contains(FsdtRoot + @"\", StringComparison.OrdinalIgnoreCase);
}
