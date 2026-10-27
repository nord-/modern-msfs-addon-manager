using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using Microsoft.Data.Sqlite;

namespace AddonManager.Infrastructure.Tests;

public sealed class SqliteScanCacheTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private string File_ => Path.Combine(_temp.Root, "scan-cache.db");

    public void Dispose() => _temp.Dispose();

    private static PackageContent Sample(string path = @"c:\sim\addons\acme-esgg", string title = "Gothenburg")
        => new(
            new PackageName("acme-esgg"),
            path,
            PackageSource.Library,
            new ChangeMarker(123, 456, 789, 1011),
            new Manifest(title, "Acme", "SCENERY", "1.2.3", ["dep-one", "dep-two"]),
            true,
            [1u, 3u, 42u],
            2,
            [
                new AirportRecord(Code("ESGG"), "Landvetter", new GeoPoint(57.66, 12.28)),
                new AirportRecord(Code("ESGP"), null, null),
            ],
            true,
            [new AircraftConfig("Cessna", "Base"), new AircraftConfig("Piper", null)],
            ["Cessna", "Piper"],
            ["Piper"],
            @"ContentInfo\x\Thumbnail.jpg");

    private static AirportCode Code(string text)
    {
        Assert.True(AirportCode.TryParse(text, out var code));
        return code;
    }

    private static void Exec(string file, string sql)
    {
        using var db = Open(file);
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string file)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    [Fact]
    public void Round_trips_content_including_airports_and_aircraft()
    {
        using (var cache = new SqliteScanCache(File_))
            cache.Upsert([Sample()]);

        using var reopened = new SqliteScanCache(File_);
        Assert.Equivalent(Sample(), reopened.LoadAll().Single());
    }

    [Fact]
    public void Round_trips_content_whose_optional_members_are_null()
    {
        var content = Sample() with
        {
            Marker = new ChangeMarker(1, 2, null, null),
            Manifest = Manifest.Empty,
            ThumbnailPath = null,
        };
        using (var cache = new SqliteScanCache(File_))
            cache.Upsert([content]);

        using var reopened = new SqliteScanCache(File_);
        Assert.Equivalent(content, reopened.LoadAll().Single());
        Assert.False(reopened.IsFallback);
    }

    [Fact]
    public void Upsert_replaces_by_path_case_insensitively()
    {
        using var cache = new SqliteScanCache(File_);
        cache.Upsert([Sample(@"C:\Sim\Addons\Acme-ESGG", "Old")]);
        cache.Upsert([Sample(@"c:\sim\addons\acme-esgg", "New")]);

        var loaded = Assert.Single(cache.LoadAll());
        Assert.Equal("New", loaded.Manifest.Title);
    }

    [Fact]
    public void Paths_that_differ_only_in_the_case_of_a_non_ascii_letter_are_one_row()
    {
        using var cache = new SqliteScanCache(File_);
        cache.Upsert([Sample(@"C:\Flygsim\Övrigt\pkg", "Old")]);
        cache.Upsert([Sample(@"C:\Flygsim\övrigt\pkg", "New")]);
        Assert.Equal("New", Assert.Single(cache.LoadAll()).Manifest.Title);

        cache.Remove([@"C:\Flygsim\Övrigt\pkg"]);
        Assert.Empty(cache.LoadAll());
        using var reopened = new SqliteScanCache(File_);
        Assert.Empty(reopened.LoadAll());
    }

    [Fact]
    public void Remove_deletes_rows()
    {
        using var cache = new SqliteScanCache(File_);
        cache.Upsert([Sample(@"c:\a"), Sample(@"c:\b")]);
        cache.Remove([@"C:\A"]);

        Assert.Equal(@"c:\b", Assert.Single(cache.LoadAll()).Path);
    }

    [Fact]
    public void Constructing_creates_nothing()
    {
        _ = new SqliteScanCache(Path.Combine(_temp.Root, "sub", "c.db"));

        Assert.False(Directory.Exists(Path.Combine(_temp.Root, "sub")));
    }

    [Fact]
    public void Garbage_file_is_replaced_by_an_empty_cache()
    {
        File.WriteAllText(File_, "not a database, just some text that is long enough to be read as a header");
        using var cache = new SqliteScanCache(File_);

        Assert.Empty(cache.LoadAll());
        cache.Upsert([Sample()]);
        Assert.Single(cache.LoadAll());
        Assert.False(cache.IsFallback);
    }

    [Fact]
    public void Other_schema_version_is_rebuilt()
    {
        using (var cache = new SqliteScanCache(File_))
            cache.Upsert([Sample()]);
        Exec(File_, "PRAGMA user_version = 99");

        using var reopened = new SqliteScanCache(File_);
        Assert.Empty(reopened.LoadAll());
        Assert.False(reopened.IsFallback);
    }

    [Fact]
    public void Row_with_invalid_json_rebuilds_the_cache()
    {
        using (var cache = new SqliteScanCache(File_))
            cache.Upsert([Sample()]);
        Exec(File_, "UPDATE package SET content = '{'");

        using var reopened = new SqliteScanCache(File_);
        Assert.Empty(reopened.LoadAll());
        Assert.False(reopened.IsFallback);
    }

    [Fact]
    public void Corrupt_file_that_cannot_be_deleted_falls_back_to_memory()
    {
        using (var cache = new SqliteScanCache(File_))
            cache.Upsert([Sample()]);
        Exec(File_, "PRAGMA user_version = 99");

        using var holder = Open(File_);
        using (var cmd = holder.CreateCommand())
        {
            cmd.CommandText = "SELECT count(*) FROM package";
            cmd.ExecuteScalar();
        }

        using var c = new SqliteScanCache(File_);
        Assert.Empty(c.LoadAll());
        Assert.True(c.IsFallback);
        c.Upsert([Sample()]);
        Assert.Single(c.LoadAll());
    }

    [Fact]
    public void Locked_file_falls_back_to_memory_without_throwing()
    {
        using (var cache = new SqliteScanCache(File_))
            cache.Upsert([Sample(@"c:\other")]);

        using var holder = Open(File_);
        Exec(holder, "BEGIN EXCLUSIVE");

        using var locked = new SqliteScanCache(File_);
        locked.Upsert([Sample()]);

        Assert.True(locked.IsFallback);
        Assert.Single(locked.LoadAll());
        locked.Remove([@"c:\sim\addons\acme-esgg"]);
        Assert.Empty(locked.LoadAll());
    }

    [Theory]
    [InlineData("UPDATE package SET content = '{}'")]
    [InlineData("UPDATE package SET content = replace(content, '\"Name\":\"acme-esgg\"', '\"Name\":\"\"')")]
    [InlineData("UPDATE package SET content = '{\"Name\":\"x\",\"Path\":\"C:\\\\x\"}'")]
    [InlineData("UPDATE package SET content = json_set(content, '$.Airports', json('null'))")]
    [InlineData("UPDATE package SET content = json_remove(content, '$.AircraftConfigs')")]
    [InlineData("UPDATE package SET content = json_set(content, '$.Marker', json('null'))")]
    [InlineData("UPDATE package SET content = json_set(content, '$.Manifest.Dependencies', json('null'))")]
    [InlineData("UPDATE package SET content = json_set(content, '$.Airports', json('[null]'))")]
    [InlineData("UPDATE package SET content = json_set(content, '$.AircraftConfigs', json('[null]'))")]
    [InlineData("UPDATE package SET content = json_set(content, '$.AirplaneFolders', json('[null]'))")]
    [InlineData("UPDATE package SET content = json_set(content, '$.Manifest.Dependencies', json('[null]'))")]
    public void Row_that_is_json_but_not_valid_content_rebuilds_the_cache(string sql)
    {
        using (var cache = new SqliteScanCache(File_))
            cache.Upsert([Sample()]);
        Exec(File_, sql);

        using var reopened = new SqliteScanCache(File_);
        Assert.Empty(reopened.LoadAll());
        Assert.False(reopened.IsFallback);
    }

    [Fact]
    public void Fallback_starts_from_rows_written_and_removed_this_session()
    {
        using (var seed = new SqliteScanCache(File_))
            seed.Upsert([Sample(@"c:\gone"), Sample(@"c:\kept")]);

        using var cache = new SqliteScanCache(File_);
        Assert.Equal(2, cache.LoadAll().Count);
        cache.Upsert([Sample(@"c:\x")]);
        cache.Remove([@"c:\gone"]);

        using var holder = Open(File_);
        Exec(holder, "BEGIN EXCLUSIVE");
        cache.Upsert([Sample(@"c:\y")]);

        Assert.True(cache.IsFallback);
        Assert.Equal([@"c:\kept", @"c:\x", @"c:\y"], cache.LoadAll().Select(c => c.Path).Order().ToArray());
    }

    private static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
