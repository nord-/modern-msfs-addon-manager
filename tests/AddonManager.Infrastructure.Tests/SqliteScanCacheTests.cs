using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using Microsoft.Data.Sqlite;

namespace AddonManager.Infrastructure.Tests;

public sealed class SqliteScanCacheTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private string DbFile => Path.Combine(_temp.Root, "userdata.db");

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

    private static void Exec(UserDataDatabase db, string sql)
        => db.Write(WriteScope.Cache, (c, t) =>
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = t;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
            return 0;
        });

    private static void ExecRaw(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string? Scalar(UserDataDatabase db, string sql)
        => db.Read(c =>
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToString(cmd.ExecuteScalar());
        });

    [Fact]
    public void Contents_survive_a_round_trip_through_the_shared_file()
    {
        using (var db = UserDataDatabase.Open(DbFile))
            new SqliteScanCache(db).Upsert([Sample()]);

        using var reopened = UserDataDatabase.Open(DbFile);
        Assert.Equivalent(Sample(), new SqliteScanCache(reopened).LoadAll().Single());
    }

    [Fact]
    public void A_type_designator_survives_the_cache()
    {
        var content = Sample() with { AircraftConfigs = [new AircraftConfig("Cessna", "Base", "C152")] };
        using (var db = UserDataDatabase.Open(DbFile))
            new SqliteScanCache(db).Upsert([content]);

        using var reopened = UserDataDatabase.Open(DbFile);
        Assert.Equal("C152", new SqliteScanCache(reopened).LoadAll().Single().AircraftConfigs.Single().TypeDesignator);
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
        using (var db = UserDataDatabase.Open(DbFile))
            new SqliteScanCache(db).Upsert([content]);

        using var reopened = UserDataDatabase.Open(DbFile);
        Assert.Equivalent(content, new SqliteScanCache(reopened).LoadAll().Single());
    }

    [Fact]
    public void Upsert_replaces_by_path_case_insensitively()
    {
        using var db = UserDataDatabase.Open(DbFile);
        var cache = new SqliteScanCache(db);
        cache.Upsert([Sample(@"C:\Sim\Addons\Acme-ESGG", "Old")]);
        cache.Upsert([Sample(@"c:\sim\addons\acme-esgg", "New")]);

        Assert.Equal("New", Assert.Single(cache.LoadAll()).Manifest.Title);
    }

    [Fact]
    public void Paths_that_differ_only_in_the_case_of_a_non_ascii_letter_are_one_row()
    {
        using var db = UserDataDatabase.Open(DbFile);
        var cache = new SqliteScanCache(db);
        cache.Upsert([Sample(@"C:\Flygsim\Övrigt\pkg", "Old")]);
        cache.Upsert([Sample(@"C:\Flygsim\övrigt\pkg", "New")]);
        Assert.Equal("New", Assert.Single(cache.LoadAll()).Manifest.Title);

        cache.Remove([@"C:\Flygsim\Övrigt\pkg"]);
        Assert.Empty(cache.LoadAll());
    }

    [Fact]
    public void Remove_deletes_rows()
    {
        using var db = UserDataDatabase.Open(DbFile);
        var cache = new SqliteScanCache(db);
        cache.Upsert([Sample(@"c:\a"), Sample(@"c:\b")]);
        cache.Remove([@"C:\A"]);

        Assert.Equal(@"c:\b", Assert.Single(cache.LoadAll()).Path);
    }

    [Fact]
    public void A_row_that_cannot_be_read_rebuilds_the_cache_and_keeps_user_rows()
    {
        using var db = UserDataDatabase.Open(DbFile);
        db.Write(WriteScope.User, (c, t) =>
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = t;
            cmd.CommandText = "INSERT INTO card(id, section, tab, created) VALUES (1, 'Scenery', 'A', 'now')";
            cmd.ExecuteNonQuery();
            return 0;
        });
        Exec(db, "INSERT INTO scan_package(key, content) VALUES ('K', '{}')");

        Assert.Empty(new SqliteScanCache(db).LoadAll());
        Assert.Equal("0", Scalar(db, "SELECT count(*) FROM scan_package"));
        Assert.Equal("1", Scalar(db, "SELECT count(*) FROM card"));
        Assert.True(File.Exists(DbFile));
    }

    [Fact]
    public void A_locked_file_fails_the_write_and_never_falls_back_to_memory()
    {
        using var db = UserDataDatabase.Open(DbFile);
        var cache = new SqliteScanCache(db);

        using (var other = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbFile, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            other.Open();
            ExecRaw(other, "BEGIN IMMEDIATE");
            Assert.Throws<UserDataLockedException>(() => cache.Upsert([Sample()]));
            ExecRaw(other, "ROLLBACK");
        }

        Assert.Empty(cache.LoadAll());
    }

    [Fact]
    public void The_database_file_lives_under_local_app_data()
    {
        Assert.EndsWith(@"ModernMsfsAddonManager\userdata.db", AppPaths.DatabaseFile);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), AppPaths.DatabaseFile);
        Assert.Equal(Path.GetDirectoryName(AppPaths.DatabaseFile), AppPaths.DataDirectory);
    }

    [Theory]
    [InlineData("UPDATE scan_package SET content = '{'")]
    [InlineData("UPDATE scan_package SET content = '{}'")]
    [InlineData("UPDATE scan_package SET content = replace(content, '\"Name\":\"acme-esgg\"', '\"Name\":\"\"')")]
    [InlineData("UPDATE scan_package SET content = '{\"Name\":\"x\",\"Path\":\"C:\\\\x\"}'")]
    [InlineData("UPDATE scan_package SET content = json_set(content, '$.Airports', json('null'))")]
    [InlineData("UPDATE scan_package SET content = json_remove(content, '$.AircraftConfigs')")]
    [InlineData("UPDATE scan_package SET content = json_set(content, '$.Marker', json('null'))")]
    [InlineData("UPDATE scan_package SET content = json_set(content, '$.Manifest.Dependencies', json('null'))")]
    [InlineData("UPDATE scan_package SET content = json_set(content, '$.Airports', json('[null]'))")]
    [InlineData("UPDATE scan_package SET content = json_set(content, '$.AircraftConfigs', json('[null]'))")]
    [InlineData("UPDATE scan_package SET content = json_set(content, '$.AirplaneFolders', json('[null]'))")]
    [InlineData("UPDATE scan_package SET content = json_set(content, '$.Manifest.Dependencies', json('[null]'))")]
    public void Row_that_is_not_valid_content_rebuilds_the_cache(string sql)
    {
        using var db = UserDataDatabase.Open(DbFile);
        var cache = new SqliteScanCache(db);
        cache.Upsert([Sample()]);
        Exec(db, sql);

        Assert.Empty(cache.LoadAll());
        Assert.Equal("0", Scalar(db, "SELECT count(*) FROM scan_package"));
    }
}
