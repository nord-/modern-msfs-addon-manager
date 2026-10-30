using AddonManager.Core.Community;
using AddonManager.Core.FirstRun;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.FirstRun;

public class LinkCoverageTests
{
    [Fact]
    public void Counts_existing_junctions_to_packages_and_covers_those_under_a_library()
    {
        CommunityEntryInfo[] community =
        [
            new("a", EntryKind.Junction, @"C:\Sim\Addons\a", true, true),
            new("b", EntryKind.Junction, @"D:\Other\b", true, true),
        ];

        var report = LinkCoverage.Measure(community, [new LibraryRoot(@"C:\Sim\Addons", LibraryRoot.AllSections)]);

        Assert.Equal((1, 2), (report.Covered, report.Counted));
        Assert.Empty(report.NotCounted);
    }

    [Fact]
    public void Symlinks_broken_links_fsdt_junctions_and_non_package_targets_are_not_counted()
    {
        CommunityEntryInfo[] community =
        [
            new("s", EntryKind.Symlink, @"C:\Sim\Addons\s", true, true),
            new("x", EntryKind.Junction, @"C:\Gone\x", false, false),
            new("f", EntryKind.Junction, @"C:\Sim\FSDT Addon Manager\MSFS\gsx", true, true),
            new("n", EntryKind.Junction, @"C:\Empty", true, false),
        ];

        var report = LinkCoverage.Measure(community, [new LibraryRoot(@"C:\Sim\Addons", LibraryRoot.AllSections)]);

        Assert.Equal((0, 0), (report.Covered, report.Counted));
        Assert.Equal(["f", "n", "s", "x"], report.NotCounted.Select(e => e.Name));
    }

    [Fact]
    public void Folders_and_files_are_neither_counted_nor_listed()
    {
        CommunityEntryInfo[] community =
        [
            new("d", EntryKind.Directory, null, false, true),
            new("f.txt", EntryKind.File, null, false, false),
        ];

        var report = LinkCoverage.Measure(community, []);

        Assert.Equal((0, 0), (report.Covered, report.Counted));
        Assert.Empty(report.NotCounted);
    }
}
