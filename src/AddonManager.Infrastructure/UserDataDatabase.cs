using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AddonManager.Infrastructure;

/// <summary>What the caller may do with the file. The dev CLI uses <see cref="CacheOnly"/>.</summary>
public enum UserDataAccess { Full, CacheOnly }

/// <summary>Which tables a write touches.</summary>
public enum WriteScope { Cache, User }

/// <summary>One step from user data version <paramref name="From"/> to the next, run inside the migration transaction.</summary>
internal sealed record UserMigration(int From, Action<SqliteConnection, SqliteTransaction> Apply);

/// <summary>
/// The one SQLite file holding the user's data and the scan cache. One connection, one writer in the process.
/// <see cref="Open(string, UserDataAccess)"/> runs <c>PRAGMA quick_check</c>, so call it off the UI thread.
/// </summary>
public sealed class UserDataDatabase : IDisposable
{
    public const int UserVersion = 1;
    public const int CacheVersion = 4;

    private const int SqliteError = 1;
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADb = 26;

    private static readonly TimeSpan DefaultBusyTimeout = TimeSpan.FromSeconds(2);

    private readonly SqliteConnection _db;
    private readonly Lock _gate = new();
    private Change? _change; // guarded by _gate

    private UserDataDatabase(string filePath, UserDataAccess access, SqliteConnection db)
    {
        FilePath = filePath;
        Access = access;
        _db = db;
    }

    public string FilePath { get; }

    public UserDataAccess Access { get; }

    /// <summary>Opens or creates the file and runs <c>PRAGMA quick_check</c>, so call it off the UI thread.</summary>
    public static UserDataDatabase Open(string filePath, UserDataAccess access = UserDataAccess.Full)
        => Open(filePath, access, UserVersion, [], null, null);

    /// <summary>
    /// As the public overload, with the version, migrations and check injectable. Runs the check, so call it off the UI thread.
    /// <paramref name="busyTimeout"/> is rounded up to whole seconds (minimum 1): the provider sets the busy handler from the command timeout.
    /// </summary>
    internal static UserDataDatabase Open(string filePath, UserDataAccess access, int userVersion,
        IReadOnlyList<UserMigration> migrations, Func<SqliteConnection, bool>? quickCheck = null, TimeSpan? busyTimeout = null)
    {
        var file = Path.GetFullPath(filePath);
        var timeout = busyTimeout ?? DefaultBusyTimeout;
        var check = quickCheck ?? DefaultQuickCheck;
        var copies = new List<string>();
        var repair = false;
        var isNew = true;
        var fileCacheVersion = 0;

        SqliteConnection? db = null;
        try
        {
            if (File.Exists(file))
            {
                // Steps 1-3 on a read-only connection: it never checkpoints the -wal on close, so nothing is written.
                using var ro = Connect(file, SqliteOpenMode.ReadOnly, timeout);
                try
                {
                    repair = !check(ro);
                }
                catch (SqliteException ex) when (IsDamage(ex))
                {
                    repair = true;
                }

                if (repair)
                    CopyDamaged(file, copies);

                try
                {
                    var tables = Column(ro, "SELECT name FROM sqlite_master WHERE type = 'table'");
                    if (tables.Count > 0)
                    {
                        if (!tables.Contains("meta"))
                            throw Damaged(file, copies, "It is not a user data file.");

                        var fileVersion = ReadMeta(ro, "user_version") ?? throw Damaged(file, copies, "Its user data version is missing.");
                        if (fileVersion > userVersion)
                            throw new UserDataTooNewException(file, fileVersion);

                        isNew = false;
                        fileCacheVersion = ReadMeta(ro, "cache_version") ?? 0;
                    }
                }
                catch (SqliteException ex) when (IsDamage(ex))
                {
                    throw Damaged(file, copies);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            db = Connect(file, SqliteOpenMode.ReadWriteCreate, timeout);

            // Step 4: another program holding the write lock stops a full open at startup.
            if (access == UserDataAccess.Full)
            {
                Exec(db, null, "BEGIN IMMEDIATE");
                Exec(db, null, "ROLLBACK");
            }

            // Step 5: writes may start.
            Exec(db, null, "PRAGMA journal_mode = WAL");
            Exec(db, null, "PRAGMA foreign_keys = ON");
            if (isNew)
                CreateSchema(db, userVersion);

            if (isNew || repair || fileCacheVersion != CacheVersion)
                ResetCache(db);

            if (repair && !check(db))
                throw Damaged(file, copies);

            // Step 6: migrate an older file.
            var current = ReadMeta(db, "user_version")!.Value;
            if (current < userVersion)
            {
                if (access == UserDataAccess.CacheOnly)
                    throw new UserDataTooOldException(file);

                Migrate(db, file, current, userVersion, migrations);
            }

            return new UserDataDatabase(file, access, db);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteBusy or SqliteLocked)
        {
            db?.Dispose();
            throw new UserDataLockedException(file);
        }
        catch (SqliteException ex) when (IsDamage(ex))
        {
            // Damage found after the check passed (or while repairing): copy the file unless that is done already.
            db?.Dispose();
            if (!repair && File.Exists(file))
                CopyDamaged(file, copies);
            throw Damaged(file, copies);
        }
        catch (SqliteException ex)
        {
            // Read-only, disk full, I/O error and the like: name the file instead of a bare provider message.
            db?.Dispose();
            throw new UserDataUnavailableException(file, ex.Message, ex);
        }
        catch
        {
            db?.Dispose();
            throw;
        }
    }

    /// <summary>Runs <paramref name="work"/> on the connection; it must not write.</summary>
    internal T Read<T>(Func<SqliteConnection, T> work)
    {
        lock (_gate)
        {
            try
            {
                return work(_db);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteBusy or SqliteLocked)
            {
                throw new UserDataLockedException(FilePath);
            }
        }
    }

    /// <summary>
    /// Takes the private lock and the SQLite write lock (<c>BEGIN IMMEDIATE</c>) now and holds both until the returned scope is disposed, so a
    /// change that saves is refused up front when another program holds the file. Re-entrant on the same thread: a nested scope joins the
    /// outer one. A write inside it uses the scope's transaction and does not commit; disposing the outermost scope commits when a write ran
    /// (and none failed) and rolls back otherwise. Thread-affine: dispose it on the thread that opened it, and never await inside it.
    /// </summary>
    internal IDisposable BeginUserChange()
    {
        if (Access == UserDataAccess.CacheOnly)
            throw new InvalidOperationException("This file was opened for the scan cache only.");

        _gate.Enter();
        try
        {
            if (_change is null)
            {
                try
                {
                    _change = new Change(_db.BeginTransaction(deferred: false));
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteBusy or SqliteLocked)
                {
                    throw new UserDataLockedException(FilePath);
                }
            }

            _change.Depth++;
            return new ChangeHandle(this);
        }
        catch
        {
            _gate.Exit();
            throw;
        }
    }

    /// <summary>A change scope ends only on the thread that opened it: the private lock is thread-affine, so ending it elsewhere must fail before anything commits.</summary>
    private void RequireOwnerThread()
    {
        if (!_gate.IsHeldByCurrentThread)
            throw new InvalidOperationException("A user change scope must be disposed on the thread that opened it.");
    }

    private void EndUserChange()
    {
        RequireOwnerThread();

        // Runs under the private lock taken by BeginUserChange.
        try
        {
            var change = _change!;
            if (--change.Depth > 0)
                return;

            _change = null;
            try
            {
                if (change.Wrote && !change.Failed)
                    change.Tx.Commit();
                else
                    change.Tx.Rollback();
            }
            finally
            {
                change.Tx.Dispose();
            }
        }
        finally
        {
            _gate.Exit();
        }
    }

    private sealed class Change(SqliteTransaction tx)
    {
        public SqliteTransaction Tx { get; } = tx;

        public int Depth { get; set; }

        public bool Wrote { get; set; }

        public bool Failed { get; set; }
    }

    private sealed class ChangeHandle(UserDataDatabase owner) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
                return;
            // Checked before the scope is marked done, so the owner thread can still dispose it after a foreign thread failed to.
            owner.RequireOwnerThread();
            _done = true;
            owner.EndUserChange();
        }
    }

    /// <summary>Runs <paramref name="work"/> in one immediate transaction and commits it, or inside the open change scope's transaction.</summary>
    internal T Write<T>(WriteScope scope, Func<SqliteConnection, SqliteTransaction, T> work)
    {
        if (scope == WriteScope.User && Access == UserDataAccess.CacheOnly)
            throw new InvalidOperationException("This file was opened for the scan cache only.");

        lock (_gate)
        {
            if (_change is { } change)
            {
                // SQLite has no nested transactions: the scope's one decides. A failed write may be half done, so the scope then rolls back.
                try
                {
                    var inner = work(_db, change.Tx);
                    change.Wrote = true;
                    return inner;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteBusy or SqliteLocked)
                {
                    change.Failed = true;
                    throw new UserDataLockedException(FilePath);
                }
                catch
                {
                    change.Failed = true;
                    throw;
                }
            }

            try
            {
                using var tx = _db.BeginTransaction(deferred: false);
                var result = work(_db, tx);
                tx.Commit();
                return result;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteBusy or SqliteLocked)
            {
                throw new UserDataLockedException(FilePath);
            }
        }
    }

    /// <summary>Drops and recreates the cache tables and sets <c>cache_version</c>.</summary>
    internal void ResetCache() => Write(WriteScope.Cache, (c, t) =>
    {
        ResetCacheTables(c, t);
        return 0;
    });

    public void Dispose()
    {
        lock (_gate)
            _db.Dispose();
    }

    // Microsoft.Data.Sqlite sets the busy handler from the command timeout (whole seconds, from DefaultTimeout) before every command,
    // so a PRAGMA busy_timeout would be overridden; a shorter timeout than one second is therefore rounded up.
    private static SqliteConnection Connect(string file, SqliteOpenMode mode, TimeSpan timeout)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = mode, Pooling = false, DefaultTimeout = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds)) }.ToString());
        try
        {
            db.Open();
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    private static bool DefaultQuickCheck(SqliteConnection db)
    {
        var rows = Column(db, "PRAGMA quick_check");
        return rows is ["ok"];
    }

    private static bool IsDamage(SqliteException ex) => ex.SqliteErrorCode is SqliteCorrupt or SqliteNotADb;

    private static UserDataDamagedException Damaged(string file, List<string> copies, string? detail = null)
    {
        if (copies.Count == 0)
            CopyDamaged(file, copies);
        return new UserDataDamagedException(file, copies.ToArray(), detail);
    }

    // Byte for byte, the -wal and -shm with the same suffix; never overwrites an earlier copy.
    private static void CopyDamaged(string file, List<string> copies)
    {
        if (copies.Count > 0)
            return;

        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var target = UniquePath($"{file}.damaged-{stamp}", "");
        File.Copy(file, target);
        copies.Add(target);
        foreach (var side in new[] { "-wal", "-shm" })
        {
            if (File.Exists(file + side))
                File.Copy(file + side, target + side);
        }
    }

    // Returns "<stem><suffix>", or "<stem>-2<suffix>", "-3" ... when taken (the side files are checked too).
    private static string UniquePath(string stem, string suffix)
    {
        var path = stem + suffix;
        for (var n = 2; File.Exists(path) || File.Exists(path + "-wal") || File.Exists(path + "-shm"); n++)
            path = $"{stem}-{n}{suffix}";
        return path;
    }

    private static void CreateSchema(SqliteConnection db, int userVersion)
    {
        using var tx = db.BeginTransaction(deferred: false);
        Exec(db, tx, UserDataSchema.UserTables);
        Exec(db, tx, $"INSERT INTO meta(key, value) VALUES ('user_version', '{userVersion}')");
        tx.Commit();
    }

    private static void ResetCache(SqliteConnection db)
    {
        using var tx = db.BeginTransaction(deferred: false);
        ResetCacheTables(db, tx);
        tx.Commit();
    }

    private static void ResetCacheTables(SqliteConnection db, SqliteTransaction tx)
    {
        Exec(db, tx, UserDataSchema.DropCacheTables);
        Exec(db, tx, UserDataSchema.CacheTables);
        Exec(db, tx, $"INSERT INTO meta(key, value) VALUES ('cache_version', '{CacheVersion}') ON CONFLICT(key) DO UPDATE SET value = excluded.value");
    }

    private static void Migrate(SqliteConnection db, string file, int from, int to, IReadOnlyList<UserMigration> migrations)
    {
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var backup = UniquePath(Path.Combine(Path.GetDirectoryName(file)!, $"userdata.v{from}-{stamp}"), ".db.bak");
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "VACUUM INTO $path";
            cmd.Parameters.AddWithValue("$path", backup);
            cmd.ExecuteNonQuery();
        }

        using var tx = db.BeginTransaction(deferred: false);
        foreach (var migration in migrations.Where(m => m.From >= from).OrderBy(m => m.From))
            migration.Apply(db, tx);

        Exec(db, tx, $"UPDATE meta SET value = '{to}' WHERE key = 'user_version'");
        tx.Commit();
    }

    private static int? ReadMeta(SqliteConnection db, string key)
    {
        try
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT value FROM meta WHERE key = $key";
            cmd.Parameters.AddWithValue("$key", key);
            return int.TryParse(cmd.ExecuteScalar() as string, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteError)
        {
            return null; // a foreign meta table without our columns
        }
    }

    private static List<string> Column(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(reader.GetString(0));
        return rows;
    }

    private static void Exec(SqliteConnection db, SqliteTransaction? tx, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
