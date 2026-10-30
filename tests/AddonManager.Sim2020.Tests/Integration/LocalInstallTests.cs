using System.Diagnostics;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Infrastructure;
using AddonManager.Sim2020.Content;
using Xunit.Abstractions;

namespace AddonManager.Sim2020.Tests.Integration;

/// <summary>
/// Tests against the real MSFS 2020 install on this machine. They run only with MMAM_SIM_TESTS=1 and read the real
/// Official folder, Libraries and Community (read-only). Scan caches live in a temp folder, never in AppPaths.
/// </summary>
public sealed class LocalInstallTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    [SimFact]
    public async Task Real_eham_package_reads_eham_near_schiphol()
    {
        var outcome = await ScanAsync();
        var package = outcome.Content.Contents.FirstOrDefault(c => IsNamed(c, "brickbybrickdesign-airport-eham-amsterdam"));
        Assert.True(package is not null, "Package 'brickbybrickdesign-airport-eham-amsterdam' was not found in the scanned Libraries or Community.");

        var eham = package.Airports.FirstOrDefault(a => a.Code.Value == "EHAM");
        Assert.True(eham is not null, "The Package has no EHAM airport record.");
        output.WriteLine($"EHAM: name '{eham.Name}', position {eham.Position}, {package.Airports.Count} airport records, {package.UnreadableBglCount} unreadable BGL files");
        Assert.NotNull(eham.Name);
        Assert.NotNull(eham.Position);
        Assert.InRange(eham.Position.Value.Latitude, 52.25, 52.36);
    }

    [SimFact]
    public async Task Real_generic_airports_hold_about_forty_thousand_codes()
    {
        var official = RequireInstall().OfficialPath;
        Assert.True(official is not null, "The install has no Official folder.");
        var contents = await ReadOfficialAsync(official, "fs-base-genericairports");

        var count = contents.Single().Airports.Count;
        output.WriteLine($"fs-base-genericairports: {count} airport records");
        Assert.InRange(count, 35_000, 45_000);
    }

    [SimFact]
    public async Task Official_airport_keys_resolve_to_names()
    {
        var official = RequireInstall().OfficialPath;
        Assert.True(official is not null, "The install has no Official folder.");

        var lowi = (await ReadOfficialAsync(official, "asobo-airport-lowi-innsbruck")).Single().Airports.First(a => a.Code.Value == "LOWI");
        var cytz = (await ReadOfficialAsync(official, "asobo-airport-cytz-billy-bishop")).Single().Airports.First(a => a.Code.Value == "CYTZ");
        output.WriteLine($"LOWI: '{lowi.Name}', CYTZ: '{cytz.Name}'");
        Assert.Equal("Innsbruck", lowi.Name);
        Assert.Equal("Toronto/City Centre", cytz.Name);
    }

    [SimFact]
    public async Task Incremental_scan_without_changes_takes_under_two_seconds()
    {
        var (paths, state) = Prepare();
        var cacheFile = Path.Combine(temp.Root, "userdata.db");

        var first = await RunAsync(paths, cacheFile, state, full: true);
        output.WriteLine($"full: {first.Elapsed}, read {first.Content.Read}, reused {first.Content.Reused}, failed {first.Content.FailedCount}, {first.Content.Contents.Count} Packages");

        // A Package without a fixed section is always read again, so give every scanned Package one first.
        foreach (var content in first.Content.Contents)
            state.Packages[content.Name] = (state.Packages.GetValueOrDefault(content.Name) ?? new PackageState(false)) with { Section = Category.Scenery };

        var second = await RunAsync(paths, cacheFile, state, full: false);
        output.WriteLine($"incremental: {second.Elapsed}, read {second.Content.Read}, reused {second.Content.Reused}, failed {second.Content.FailedCount}");
        Assert.True(second.Elapsed < TimeSpan.FromSeconds(2), $"Incremental scan took {second.Elapsed}.");
    }

    [SimFact]
    public async Task Cached_startup_load_and_analysis_takes_under_one_second()
    {
        var (paths, state) = Prepare();
        var cacheFile = Path.Combine(temp.Root, "userdata.db");

        var first = await RunAsync(paths, cacheFile, state, full: true);
        output.WriteLine($"full: {first.Elapsed}, {first.Content.Contents.Count} Packages");

        // A fresh service and cache instance, as on app start.
        using var db = UserDataDatabase.Open(cacheFile);
        using var service = NewService(paths, new SqliteScanCache(db));
        var stopwatch = Stopwatch.StartNew();
        TimeSpan? cached = null;
        var run = service.RunAsync(state, full: false, new ScanCallbacks(OnCached: (_, _) => cached = stopwatch.Elapsed));
        await run;

        Assert.True(cached is not null, "OnCached never fired.");
        output.WriteLine($"cache load and analysis until OnCached: {cached}");
        Assert.True(cached < TimeSpan.FromSeconds(1), $"Cached startup took {cached}.");
    }

    private static bool IsNamed(PackageContent content, string name)
        => string.Equals(content.Name.Value, name, StringComparison.OrdinalIgnoreCase);

    private static SimPaths RequireInstall()
    {
        var paths = InstallLocator.Locate(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify));
        Assert.True(paths is not null, $"{SimFactAttribute.EnvironmentVariable}=1 is set but no MSFS 2020 install was found (no UserCfg.opt with InstalledPackagesPath).");
        return paths;
    }

    /// <summary>The install plus the saved user data, with Libraries from MSFS_LIBRARIES when set, else the stored ones. Read-only: nothing is saved.</summary>
    private static (SimPaths Paths, UserState State) Prepare()
    {
        var paths = RequireInstall();
        var state = LoadStoredState();
        var fromEnvironment = SimFactAttribute.EnvironmentLibraries();
        if (fromEnvironment.Count > 0)
            state.Libraries = fromEnvironment;

        return (paths, state);
    }

    private static UserState LoadStoredState()
    {
        if (!File.Exists(AppPaths.DatabaseFile))
            return new UserState();

        using var db = UserDataDatabase.Open(AppPaths.DatabaseFile, UserDataAccess.CacheOnly);
        return new SqliteUserStateStore(db).Load();
    }

    private static ScanService NewService(SimPaths paths, IScanCache cache)
        => new(new PackageContentReader(new AirportNames(paths.OfficialPath)), cache, paths.CommunityPath, new OfficialPackages(paths.OfficialPath));

    /// <summary>One run with a new cache and service instance on <paramref name="cacheFile"/>.</summary>
    private static async Task<ScanOutcome> RunAsync(SimPaths paths, string cacheFile, UserState state, bool full)
    {
        using var db = UserDataDatabase.Open(cacheFile);
        using var service = NewService(paths, new SqliteScanCache(db));
        return await service.RunAsync(state, full);
    }

    private async Task<ScanOutcome> ScanAsync()
    {
        var (paths, state) = Prepare();
        var outcome = await RunAsync(paths, Path.Combine(temp.Root, "userdata.db"), state, full: true);
        output.WriteLine($"scan: {outcome.Elapsed}, {outcome.Content.Contents.Count} Packages");
        return outcome;
    }

    /// <summary>Reads the named Official Packages directly, without a scan.</summary>
    private Task<List<PackageContent>> ReadOfficialAsync(string officialPath, string packageName)
    {
        var reader = new PackageContentReader(new AirportNames(officialPath));
        var folder = Path.Combine(officialPath, packageName);
        Assert.True(Directory.Exists(folder), $"Official Package '{packageName}' was not found in {officialPath}.");
        var stopwatch = Stopwatch.StartNew();

        var marker = reader.ReadMarker(folder);
        Assert.True(marker is not null, $"'{packageName}' has no manifest.json.");
        var manifest = ManifestReader.Read(Path.Combine(folder, "manifest.json"));
        var content = reader.Read(new ScanTarget(new PackageName(packageName), folder, PackageSource.SimManaged, manifest), marker);
        Assert.True(content is not null, $"'{packageName}' could not be read.");
        output.WriteLine($"read {packageName} in {stopwatch.Elapsed}");
        return Task.FromResult(new List<PackageContent> { content });
    }
}
