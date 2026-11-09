using AddonManager.App.ViewModels;
using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using static AddonManager.App.Tests.ViewModels.Boards;

namespace AddonManager.App.Tests.ViewModels;

public sealed class BannerTextTests
{
    private const string Gsx = @"C:\GSX";

    private static IReadOnlyList<string> Lines(Banner? banner, ScanProgress? progress = null, string? error = null)
        => BannerText.Lines(banner, progress, error, Gsx);

    [Fact]
    public void An_empty_banner_has_no_lines() => Assert.Empty(Lines(EmptyBanner));

    [Fact]
    public void No_banner_and_nothing_else_has_no_lines() => Assert.Empty(Lines(null));

    [Fact]
    public void An_error_is_a_line() => Assert.Equal(["Disk full"], Lines(EmptyBanner, error: "Disk full"));

    [Fact]
    public void Progress_is_a_line() => Assert.Equal(["Scanning… 3 of 10"], Lines(null, new ScanProgress(3, 10, null)));

    [Fact]
    public void Grouping_is_a_line() => Assert.Equal(["Grouping cards… (7)"], Lines(EmptyBanner with { Grouping = 7 }));

    [Fact]
    public void Broken_and_stray_entries_are_counted()
    {
        var banner = EmptyBanner with { Broken = [Entry("a", EntryClass.Broken), Entry("b", EntryClass.Broken)], Stray = [Entry("c", EntryClass.Stray)] };
        Assert.Equal(["Community: 2 Broken, 1 Stray"], Lines(banner));
    }

    [Fact]
    public void Only_one_kind_of_entry_still_names_both()
        => Assert.Equal(["Community: 0 Broken, 1 Stray"], Lines(EmptyBanner with { Stray = [Entry("c", EntryClass.Stray)] }));

    [Fact]
    public void Not_shown_packages_that_are_on_follow_unreadable()
    {
        Assert.Equal(["1 package is on but not shown: a"], Lines(EmptyBanner with { OnButNotShown = [new PackageName("a")] }));
        Assert.Equal(["2 packages are on but not shown: a, b"], Lines(EmptyBanner with { OnButNotShown = [new PackageName("a"), new PackageName("b")] }));
        Assert.Equal(
            ["Unreadable: u", "1 package is on but not shown: a"],
            Lines(EmptyBanner with { Unreadable = [new PackageName("u")], OnButNotShown = [new PackageName("a")] }));
    }

    [Fact]
    public void Held_back_rows_follow_the_community_line()
    {
        var banner = EmptyBanner with { Broken = [Entry("a", EntryClass.Broken)] };
        Assert.Equal(
            ["Community: 1 Broken, 0 Stray", "Some links wait for MSFS to close. Close MSFS, then press F5."],
            BannerText.Lines(banner, null, null, Gsx, linksWaiting: true));
    }

    [Fact]
    public void Each_missing_library_is_a_line()
    {
        var banner = EmptyBanner with { LibrariesNotFound = [new LibraryRoot(@"D:\A", [Category.Scenery]), new LibraryRoot(@"E:\B", [Category.Aircraft])] };
        Assert.Equal([@"Library not found: D:\A", @"Library not found: E:\B"], Lines(banner));
    }

    [Fact]
    public void Unreadable_Packages_are_named()
        => Assert.Equal(["Unreadable: one, two"], Lines(EmptyBanner with { Unreadable = [new PackageName("one"), new PackageName("two")] }));

    [Fact]
    public void A_missing_gsx_folder_is_a_line() => Assert.Equal([@"GSX folder missing: C:\GSX"], Lines(EmptyBanner with { GsxFolderMissing = true }));

    [Fact]
    public void Pending_changes_are_counted() => Assert.Equal(["3 changes wait for the next sim start"], Lines(EmptyBanner with { Pending = 3 }));

    [Fact]
    public void Lines_come_in_order()
    {
        var banner = EmptyBanner with
        {
            Grouping = 2,
            Broken = [Entry("a", EntryClass.Broken)],
            OnButNotShown = [new PackageName("n")],
            LibrariesNotFound = [new LibraryRoot(@"D:\A", [Category.Scenery])],
            Unreadable = [new PackageName("u")],
            GsxFolderMissing = true,
            Pending = 1,
        };

        Assert.Equal(
            [
                "Disk full",
                "Scanning… 1 of 2",
                "Grouping cards… (2)",
                "Community: 1 Broken, 0 Stray",
                "Some links wait for MSFS to close. Close MSFS, then press F5.",
                @"Library not found: D:\A",
                "Unreadable: u",
                "1 package is on but not shown: n",
                @"GSX folder missing: C:\GSX",
                "1 changes wait for the next sim start",
            ],
            BannerText.Lines(banner, new ScanProgress(1, 2, null), "Disk full", Gsx, linksWaiting: true));
    }
}
