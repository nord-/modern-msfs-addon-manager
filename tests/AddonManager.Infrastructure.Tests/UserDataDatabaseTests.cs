using Microsoft.Data.Sqlite;

namespace AddonManager.Infrastructure.Tests;

public sealed class UserDataDatabaseTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private string DbFile => Path.Combine(_temp.Root, "userdata.db");

    public void Dispose() => _temp.Dispose();

    private static string? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar());
    }

    private static void Exec(SqliteConnection c, SqliteTransaction? t, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SqliteConnection Raw(string file, SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = mode, Pooling = false }.ToString());
        c.Open();
        return c;
    }

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    private static UserDataDatabase OpenAt(string file, UserDataAccess access = UserDataAccess.Full, int userVersion = UserDataDatabase.UserVersion,
        IReadOnlyList<UserMigration>? migrations = null, Func<SqliteConnection, bool>? quickCheck = null)
        => UserDataDatabase.Open(file, access, userVersion, migrations ?? [], quickCheck, Short);

    [Fact]
    public void Open_creates_a_missing_folder()
    {
        var file = Path.Combine(_temp.Root, "sub", "userdata.db");
        using var db = UserDataDatabase.Open(file);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Open_creates_the_schema_with_both_versions_in_wal_mode()
    {
        using var db = UserDataDatabase.Open(DbFile);
        Assert.Equal("wal", db.Read(c => Scalar(c, "PRAGMA journal_mode")));
        Assert.Equal("1", db.Read(c => Scalar(c, "SELECT value FROM meta WHERE key = 'user_version'")));
        Assert.Equal(UserDataDatabase.CacheVersion.ToString(), db.Read(c => Scalar(c, "SELECT value FROM meta WHERE key = 'cache_version'")));
    }

    [Fact]
    public void A_new_cache_version_drops_only_the_cache_tables()
    {
        using (var db = UserDataDatabase.Open(DbFile))
        {
            db.Write(WriteScope.User, (c, t) => { Exec(c, t, "INSERT INTO card(id, section, tab, created) VALUES (1, 'Scenery', 'A', 'now')"); return 0; });
            db.Write(WriteScope.Cache, (c, t) => { Exec(c, t, "INSERT INTO scan_package(key, content) VALUES ('K', '{}')"); return 0; });
            db.Write(WriteScope.Cache, (c, t) => { Exec(c, t, $"UPDATE meta SET value = '{UserDataDatabase.CacheVersion - 1}' WHERE key = 'cache_version'"); return 0; });
        }

        using var again = UserDataDatabase.Open(DbFile);
        Assert.Equal("0", again.Read(c => Scalar(c, "SELECT count(*) FROM scan_package")));
        Assert.Equal("1", again.Read(c => Scalar(c, "SELECT count(*) FROM card")));
        Assert.Equal(UserDataDatabase.CacheVersion.ToString(), again.Read(c => Scalar(c, "SELECT value FROM meta WHERE key = 'cache_version'")));
    }

    [Fact]
    public void A_newer_user_version_stops_with_an_error_naming_the_file_and_writes_nothing()
    {
        using (var db = UserDataDatabase.Open(DbFile))
        {
            db.Write(WriteScope.User, (c, t) =>
            {
                Exec(c, t, "UPDATE meta SET value = '9' WHERE key = 'user_version'");
                Exec(c, t, "UPDATE meta SET value = '9' WHERE key = 'cache_version'");
                return 0;
            });
        }

        var before = File.ReadAllBytes(DbFile);
        var ex = Assert.Throws<UserDataTooNewException>(() => UserDataDatabase.Open(DbFile));
        Assert.Equal(DbFile, ex.FilePath);
        Assert.Contains(DbFile, ex.Message);
        Assert.Equal(before, File.ReadAllBytes(DbFile));
    }

    [Fact]
    public void A_newer_version_waiting_in_the_wal_is_refused_without_a_checkpoint()
    {
        using (UserDataDatabase.Open(DbFile))
        {
        }

        using var writer = Raw(DbFile);
        Exec(writer, null, "PRAGMA wal_autocheckpoint = 0");
        Exec(writer, null, "PRAGMA journal_mode = WAL");
        Exec(writer, null, "UPDATE meta SET value = '9' WHERE key = 'user_version'");

        var copy = Path.Combine(_temp.Root, "copy.db");
        File.Copy(DbFile, copy);
        File.Copy(DbFile + "-wal", copy + "-wal");
        var before = File.ReadAllBytes(copy);

        Assert.Throws<UserDataTooNewException>(() => UserDataDatabase.Open(copy));
        Assert.Equal(before, File.ReadAllBytes(copy));
        Assert.True(File.Exists(copy + "-wal"));
    }

    [Fact]
    public void A_write_lock_held_at_startup_stops_the_open()
    {
        using (UserDataDatabase.Open(DbFile))
        {
        }

        using var holder = Raw(DbFile);
        Exec(holder, null, "BEGIN IMMEDIATE");

        var ex = Assert.Throws<UserDataLockedException>(() => OpenAt(DbFile));
        Assert.Equal(DbFile, ex.FilePath);
        using var cacheOnly = OpenAt(DbFile, UserDataAccess.CacheOnly);
        Assert.Equal(UserDataAccess.CacheOnly, cacheOnly.Access);
    }

    [Fact]
    public void An_older_user_version_is_migrated_after_a_vacuum_into_backup()
    {
        using (var db = UserDataDatabase.Open(DbFile))
            db.Write(WriteScope.User, (c, t) => { Exec(c, t, "INSERT INTO card(id, section, tab, created) VALUES (1, 'Scenery', 'A', 'now')"); return 0; });

        IReadOnlyList<UserMigration> migrations = [new(1, (c, t) => Exec(c, t, "ALTER TABLE card ADD COLUMN x TEXT"))];
        using (var db = OpenAt(DbFile, UserDataAccess.Full, 2, migrations))
        {
            Assert.Equal("2", db.Read(c => Scalar(c, "SELECT value FROM meta WHERE key = 'user_version'")));
            Assert.Equal("1", db.Read(c => Scalar(c, "SELECT count(*) FROM pragma_table_info('card') WHERE name = 'x'")));
        }

        var backup = Assert.Single(Directory.GetFiles(_temp.Root, "userdata.v1-*.db.bak"));
        using var old = Raw(backup, SqliteOpenMode.ReadOnly);
        Assert.Equal("1", Scalar(old, "SELECT value FROM meta WHERE key = 'user_version'"));
        Assert.Equal("1", Scalar(old, "SELECT count(*) FROM card"));
    }

    [Fact]
    public void A_retried_migration_never_overwrites_an_earlier_backup()
    {
        using (UserDataDatabase.Open(DbFile))
        {
        }

        var earlier = Path.Combine(_temp.Root, "userdata.v1-20000101000000.db.bak");
        byte[] known = [1, 2, 3, 4, 5];
        File.WriteAllBytes(earlier, known);

        IReadOnlyList<UserMigration> migrations = [new(1, (c, t) => Exec(c, t, "ALTER TABLE card ADD COLUMN x TEXT"))];
        using (OpenAt(DbFile, UserDataAccess.Full, 2, migrations))
        {
        }

        Assert.Equal(known, File.ReadAllBytes(earlier));
        Assert.Equal(2, Directory.GetFiles(_temp.Root, "userdata.v1-*.db.bak").Length);
    }

    [Fact]
    public void A_file_that_is_not_a_database_is_copied_and_never_written()
    {
        var junk = Enumerable.Repeat((byte)0x5A, 4096).ToArray();
        File.WriteAllBytes(DbFile, junk);

        var ex = Assert.Throws<UserDataDamagedException>(() => UserDataDatabase.Open(DbFile));
        Assert.Equal(DbFile, ex.FilePath);
        Assert.Equal(junk, File.ReadAllBytes(DbFile));
        Assert.Equal(junk, File.ReadAllBytes(ex.Copies.Single()));
    }

    [Fact]
    public void Damage_in_the_cache_is_repaired_after_the_copy_and_user_rows_stay()
    {
        using (var db = UserDataDatabase.Open(DbFile))
        {
            db.Write(WriteScope.User, (c, t) => { Exec(c, t, "INSERT INTO card(id, section, tab, created) VALUES (1, 'Scenery', 'A', 'now')"); return 0; });
            db.Write(WriteScope.Cache, (c, t) => { Exec(c, t, "INSERT INTO scan_package(key, content) VALUES ('K', '{}')"); return 0; });
        }

        var original = File.ReadAllBytes(DbFile);
        var calls = 0;
        using var again = OpenAt(DbFile, quickCheck: _ => ++calls > 1);
        var copy = Assert.Single(Directory.GetFiles(_temp.Root, "userdata.db.damaged-*"), f => !f.EndsWith("-wal") && !f.EndsWith("-shm"));
        Assert.Equal(original, File.ReadAllBytes(copy));
        Assert.Equal("0", again.Read(c => Scalar(c, "SELECT count(*) FROM scan_package")));
        Assert.Equal("1", again.Read(c => Scalar(c, "SELECT count(*) FROM card")));
    }

    [Fact]
    public void A_repair_whose_second_check_fails_is_reported_as_damaged_with_the_copy()
    {
        using (UserDataDatabase.Open(DbFile))
        {
        }

        var original = File.ReadAllBytes(DbFile);
        var ex = Assert.Throws<UserDataDamagedException>(() => OpenAt(DbFile, quickCheck: _ => false));
        Assert.Equal(original, File.ReadAllBytes(ex.Copies.Single()));
    }

    [Fact]
    public void Corruption_found_during_the_repair_is_reported_as_damaged_with_the_copy()
    {
        using (UserDataDatabase.Open(DbFile))
        {
        }

        var calls = 0;
        var ex = Assert.Throws<UserDataDamagedException>(() => OpenAt(DbFile, quickCheck: _ => ++calls > 1 ? throw new SqliteException("corrupt", 11) : false));
        Assert.Equal(DbFile, ex.FilePath);
        Assert.Single(ex.Copies);
        Assert.True(File.Exists(ex.Copies[0]));
    }

    [Fact]
    public void A_database_without_a_meta_table_is_copied_and_never_written()
    {
        using (var foreign = Raw(DbFile, SqliteOpenMode.ReadWriteCreate))
            Exec(foreign, null, "CREATE TABLE t (a TEXT)");

        var before = File.ReadAllBytes(DbFile);
        var ex = Assert.Throws<UserDataDamagedException>(() => UserDataDatabase.Open(DbFile));
        Assert.Equal(before, File.ReadAllBytes(DbFile));
        Assert.Equal(before, File.ReadAllBytes(ex.Copies.Single()));
    }

    [Fact]
    public void A_meta_table_with_other_columns_is_treated_as_a_foreign_file()
    {
        using (var foreign = Raw(DbFile, SqliteOpenMode.ReadWriteCreate))
            Exec(foreign, null, "CREATE TABLE meta (a TEXT)");

        var before = File.ReadAllBytes(DbFile);
        var ex = Assert.Throws<UserDataDamagedException>(() => UserDataDatabase.Open(DbFile));
        Assert.Equal(before, File.ReadAllBytes(DbFile));
        Assert.Equal(before, File.ReadAllBytes(ex.Copies.Single()));
    }

    [Fact]
    public void Cache_only_access_to_an_older_user_version_asks_to_run_the_app()
    {
        using (UserDataDatabase.Open(DbFile))
        {
        }

        var ex = Assert.Throws<UserDataTooOldException>(() => OpenAt(DbFile, UserDataAccess.CacheOnly, 2));
        Assert.Contains(DbFile, ex.Message);
        Assert.Equal(DbFile, ex.FilePath);
    }

    [Fact]
    public void An_sqlite_error_that_is_neither_a_lock_nor_damage_is_reported_naming_the_file()
    {
        using (UserDataDatabase.Open(DbFile))
        {
        }

        var before = File.ReadAllBytes(DbFile);

        // SQLITE_IOERR (10) from the check: not a lock, not damage.
        var ex = Assert.Throws<UserDataUnavailableException>(() => UserDataDatabase.Open(DbFile, UserDataAccess.Full,
            UserDataDatabase.UserVersion, [], _ => throw new SqliteException("disk I/O error", 10)));

        Assert.Equal(DbFile, ex.FilePath);
        Assert.Contains(DbFile, ex.Message);
        Assert.Equal(before, File.ReadAllBytes(DbFile));
    }

    [Fact]
    public void A_write_lock_held_by_another_program_is_reported_as_locked()
    {
        using var db = OpenAt(DbFile);
        using var holder = Raw(DbFile);
        Exec(holder, null, "BEGIN IMMEDIATE");

        var ex = Assert.Throws<UserDataLockedException>(() => db.Write(WriteScope.Cache, (c, t) => 0));
        Assert.Equal(DbFile, ex.FilePath);
    }

    [Fact]
    public void A_change_scope_disposed_on_another_thread_fails_and_can_still_be_disposed_on_its_own()
    {
        using var db = OpenAt(DbFile);
        var scope = db.BeginUserChange();
        Exception? failure = null;
        var other = new Thread(() =>
        {
            try { scope.Dispose(); }
            catch (Exception ex) { failure = ex; }
        });
        other.Start();
        other.Join();

        Assert.IsType<InvalidOperationException>(failure);

        // The owner thread's dispose still ends the scope and releases the gate: a later change opens on another thread without blocking.
        scope.Dispose();
        var later = new Thread(() => db.BeginUserChange().Dispose()) { IsBackground = true };
        later.Start();
        Assert.True(later.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Cache_only_access_refuses_user_table_writes()
    {
        using var db = UserDataDatabase.Open(DbFile, UserDataAccess.CacheOnly);
        Assert.Throws<InvalidOperationException>(() => db.Write(WriteScope.User, (c, t) => 0));
        Assert.Equal(0, db.Write(WriteScope.Cache, (c, t) => 0));
    }
}
