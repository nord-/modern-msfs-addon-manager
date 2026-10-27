using AddonManager.Core.Community;
using AddonManager.Core.Content;
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

    private static AirportCode Code(string text)
    {
        Assert.True(AirportCode.TryParse(text, out var code));
        return code;
    }

    [Fact]
    public void Plan1_user_data_without_content_loads_defaults()
    {
        File.WriteAllText(file, """{"schemaVersion":1,"libraries":[],"packages":{}}""");

        Assert.Equal(10, store.Load().Content.AirportThreshold);
    }

    [Fact]
    public void Null_content_loads_defaults()
    {
        File.WriteAllText(file, """{"schemaVersion":1,"content":null}""");

        var content = store.Load().Content;

        Assert.Equal(10, content.AirportThreshold);
        Assert.Equal(ContentSettings.DefaultStopList.Order(StringComparer.OrdinalIgnoreCase), content.StopList);
    }

    [Fact]
    public void Null_content_collections_load_as_empty()
    {
        File.WriteAllText(file, """
            {"schemaVersion":1,"content":{
              "addedExclusionPatterns":null,"removedExclusionPatterns":null,"addedStopWords":null,"removedStopWords":null,
              "optedIn":null,"manualAirportLinks":null,"removedAirportLinks":{"pkg":null}}}
            """);

        var content = store.Load().Content;

        Assert.Empty(content.OptedIn);
        Assert.Empty(content.ManualAirportLinks);
        Assert.Empty(content.RemovedAirportLinks);
        Assert.Equal(ContentSettings.DefaultStopList.Order(StringComparer.OrdinalIgnoreCase), content.StopList);
        Assert.True(content.MatchesExclusion(new("NEOFLY-x")));
        content.AddStopWord("Extra");
        content.LinkManually(new("pkg"), Code("EHAM"));
        Assert.True(content.IsStopWord("EXTRA"));
        _ = content.Clone();
    }

    [Fact]
    public void Stop_list_changes_round_trip_on_top_of_the_defaults()
    {
        var s = new UserState();
        s.Content.AddStopWord("EXTRA");
        s.Content.RemoveStopWord("pack");
        store.Save(s);

        var list = store.Load().Content.StopList;

        Assert.Contains("EXTRA", list);
        Assert.DoesNotContain("PACK", list);
        Assert.Contains("SAVE", list);
        Assert.DoesNotContain("stopList", File.ReadAllText(file));
    }

    [Fact]
    public void Re_adding_a_removed_default_after_a_reload_ignores_case()
    {
        var s = new UserState();
        s.Content.RemoveStopWord("pack");
        store.Save(s);

        var loaded = store.Load();
        loaded.Content.AddStopWord("PACK");

        Assert.Contains("PACK", loaded.Content.StopList);
        Assert.Empty(loaded.Content.RemovedStopWords);
    }

    [Fact]
    public void Airport_links_round_trip_case_insensitively()
    {
        var s = new UserState();
        s.Content.LinkManually(new("pkg"), Code("EHAM"));
        s.Content.RemoveLink(new("other"), Code("LOWI"));
        s.Content.OptedIn.Add(new("opt"));
        store.Save(s);

        var loaded = store.Load();

        Assert.Contains(Code("eham"), loaded.Content.ManualAirportLinks[new("PKG")]);
        Assert.Contains(Code("lowi"), loaded.Content.RemovedAirportLinks[new("OTHER")]);
        Assert.Contains(new PackageName("OPT"), loaded.Content.OptedIn);
    }

    [Fact]
    public void Invalid_codes_in_user_data_are_dropped()
    {
        File.WriteAllText(file, """{"schemaVersion":1,"content":{"manualAirportLinks":{"pkg":["EHAM","x"],"bad":["y"]},"removedAirportLinks":{"r":["!"]}}}""");

        var loaded = store.Load();

        Assert.Equal([Code("EHAM")], loaded.Content.ManualAirportLinks[new("pkg")]);
        Assert.DoesNotContain(new PackageName("bad"), loaded.Content.ManualAirportLinks.Keys);
        Assert.Empty(loaded.Content.RemovedAirportLinks);
    }

    [Fact]
    public void Non_string_airport_codes_are_dropped_not_an_error()
    {
        File.WriteAllText(file, """{"schemaVersion":1,"content":{"manualAirportLinks":{"p":[5,"EHAM",{"a":1}]}}}""");

        Assert.Equal([Code("EHAM")], store.Load().Content.ManualAirportLinks[new("p")]);
    }

    [Fact]
    public void Exclusion_changes_take_effect_after_a_load()
    {
        var s = new UserState();
        s.Content.AddExclusionPattern("foo-*");
        s.Content.RemoveExclusionPattern("neofly-*");
        store.Save(s);

        var loaded = store.Load();

        Assert.True(loaded.Content.MatchesExclusion(new("foo-x")));
        Assert.False(loaded.Content.MatchesExclusion(new("neofly-x")));
        loaded.Content.AddExclusionPattern("neofly-*");
        Assert.True(loaded.Content.MatchesExclusion(new("neofly-x")));
    }

    [Fact]
    public void AppPaths_point_into_appdata()
    {
        Assert.EndsWith(@"ModernMsfsAddonManager\userdata.json", AppPaths.UserDataFile);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), AppPaths.UserDataFile);
    }
}
