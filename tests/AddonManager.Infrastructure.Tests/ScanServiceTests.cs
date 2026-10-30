using AddonManager.Core.Community;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Infrastructure.Tests.Fakes;

namespace AddonManager.Infrastructure.Tests;

public sealed class ScanServiceTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private sealed class FakeSimManaged(List<string> folders) : ISimManagedPackages
    {
        public IReadOnlyList<string> PackageFolders() => folders;
    }

    private readonly TempFolder temp = new();
    private readonly GatedContentReader reader = new();
    private readonly InMemoryScanCache cache = new();
    private readonly List<string> simFolders = [];
    private readonly string community;
    private readonly ScanService service;
    private readonly UserState state;

    // Three Library Packages plus one Sim-managed Package.
    private const int TargetCount = 4;
    private readonly string simPackage;

    public ScanServiceTests()
    {
        community = Directory.CreateDirectory(Path.Combine(temp.Root, "community")).FullName;
        Package("lib", "p1");
        Package("lib", "p2");
        Package("lib", "p3");
        simPackage = Package("official", "asobo-sim");
        simFolders.Add(simPackage);
        state = new UserState { Libraries = [new LibraryRoot(Path.Combine(temp.Root, "lib"), LibraryRoot.AllSections)] };
        service = new ScanService(reader, cache, community, new FakeSimManaged(simFolders));
    }

    public void Dispose()
    {
        service.Dispose();
        temp.Dispose();
    }

    private string Package(string folder, string name, string contentType = "SCENERY")
    {
        var path = Directory.CreateDirectory(Path.Combine(temp.Root, folder, name)).FullName;
        File.WriteAllText(Path.Combine(path, "manifest.json"), $$"""{"title":"{{name}}","content_type":"{{contentType}}"}""");
        return path;
    }

    private static UserState StateWith(params string[] libraryPaths) => new() { Libraries = [.. libraryPaths.Select(p => new LibraryRoot(p, LibraryRoot.AllSections))] };

    private string LibPath(string folder) => Path.Combine(temp.Root, folder);

    private static string Key(string path) => PathNormalizer.Normalize(path);

    private static Task<T> Bounded<T>(Task<T> task) => task.WaitAsync(Bound);

    private static IEnumerable<string> Names(ScanOutcome outcome) => outcome.Content.Contents.Select(c => c.Name.Value);

    private static AirportCode Code(string text) => AirportCode.TryParse(text, out var code) ? code : throw new ArgumentException(text);

    [Fact]
    public async Task A_run_reads_the_gsx_folder()
    {
        var gsx = Directory.CreateDirectory(Path.Combine(temp.Root, "gsx")).FullName;
        File.WriteAllText(Path.Combine(gsx, "eham.ini"), "");
        using var withGsx = new ScanService(reader, cache, community, new FakeSimManaged(simFolders), gsx);

        var outcome = await Bounded(withGsx.RunAsync(state, false));

        Assert.True(outcome.Gsx.Found);
        Assert.Equal("eham", Assert.Single(outcome.Gsx.Profiles).Name.Value);
    }

    [Fact]
    public async Task A_run_without_a_gsx_folder_reports_it_missing()
    {
        var withoutFolder = await Bounded(service.RunAsync(state, false));
        using var absent = new ScanService(reader, cache, community, new FakeSimManaged(simFolders), Path.Combine(temp.Root, "nope"));
        var withAbsentFolder = await Bounded(absent.RunAsync(state, false));

        Assert.False(withoutFolder.Gsx.Found);
        Assert.False(withAbsentFolder.Gsx.Found);
    }

    [Fact]
    public async Task Discovery_is_reported_before_content_is_read()
    {
        var markerCallsAtDiscovery = -1;
        var discoveredTargets = 0;

        await Bounded(service.RunAsync(state, false, new ScanCallbacks(OnDiscovered: d =>
        {
            markerCallsAtDiscovery = reader.MarkerCalls;
            discoveredTargets = d.Targets.Count;
        })));

        Assert.Equal(0, markerCallsAtDiscovery);
        Assert.Equal(TargetCount, discoveredTargets);
        Assert.Equal(TargetCount, reader.MarkerCalls);
    }

    [Fact]
    public async Task Provisional_category_uses_layout_bgl_presence()
    {
        var gsx = Package("lib", "gsx-pro");
        reader.HasBglResult["gsx-pro"] = false;
        reader.HasBglResult["p1"] = true;

        var d = (await Bounded(service.RunAsync(state, false))).Discovery;

        Assert.Equal(Category.Other, d.ProvisionalCategories[Key(gsx)]);
        Assert.Equal(Category.Scenery, d.ProvisionalCategories[Key(Path.Combine(LibPath("lib"), "p1"))]);
        // No layout.json (null) keeps Scenery.
        Assert.Equal(Category.Scenery, d.ProvisionalCategories[Key(Path.Combine(LibPath("lib"), "p2"))]);
    }

    [Fact]
    public async Task Provisional_category_uses_content_type()
    {
        var plane = Package("lib", "plane", "AIRCRAFT");
        var misc = Package("lib", "misc", "MISC");

        var d = (await Bounded(service.RunAsync(state, false))).Discovery;

        Assert.Equal(Category.Aircraft, d.ProvisionalCategories[Key(plane)]);
        Assert.Equal(Category.Other, d.ProvisionalCategories[Key(misc)]);
    }

    [Fact]
    public async Task Provisional_category_prefers_loaded_content_over_the_reader()
    {
        reader.HasBglResult["p1"] = false;
        await Bounded(service.RunAsync(state, false));
        var callsAfterFirstRun = reader.HasBglCallsFor("p1");
        reader.HasBglResult["p1"] = true;

        var d = (await Bounded(service.RunAsync(state, false))).Discovery;

        // The cached row says no BGL; the reader (now saying yes) isn't asked.
        Assert.Equal(Category.Other, d.ProvisionalCategories[Key(Path.Combine(LibPath("lib"), "p1"))]);
        Assert.Equal(callsAfterFirstRun, reader.HasBglCallsFor("p1"));
    }

    [Fact]
    public async Task Concurrent_requests_coalesce_into_one_follow_up_run()
    {
        reader.Gate = new SemaphoreSlim(0);
        var first = service.RunAsync(state, false);
        var second = service.RunAsync(state, false);
        var third = service.RunAsync(state, true);
        reader.Gate.Release(int.MaxValue);
        await Bounded(Task.WhenAll(first, second, third));

        Assert.Same(second, third);
        Assert.NotSame(first, second);
        Assert.Equal(2 * TargetCount, reader.MarkerCalls);   // two runs
        Assert.Equal(2 * TargetCount, reader.ReadCalls);     // empty cache, then a full follow-up: nothing reused
        Assert.Equal(1, reader.MaxConcurrentMarkerCalls);    // never two runs at once
        Assert.Equal(2, cache.LoadCount);
    }

    [Fact]
    public async Task Queued_run_is_full_if_any_caller_asked_for_it()
    {
        reader.Gate = new SemaphoreSlim(0);
        var first = service.RunAsync(state, false);
        var second = service.RunAsync(state, true);
        var third = service.RunAsync(state, false);
        reader.Gate.Release(int.MaxValue);
        await Bounded(Task.WhenAll(first, second, third));

        Assert.Same(second, third);
        Assert.Equal(2 * TargetCount, reader.ReadCalls);
    }

    [Fact]
    public async Task Queued_run_reuses_unchanged_content_when_nobody_asked_for_full()
    {
        FixSections("p1", "p2", "p3");
        reader.Gate = new SemaphoreSlim(0);
        var first = service.RunAsync(state, false);
        var second = service.RunAsync(state, false);
        var third = service.RunAsync(state, false);
        reader.Gate.Release(int.MaxValue);
        await Bounded(Task.WhenAll(first, second, third));

        Assert.Equal(2 * TargetCount, reader.MarkerCalls);
        Assert.Equal(TargetCount, reader.ReadCalls);
        Assert.Equal(TargetCount, (await second).Content.Reused);
    }

    private void FixSections(params string[] names)
    {
        foreach (var name in names)
            state.Packages[new PackageName(name)] = new PackageState(false) { Section = Category.Scenery };
    }

    [Fact]
    public async Task Packages_without_a_fixed_section_are_read_again_on_every_run()
    {
        await Bounded(service.RunAsync(state, false));
        var afterFirst = reader.ReadCalls;

        await Bounded(service.RunAsync(state, false));

        // p1-p3 have no section and are read again; the Sim-managed Package is reused.
        Assert.Equal(3, reader.ReadCalls - afterFirst);
    }

    [Fact]
    public async Task Packages_with_a_fixed_section_are_reused()
    {
        FixSections("p1", "p2", "p3");
        await Bounded(service.RunAsync(state, false));
        var afterFirst = reader.ReadCalls;

        var second = await Bounded(service.RunAsync(state, false));

        Assert.Equal(afterFirst, reader.ReadCalls);
        Assert.Equal(TargetCount, second.Content.Reused);
    }

    [Fact]
    public async Task A_carded_package_outside_every_library_is_present_while_its_folder_exists()
    {
        var outside = Package("elsewhere", "away");
        var inside = Path.Combine(LibPath("lib"), "p1");
        state.Packages[new PackageName("away")] = new PackageState(true) { CardId = 1, Memo = MemoAt(outside) };
        state.Packages[new PackageName("p1")] = new PackageState(true) { CardId = 2, Memo = MemoAt(inside) };
        state.Packages[new PackageName("no-card")] = new PackageState(true) { Memo = MemoAt(outside) };
        state.Packages[new PackageName("gone")] = new PackageState(true) { CardId = 3, Memo = MemoAt(Path.Combine(temp.Root, "missing", "gone")) };

        var present = (await Bounded(service.RunAsync(state, false))).Discovery.PresentOutsideLibraries;

        Assert.Equal([Key(outside)], present);

        Directory.Delete(outside, recursive: true);
        var after = (await Bounded(service.RunAsync(state, false))).Discovery.PresentOutsideLibraries;

        Assert.Empty(after);
    }

    private static PackageMemo MemoAt(string path)
        => new(path, PackageSource.Library, "x", null, null, null, BglPlacement.None, [], [], AircraftRole.None, []);

    [Fact]
    public async Task A_call_after_the_run_finished_starts_a_new_run()
    {
        var first = service.RunAsync(state, false);
        await Bounded(first);
        var second = service.RunAsync(state, false);
        await Bounded(second);

        Assert.NotSame(first, second);
        Assert.Equal(2 * TargetCount, reader.MarkerCalls);
    }

    [Fact]
    public async Task Follow_up_run_uses_the_last_callers_state()
    {
        Package("libB", "only-in-b");
        var stateA = state;
        var stateB = StateWith(LibPath("lib"), LibPath("libB"));
        reader.Gate = new SemaphoreSlim(0);

        var first = service.RunAsync(stateA, false);
        var second = service.RunAsync(stateA, false);
        var third = service.RunAsync(stateB, false);
        reader.Gate.Release(int.MaxValue);
        await Bounded(Task.WhenAll(first, second, third));

        Assert.Same(second, third);
        Assert.Contains("only-in-b", Names(await third));
        Assert.DoesNotContain("only-in-b", Names(await first));
    }

    [Fact]
    public async Task Follow_up_run_uses_the_last_callers_callbacks()
    {
        var secondCalled = false;
        var thirdCalled = false;
        reader.Gate = new SemaphoreSlim(0);

        var first = service.RunAsync(state, false);
        var second = service.RunAsync(state, false, new ScanCallbacks(OnDiscovered: _ => secondCalled = true));
        var third = service.RunAsync(state, false, new ScanCallbacks(OnDiscovered: _ => thirdCalled = true));
        reader.Gate.Release(int.MaxValue);
        await Bounded(Task.WhenAll(first, second, third));

        Assert.False(secondCalled);
        Assert.True(thirdCalled);
    }

    [Fact]
    public async Task Cancelling_one_caller_does_not_cancel_the_shared_run()
    {
        reader.Gate = new SemaphoreSlim(0);
        using var cancel = new CancellationTokenSource();

        var first = service.RunAsync(state, false);
        var second = service.RunAsync(state, false, null, cancel.Token);
        var third = service.RunAsync(state, false);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bounded(second));
        reader.Gate.Release(int.MaxValue);

        Assert.NotNull(await Bounded(third));
        await Bounded(first);
        Assert.Equal(2 * TargetCount, reader.MarkerCalls);   // the follow-up still ran to the end
    }

    [Fact]
    public async Task Cancelling_the_caller_of_the_run_in_progress_does_not_cancel_it()
    {
        reader.Gate = new SemaphoreSlim(0);
        using var cancel = new CancellationTokenSource();

        var first = service.RunAsync(state, false, null, cancel.Token);
        var sharer = service.RunAsync(state, false);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bounded(first));
        reader.Gate.Release(int.MaxValue);

        Assert.NotNull(await Bounded(sharer));
        Assert.Equal(2 * TargetCount, reader.MarkerCalls);
    }

    [Fact]
    public async Task Changing_state_during_a_run_does_not_affect_it()
    {
        // A pattern-excluded Package with airport records shows up in Excluded; removing the default pattern would hide it.
        Package("lib", "neofly-x");
        reader.Airports["neofly-x"] = [new AirportRecord(Code("EHAM"), "Schiphol", null)];
        reader.Gate = new SemaphoreSlim(0);

        var run = service.RunAsync(state, false);
        state.Content.RemoveExclusionPattern("neofly-*");
        state.Content.AddStopWord("X");
        state.Libraries.Clear();
        reader.Gate.Release(int.MaxValue);
        var outcome = await Bounded(run);

        Assert.Contains("neofly-x", outcome.Analysis.Excluded.Select(c => c.Name.Value));
        Assert.Contains("p1", Names(outcome));
    }

    [Fact]
    public async Task Changing_state_after_queueing_does_not_affect_the_queued_run()
    {
        Package("libB", "only-in-b");
        var stateB = StateWith(LibPath("libB"));
        reader.Gate = new SemaphoreSlim(0);

        var first = service.RunAsync(state, false);
        var third = service.RunAsync(stateB, false);
        stateB.Libraries.Clear();
        reader.Gate.Release(int.MaxValue);
        await Bounded(first);

        Assert.Contains("only-in-b", Names(await Bounded(third)));
    }

    [Fact]
    public async Task A_faulted_run_releases_the_slot_and_the_queued_run_still_runs()
    {
        reader.ThrowOnFirstMarker(new InvalidOperationException());
        reader.Gate = new SemaphoreSlim(0);

        var first = service.RunAsync(state, false);
        var second = service.RunAsync(state, false);
        reader.Gate.Release(int.MaxValue);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Bounded(first));
        Assert.NotNull(await Bounded(second));
        Assert.NotNull(await Bounded(service.RunAsync(state, false)));
    }

    [Fact]
    public async Task A_faulted_run_faults_every_caller_that_shares_it()
    {
        reader.Gate = new SemaphoreSlim(0);
        var first = service.RunAsync(state, false);
        var second = service.RunAsync(state, false);
        // The queued run fails on its first marker: the last caller's callback arms the failure when discovery is done.
        var third = service.RunAsync(state, false, new ScanCallbacks(OnDiscovered: _ => reader.ThrowOnFirstMarker(new InvalidOperationException())));
        reader.Gate.Release(int.MaxValue);

        await Bounded(first);
        Assert.Same(second, third);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bounded(second));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bounded(third));
    }

    [Fact]
    public async Task A_has_bgl_io_error_does_not_fail_discovery()
    {
        var x = Package("lib", "x");
        reader.HasBglThrows["x"] = new IOException();
        Package("lib", "y");
        reader.HasBglThrows["y"] = new UnauthorizedAccessException();

        var d = (await Bounded(service.RunAsync(state, false))).Discovery;

        // null (unknown) keeps Scenery.
        Assert.Equal(Category.Scenery, d.ProvisionalCategories[Key(x)]);
        Assert.Equal(Category.Scenery, d.ProvisionalCategories[Key(Path.Combine(LibPath("lib"), "y"))]);
    }

    [Fact]
    public async Task Cached_analysis_is_reported_before_discovery()
    {
        Package("lib", "ehamscen");
        reader.Airports["ehamscen"] = [new AirportRecord(Code("EHAM"), "Schiphol", null)];

        ContentAnalysis? firstCached = null;
        await Bounded(service.RunAsync(state, false, new ScanCallbacks(OnCached: (_, a) => firstCached = a)));
        Assert.NotNull(firstCached);
        Assert.Empty(firstCached.Airports);   // nothing cached yet

        var seq = 0;
        int cachedAt = 0, discoveredAt = 0;
        ContentAnalysis? cached = null;
        IReadOnlyList<PackageContent>? cachedContents = null;
        await Bounded(service.RunAsync(state, false, new ScanCallbacks(
            OnCached: (c, a) => { cachedContents = c; cached = a; cachedAt = ++seq; },
            OnDiscovered: _ => discoveredAt = ++seq)));

        Assert.True(cachedAt > 0 && cachedAt < discoveredAt);
        Assert.True(cached!.Airports.ContainsKey(Code("EHAM")));
        Assert.Contains(cachedContents!, c => c.Name.Value == "ehamscen");
    }

    [Fact]
    public async Task Cached_analysis_uses_the_snapshot_settings()
    {
        Package("lib", "neofly-x");
        reader.Airports["neofly-x"] = [new AirportRecord(Code("EHAM"), "Schiphol", null)];
        await Bounded(service.RunAsync(state, false));

        ContentAnalysis? cached = null;
        state.Content.RemoveExclusionPattern("neofly-*");
        await Bounded(service.RunAsync(state, false, new ScanCallbacks(OnCached: (_, a) => cached = a)));

        Assert.Empty(cached!.Excluded);
    }

    [Fact]
    public async Task Provisional_categories_skip_sim_managed_targets()
    {
        reader.HasBglResult["asobo-sim"] = false;

        var outcome = await Bounded(service.RunAsync(state, false));
        var d = outcome.Discovery;

        Assert.Equal(0, reader.HasBglCallsFor(simPackage));
        Assert.False(d.ProvisionalCategories.ContainsKey(Key(simPackage)));
        Assert.Contains("asobo-sim", Names(outcome));
        Assert.Contains(d.Targets, t => t.Source == PackageSource.SimManaged);
    }

    [Fact]
    public async Task Unreachable_library_content_is_kept()
    {
        Package("libB", "offline-pkg");
        var both = StateWith(LibPath("lib"), LibPath("libB"));
        await Bounded(service.RunAsync(both, false));
        Directory.Delete(LibPath("libB"), recursive: true);

        var kept = await Bounded(service.RunAsync(both, false));
        Assert.Contains("offline-pkg", Names(kept));
        Assert.Equal([new LibraryRoot(LibPath("libB"), LibraryRoot.AllSections)], kept.Discovery.Index.Unreachable);

        // Once the Library is no longer registered its content goes.
        var gone = await Bounded(service.RunAsync(StateWith(LibPath("lib")), false));
        Assert.DoesNotContain("offline-pkg", Names(gone));
        Assert.DoesNotContain(cache.LoadAll(), c => c.Name.Value == "offline-pkg");
    }

    [Fact]
    public async Task Content_of_a_reachable_library_that_vanished_is_not_kept()
    {
        var gone = Package("lib", "doomed");
        await Bounded(service.RunAsync(state, false));
        Directory.Delete(gone, recursive: true);

        var outcome = await Bounded(service.RunAsync(state, false));

        Assert.DoesNotContain("doomed", Names(outcome));
    }

    // Every user row, as text, so the test sees whether a run changed what a save would write.
    private static string Dump(UserDataDatabase db)
        => db.Read(c =>
        {
            var text = new System.Text.StringBuilder();
            foreach (var table in new[] { "library", "card", "card_code", "package", "pending", "snapshot_entry", "setting" })
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT * FROM {table}";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    text.Append(table).Append(':').AppendJoin('|', Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i))).AppendLine();
            }

            return text.ToString();
        });

    [Fact]
    public async Task User_state_is_not_saved_and_community_is_unchanged()
    {
        File.WriteAllText(Path.Combine(community, "stray.txt"), "x");
        Directory.CreateDirectory(Path.Combine(community, "fixed-pkg"));
        File.WriteAllText(Path.Combine(community, "fixed-pkg", "manifest.json"), "{}");
        new JunctionFileSystem().CreateJunction(Path.Combine(community, "p1"), Path.Combine(LibPath("lib"), "p1"));
        using var userData = UserDataDatabase.Open(Path.Combine(temp.Root, "state.db"));
        var store = new SqliteUserStateStore(userData);
        store.Save(state);
        var stateBefore = Dump(userData);
        var communityBefore = Describe(community);

        await Bounded(service.RunAsync(state, true));

        store.Save(state);
        Assert.Equal(stateBefore, Dump(userData));
        Assert.Null(state.Snapshot);
        Assert.Empty(state.Packages);
        Assert.Equal(communityBefore, Describe(community));
    }

    [Fact]
    public async Task Community_entries_are_scanned_before_library_packages()
    {
        Directory.CreateDirectory(Path.Combine(community, "fixed-pkg"));
        File.WriteAllText(Path.Combine(community, "fixed-pkg", "manifest.json"), "{\"content_type\":\"SCENERY\"}");

        var d = (await Bounded(service.RunAsync(state, false))).Discovery;

        Assert.Equal("fixed-pkg", d.Targets[0].Name.Value);
        Assert.Contains(Key(Path.Combine(community, "fixed-pkg")), d.ProvisionalCategories.Keys);
    }

    [Fact]
    public async Task Elapsed_covers_the_whole_run()
    {
        var outcome = await Bounded(service.RunAsync(state, false));

        Assert.True(outcome.Elapsed > TimeSpan.Zero);
    }

    [Fact]
    public async Task Progress_is_reported_for_every_target()
    {
        var reports = new List<ScanProgress>();
        var progress = new SynchronousProgress<ScanProgress>(reports.Add);

        await Bounded(service.RunAsync(state, false, new ScanCallbacks(Progress: progress)));

        Assert.Equal(TargetCount, reports.Count);
        Assert.Equal(TargetCount, reports[^1].Done);
    }

    [Fact]
    public async Task Dispose_cancels_the_run_in_progress_and_the_queued_one()
    {
        reader.Gate = new SemaphoreSlim(0);
        var first = service.RunAsync(state, false);
        var second = service.RunAsync(state, false);

        service.Dispose();
        reader.Gate.Release(int.MaxValue);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bounded(first));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bounded(second));
        Assert.True(reader.MarkerCalls <= 1);
    }

    [Fact]
    public void A_call_after_dispose_throws()
    {
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = service.RunAsync(state, false); });
    }

    [Fact]
    public async Task A_link_into_a_folder_discovery_could_not_list_is_offline_in_the_outcome()
    {
        var hidden = Package(@"lib\closed", "hidden");
        new JunctionFileSystem().CreateJunction(Path.Combine(community, "hidden"), hidden);
        var closed = new DirectoryInfo(Path.GetDirectoryName(hidden)!);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            user,
            System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        var security = closed.GetAccessControl();
        security.AddAccessRule(deny);
        closed.SetAccessControl(security);

        try
        {
            var outcome = await Bounded(service.RunAsync(state, false));

            Assert.Equal([Key(closed.FullName)], outcome.Discovery.Inaccessible);
            Assert.Equal(EntryClass.Offline, outcome.Discovery.Community.Single(e => e.Entry.Name == "hidden").Class);
        }
        finally
        {
            security = closed.GetAccessControl();
            security.RemoveAccessRule(deny);
            closed.SetAccessControl(security);
        }
    }

    private static string Describe(string folder)
        => string.Join("\n", Directory.EnumerateFileSystemEntries(folder, "*", new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = 0 })
            .Order()
            .Select(p => $"{p}|{new FileInfo(p).Attributes}|{File.GetLastWriteTimeUtc(p).Ticks}"));

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
