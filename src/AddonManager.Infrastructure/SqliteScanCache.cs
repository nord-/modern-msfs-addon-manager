using System.Text.Json;
using AddonManager.Core.Content;
using Microsoft.Data.Sqlite;

namespace AddonManager.Infrastructure;

/// <summary>
/// Disposable SQLite store of scanned content. Never throws for a bad, foreign or locked cache file:
/// a corrupt or old file is deleted and rebuilt; a locked one (or one that cannot be deleted) makes this
/// instance work in memory for the rest of its life. Not thread-safe: one caller at a time.
/// </summary>
public sealed class SqliteScanCache(string filePath) : IScanCache, IDisposable
{
    /// <summary>Bump when <c>PackageContent</c>'s shape or any reader's output for the same files changes; the cache is then rebuilt.</summary>
    public const int FormatVersion = 2;

    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADb = 26;

    private SqliteConnection? _db;
    private Dictionary<string, PackageContent> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, PackageContent>? _memory;

    /// <summary>True when the file could not be used and this instance keeps its content in memory.</summary>
    public bool IsFallback => _memory is not null;

    public IReadOnlyList<PackageContent> LoadAll()
        => Run(
            db =>
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = "SELECT content FROM package";
                using var reader = cmd.ExecuteReader();
                var rows = new Dictionary<string, PackageContent>(StringComparer.OrdinalIgnoreCase);
                while (reader.Read())
                {
                    var content = ReadRow(reader.GetString(0));
                    rows[content.Path] = content;
                }

                _loaded = rows;
                return (IReadOnlyList<PackageContent>)rows.Values.ToList();
            },
            () => _memory!.Values.ToList());

    public void Upsert(IReadOnlyList<PackageContent> contents)
        => Run(
            db =>
            {
                using var tx = db.BeginTransaction();
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO package(key, content) VALUES($key, $content) ON CONFLICT(key) DO UPDATE SET content = excluded.content";
                var key = cmd.Parameters.Add("$key", SqliteType.Text);
                var json = cmd.Parameters.Add("$content", SqliteType.Text);
                foreach (var content in contents)
                {
                    key.Value = Key(content.Path);
                    json.Value = JsonSerializer.Serialize(content, ContentJson.Options);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                foreach (var content in contents)
                    _loaded[content.Path] = content;
                return 0;
            },
            () =>
            {
                foreach (var content in contents)
                    _memory![content.Path] = content;
                return 0;
            });

    public void Remove(IReadOnlyCollection<string> paths)
        => Run(
            db =>
            {
                using var tx = db.BeginTransaction();
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM package WHERE key = $key";
                var key = cmd.Parameters.Add("$key", SqliteType.Text);
                foreach (var p in paths)
                {
                    key.Value = Key(p);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                foreach (var p in paths)
                    _loaded.Remove(p);
                return 0;
            },
            () =>
            {
                foreach (var p in paths)
                    _memory!.Remove(p);
                return 0;
            });

    public void Dispose() => CloseConnection();

    private T Run<T>(Func<SqliteConnection, T> work, Func<T> inMemory)
    {
        if (_memory is not null)
            return inMemory();

        for (var rebuilt = false; ; rebuilt = true)
        {
            try
            {
                return work(Connection());
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteBusy or SqliteLocked)
            {
                return UseMemory(inMemory);
            }
            catch (Exception ex) when (!rebuilt && IsCorrupt(ex))
            {
                if (!TryDeleteFiles())
                    return UseMemory(inMemory);
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or JsonException)
            {
                return UseMemory(inMemory);
            }
        }
    }

    // A row that is not a valid PackageContent (null fields, an invalid name) means the cache is corrupt.
    private static PackageContent ReadRow(string json)
    {
        try
        {
            var content = JsonSerializer.Deserialize<PackageContent>(json, ContentJson.Options);
            return string.IsNullOrEmpty(content?.Path) || string.IsNullOrEmpty(content.Name.Value) || HasNullElement(content)
                ? throw new CorruptCacheException()
                : content;
        }
        catch (Exception ex) when (ex is not (CorruptCacheException or OperationCanceledException))
        {
            throw new CorruptCacheException();
        }
    }

    // RespectNullableAnnotations does not look inside collections, and a null element would fail later, in the analysis, where nothing rebuilds the cache.
    private static bool HasNullElement(PackageContent content)
        => content.Airports.Any(a => a is null)
            || content.AircraftConfigs.Any(a => a is null)
            || content.AirplaneFolders.Any(f => f is null)
            || content.EncryptedAirplaneFolders.Any(f => f is null)
            || content.Manifest.Dependencies.Any(d => d is null);

    // SQLite's NOCASE folds ASCII only, while the in-memory index compares ordinal-ignore-case; the table is keyed on a folded copy instead.
    private static string Key(string path) => path.ToUpperInvariant();

    private static bool IsCorrupt(Exception ex)
        => ex is JsonException or CorruptCacheException
            || ex is SqliteException { SqliteErrorCode: SqliteCorrupt or SqliteNotADb };

    private SqliteConnection Connection()
    {
        if (_db is not null)
            return _db;

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (directory is not null)
            Directory.CreateDirectory(directory);

        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString());

        try
        {
            db.Open();
            Initialise(db);
        }
        catch
        {
            db.Dispose();
            throw;
        }

        _db = db;
        return db;
    }

    private static void Initialise(SqliteConnection db)
    {
        var version = Scalar(db, "PRAGMA user_version");
        var hasTable = Scalar(db, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'package'") > 0;

        if (version == FormatVersion && hasTable)
            return;

        if (version != 0 || hasTable)
            throw new CorruptCacheException();

        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"CREATE TABLE package (key TEXT PRIMARY KEY, content TEXT NOT NULL); PRAGMA user_version = {FormatVersion};";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    private static long Scalar(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private bool TryDeleteFiles()
    {
        CloseConnection();
        try
        {
            File.Delete(filePath);
            File.Delete(filePath + "-journal");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private T UseMemory<T>(Func<T> inMemory)
    {
        CloseConnection();
        _memory = new Dictionary<string, PackageContent>(_loaded, StringComparer.OrdinalIgnoreCase);
        return inMemory();
    }

    private void CloseConnection()
    {
        _db?.Dispose();
        _db = null;
    }

    private sealed class CorruptCacheException : Exception;
}
