using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.Tests.Fakes;

namespace AddonManager.Core.Tests.Content;

public class ContentScannerTests
{
    private readonly FakeContentReader reader = new();
    private readonly InMemoryScanCache cache = new();

    private static ScanTarget T(string pathOrName, PackageSource source = PackageSource.Community)
    {
        var path = pathOrName.Contains('\\') || pathOrName.Contains('/') ? pathOrName : @"C:\Sim\Addons\" + pathOrName;
        return new(new PackageName(Path.GetFileName(path.TrimEnd('\\', '/'))), path, source, new Manifest(null, null, "SCENERY", null, []));
    }

    private static List<ScanTarget> Named(params string[] names) => [.. names.Select(n => T(n))];

    private static List<ScanTarget> Many(int count) => [.. Enumerable.Range(0, count).Select(i => T($"p{i}"))];

    private ContentScanResult Scan(IReadOnlyList<ScanTarget> targets, IReadOnlyList<string> keepUnder, bool full, IProgress<ScanProgress>? progress = null, CancellationToken token = default)
        => new ContentScanner(reader, cache).Scan(targets, cache.LoadAll(), keepUnder, full, progress, token);

    private sealed class SyncProgress : IProgress<ScanProgress>
    {
        public List<ScanProgress> Reports { get; } = [];

        public void Report(ScanProgress value) => Reports.Add(value);
    }

    [Fact]
    public void Unchanged_marker_reuses_the_cache_and_changed_marker_rereads()
    {
        var targets = Named("a", "b", "c");
        Scan(targets, [], full: false);
        reader.Touch("b");

        var r = Scan(targets, [], full: false);

        Assert.Equal(1, r.Read);
        Assert.Equal(2, r.Reused);
        Assert.Equal(["a", "b", "c", "b"], reader.ReadOrder);
    }

    [Fact]
    public void Full_rescan_rereads_everything()
    {
        var targets = Named("a", "b", "c");
        Scan(targets, [], full: false);

        var r = Scan(targets, [], full: true);

        Assert.Equal(3, r.Read);
        Assert.Equal(0, r.Reused);
    }

    [Fact]
    public void Package_with_unreadable_bgl_files_is_read_again_until_it_reads_clean()
    {
        var targets = Named("a");
        reader.UnreadableBgl = 1;       // read while its BGL files were still being copied
        Scan(targets, [], full: false);
        reader.UnreadableBgl = 0;

        var second = Scan(targets, [], full: false);
        var third = Scan(targets, [], full: false);

        Assert.Equal(1, second.Read);
        Assert.Equal(0, second.Contents.Single().UnreadableBglCount);
        Assert.Equal(1, third.Reused);
    }

    [Fact]
    public void Reads_in_the_given_order()
    {
        Scan(Named("community-pkg", "lib-pkg", "sim-pkg"), [], full: false);

        Assert.Equal(["community-pkg", "lib-pkg", "sim-pkg"], reader.ReadOrder);
    }

    [Fact]
    public void Vanished_package_is_dropped_from_cache_and_result()
    {
        var targets = Named("a", "b");
        Scan(targets, [], full: false);
        reader.Vanish("b");

        var r = Scan(targets, [], full: false);

        Assert.Equal(["a"], r.Contents.Select(c => c.Name.Value));
        Assert.DoesNotContain(cache.LoadAll(), c => c.Name.Value == "b");
        Assert.Equal(1, r.Failed);
    }

    [Fact]
    public void Package_that_vanishes_between_marker_and_read_is_dropped_from_cache_and_result()
    {
        var targets = Named("a", "b");
        Scan(targets, [], full: false);
        reader.Touch("b");              // the marker changed, so the scanner reads it...
        reader.VanishOnRead("b");       // ...and the folder is gone by then

        var r = Scan(targets, [], full: false);

        Assert.Equal(["a"], r.Contents.Select(c => c.Name.Value));
        Assert.DoesNotContain(cache.LoadAll(), c => c.Name.Value == "b");
        Assert.Equal(1, r.Failed);
        Assert.Equal(0, r.Read);
    }

    [Fact]
    public void Target_whose_drive_disappears_mid_scan_keeps_its_old_content()
    {
        var targets = Named("a", "b");
        Scan(targets, [], full: false);
        reader.ThrowOnMarker("b", new DirectoryNotFoundException());

        var r = Scan(targets, [], full: false);

        Assert.Contains(r.Contents, c => c.Name.Value == "b");
        Assert.Contains(cache.LoadAll(), c => c.Name.Value == "b");
        Assert.Equal(1, r.Failed);
    }

    [Fact]
    public void Unreadable_package_keeps_its_old_content()
    {
        var targets = Named("a", "b");
        var first = Scan(targets, [], full: false);
        var old = first.Contents.Single(c => c.Name.Value == "b");
        reader.Touch("b");              // forces a read
        reader.ThrowOn("b", new IOException());

        var r = Scan(targets, [], full: false);

        Assert.Contains(old, r.Contents);
        Assert.Contains(old, cache.LoadAll());
        Assert.Equal(1, r.Failed);
        Assert.Equal(0, r.Read);
    }

    [Fact]
    public void Inaccessible_marker_keeps_the_old_content()
    {
        var targets = Named("a", "b");
        Scan(targets, [], full: false);
        reader.ThrowOnMarker("b", new UnauthorizedAccessException());

        var r = Scan(targets, [], full: false);

        Assert.Contains(r.Contents, c => c.Name.Value == "b");
        Assert.Equal(1, r.Failed);
    }

    [Fact]
    public void Failed_package_without_cached_content_is_skipped_without_crashing()
    {
        reader.ThrowOn("b", new UnauthorizedAccessException());

        var r = Scan(Named("a", "b"), [], full: false);

        Assert.Equal(["a"], r.Contents.Select(c => c.Name.Value));
        Assert.Equal(1, r.Failed);
        Assert.Equal(1, r.Read);
    }

    [Fact]
    public void Unreadable_package_returns_old_content_with_the_targets_current_source()
    {
        Scan([T("x", PackageSource.Community)], [], full: false);
        reader.ThrowOnMarker("x", new IOException());

        var r = Scan([T("x", PackageSource.Library)], [], full: false);

        Assert.Equal(PackageSource.Library, r.Contents.Single().Source);
    }

    [Fact]
    public void Reused_content_takes_the_targets_current_source_and_manifest()
    {
        Scan([T("x", PackageSource.Community)], [], false);
        var moved = T("x", PackageSource.Library) with
        {
            Manifest = new Manifest("New title", null, "SCENERY", null, []),
        };

        var r = Scan([moved], [], false);

        var c = r.Contents.Single();
        Assert.Equal(1, r.Reused);
        Assert.Equal(PackageSource.Library, c.Source);
        Assert.Equal("New title", c.Manifest.Title);

        // The next start analyses the cached rows, so they must carry the adopted facts too.
        var row = cache.LoadAll().Single();
        Assert.Equal(PackageSource.Library, row.Source);
        Assert.Equal("New title", row.Manifest.Title);
    }

    private static ScanTarget WithDependencies(params string[] dependencies)
        => T("x") with { Manifest = new Manifest("Title", "Acme", "SCENERY", "1.0", [.. dependencies]) };

    [Fact]
    public void Reused_content_with_a_new_source_is_stored_as_library_in_the_cache()
    {
        Scan([T("x", PackageSource.Community)], [], false);

        var r = Scan([T("x", PackageSource.Library)], [], false);

        Assert.Equal(1, r.Reused);
        Assert.Equal(0, r.Read);
        Assert.Equal(PackageSource.Library, cache.LoadAll().Single().Source);
    }

    [Fact]
    public void Reused_content_with_unchanged_facts_is_not_written_again()
    {
        Scan([WithDependencies("a", "b")], [], false);
        var writes = cache.UpsertSizes.Count;

        // Equal dependencies in a new list instance are unchanged facts.
        var r = Scan([WithDependencies("a", "b")], [], false);

        Assert.Equal(1, r.Reused);
        Assert.Equal(writes, cache.UpsertSizes.Count);
    }

    [Fact]
    public void Reused_content_with_new_dependencies_is_written_again()
    {
        Scan([WithDependencies("a")], [], false);

        var r = Scan([WithDependencies("a", "b")], [], false);

        Assert.Equal(1, r.Reused);
        Assert.Equal(["a", "b"], cache.LoadAll().Single().Manifest.Dependencies);
    }

    [Fact]
    public void Content_under_an_unreachable_library_is_kept_and_returned()
    {
        Scan([T(@"E:\Addons\x"), T(@"C:\Sim\Addons\y")], [], false);

        var r = Scan([T(@"C:\Sim\Addons\y")], keepUnder: [@"E:\Addons"], full: false);

        Assert.Contains(r.Contents, c => c.Name.Value == "x");
        Assert.Contains(cache.LoadAll(), c => c.Name.Value == "x");
    }

    [Fact]
    public void Keep_under_matches_at_a_separator_boundary()
    {
        Scan([T(@"E:\AddonsOld\x"), T(@"C:\Sim\Addons\y")], [], false);

        var r = Scan([T(@"C:\Sim\Addons\y")], keepUnder: [@"E:\Addons"], full: false);

        Assert.DoesNotContain(r.Contents, c => c.Name.Value == "x");
        Assert.DoesNotContain(cache.LoadAll(), c => c.Name.Value == "x");
    }

    [Fact]
    public void Removed_package_leaves_the_cache()
    {
        Scan(Named("a", "b"), [], false);

        var r = Scan(Named("a"), [], false);

        Assert.DoesNotContain(cache.LoadAll(), c => c.Name.Value == "b");
        Assert.DoesNotContain(r.Contents, c => c.Name.Value == "b");
    }

    [Fact]
    public void Freshly_read_content_carries_the_normalised_path()
    {
        var r = Scan([T("C:/Sim/Addons/x/")], [], false);

        Assert.Equal(1, r.Read);
        Assert.Equal(@"C:\Sim\Addons\x", r.Contents.Single().Path);
        Assert.Equal(@"C:\Sim\Addons\x", cache.LoadAll().Single().Path);
    }

    private static PackageContent SeededRow(string path)
        => ContentBuilder.Build("x", "SCENERY") with { Path = path, Marker = new ChangeMarker(0, 0, null, null) };

    [Fact]
    public void Reused_content_carries_the_normalised_path()
    {
        cache.Upsert([SeededRow("C:/Sim/Addons/x/")]);

        var r = Scan([T(@"C:\Sim\Addons\x")], [], false);

        Assert.Equal(1, r.Reused);
        Assert.Equal(@"C:\Sim\Addons\x", r.Contents.Single().Path);
    }

    [Fact]
    public void Kept_offline_content_carries_the_normalised_path()
    {
        cache.Upsert([SeededRow("E:/Addons/x/")]);

        var r = Scan([T(@"C:\Sim\Addons\y")], keepUnder: [@"E:\Addons"], full: false);

        Assert.Equal(@"E:\Addons\x", r.Contents.Single(c => c.Name.Value == "x").Path);
    }

    [Fact]
    public void A_failed_upsert_is_not_retried_and_its_exception_propagates()
    {
        var failure = new InvalidOperationException("disk full");
        cache.UpsertFailure = failure;

        var thrown = Assert.Throws<InvalidOperationException>(() => Scan(Many(60), [], false));

        Assert.Same(failure, thrown);
        Assert.Equal([50], cache.UpsertSizes);
    }

    [Fact]
    public void Fresh_reads_are_upserted_in_batches_of_50_and_at_the_end()
    {
        Scan(Many(120), [], false);

        Assert.Equal([50, 50, 20], cache.UpsertSizes);
    }

    [Fact]
    public void Cancellation_keeps_work_done_so_far()
    {
        using var cts = new CancellationTokenSource();
        reader.OnRead = n =>
        {
            if (n == 60)
                cts.Cancel();
        };

        Assert.Throws<OperationCanceledException>(() => Scan(Many(120), [], false, token: cts.Token));

        // 50 from the full batch plus the 10 read since, which only the cancellation flush can have written.
        Assert.Equal(60, cache.LoadAll().Count);
    }

    [Fact]
    public void Reports_progress_per_target()
    {
        var progress = new SyncProgress();

        Scan(Named("a", "b", "c"), [], false, progress);

        Assert.Equal([1, 2, 3], progress.Reports.Select(p => p.Done));
        Assert.All(progress.Reports, p => Assert.Equal(3, p.Total));
        Assert.Equal("c", progress.Reports.Last().Current);
    }
}
