using AddonManager.Core.Community;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Infrastructure.Tests;

public sealed class JsonUserStateStoreTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly string dir;
    private readonly string file;
    private readonly JsonUserStateStore store;

    public JsonUserStateStoreTests()
    {
        dir = temp.Root;
        file = Path.Combine(dir, "userdata.json");
        store = new JsonUserStateStore(file);
    }

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Roundtrip_keeps_packages_that_are_not_currently_discovered_and_keys_ignore_case()
    {
        var state = new UserState();
        state.Packages[new("Missing-Pkg")] = new PackageState(On: true);

        store.Save(state);

        Assert.True(store.Load().Packages[new("missing-pkg")].On);
    }

    [Fact]
    public void Missing_file_loads_a_fresh_state_with_null_snapshot()
        => Assert.Null(new JsonUserStateStore(Path.Combine(dir, "none.json")).Load().Snapshot);

    [Fact]
    public void Corrupt_file_throws_and_is_left_in_place()
    {
        File.WriteAllText(file, "{ broken");

        Assert.Throws<UserStateCorruptException>(() => store.Load());
        Assert.Equal("{ broken", File.ReadAllText(file));
    }

    [Fact]
    public void Roundtrip_keeps_pending_snapshot_libraries_and_active_copy()
    {
        var state = new UserState { Libraries = [new LibraryRoot(@"C:\Sim\Neofly\addons", ManagedElsewhere: true)], Snapshot = [] };
        state.Packages[new("x")] = new PackageState(On: true, ActiveCopyPath: @"C:\Sim\Neofly\addons\x");
        state.Pending.Add(new("x"));

        store.Save(state);
        var loaded = store.Load();

        Assert.NotNull(loaded.Snapshot);
        Assert.Empty(loaded.Snapshot!);
        Assert.True(loaded.Libraries.Single().ManagedElsewhere);
        Assert.Equal(@"C:\Sim\Neofly\addons\x", loaded.Packages[new("x")].ActiveCopyPath);
        Assert.Contains(new PackageName("X"), loaded.Pending);
    }

    [Fact]
    public void Roundtrip_keeps_snapshot_entries()
    {
        var state = new UserState { Snapshot = [new SnapshotEntry("pkg", EntryKind.Junction, @"C:\lib\pkg")] };

        store.Save(state);

        Assert.Equal(state.Snapshot, store.Load().Snapshot);
    }

    [Fact]
    public void The_file_carries_a_schema_version()
    {
        store.Save(new UserState());

        Assert.Contains("\"schemaVersion\": 1", File.ReadAllText(file));
    }

    [Fact]
    public void Save_keeps_the_previous_file_as_bak()
    {
        store.Save(new UserState());
        store.Save(new UserState { Snapshot = [] });

        Assert.True(File.Exists(file + ".bak"));
        Assert.False(File.Exists(file + ".tmp"));
    }

    [Fact]
    public void Load_and_AppPaths_have_no_side_effects()
    {
        _ = new JsonUserStateStore(Path.Combine(dir, "sub", "u.json")).Load();
        _ = AppPaths.UserDataFile;
        _ = AppPaths.CacheDirectory;

        Assert.False(Directory.Exists(Path.Combine(dir, "sub")));
    }

    [Fact]
    public void AppPaths_point_into_appdata()
    {
        Assert.EndsWith(@"ModernMsfsAddonManager\userdata.json", AppPaths.UserDataFile);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), AppPaths.UserDataFile);
    }
}
