using System.Text.Json;
using AddonManager.Core.Content;
using Microsoft.Data.Sqlite;

namespace AddonManager.Infrastructure;

/// <summary>
/// The scan cache, kept in the <c>scan_package</c> table of the shared user data file. A row that cannot be read
/// rebuilds the cache tables (user rows stay). A locked file throws <see cref="UserDataLockedException"/>: the caller
/// pauses the scan, nothing is kept in memory instead.
/// </summary>
public sealed class SqliteScanCache(UserDataDatabase db) : IScanCache
{
    public IReadOnlyList<PackageContent> LoadAll()
    {
        try
        {
            return db.Read(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT content FROM scan_package";
                using var reader = cmd.ExecuteReader();
                var rows = new Dictionary<string, PackageContent>(StringComparer.OrdinalIgnoreCase);
                while (reader.Read())
                {
                    var content = ReadRow(reader.GetString(0));
                    rows[content.Path] = content;
                }

                return (IReadOnlyList<PackageContent>)rows.Values.ToList();
            });
        }
        catch (CorruptCacheException)
        {
            db.ResetCache();
            return [];
        }
    }

    public void Upsert(IReadOnlyList<PackageContent> contents)
        => db.Write(WriteScope.Cache, (c, tx) =>
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO scan_package(key, content) VALUES($key, $content) ON CONFLICT(key) DO UPDATE SET content = excluded.content";
            var key = cmd.Parameters.Add("$key", SqliteType.Text);
            var json = cmd.Parameters.Add("$content", SqliteType.Text);
            foreach (var content in contents)
            {
                key.Value = Key(content.Path);
                json.Value = JsonSerializer.Serialize(content, ContentJson.Options);
                cmd.ExecuteNonQuery();
            }

            return 0;
        });

    public void Remove(IReadOnlyCollection<string> paths)
        => db.Write(WriteScope.Cache, (c, tx) =>
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM scan_package WHERE key = $key";
            var key = cmd.Parameters.Add("$key", SqliteType.Text);
            foreach (var p in paths)
            {
                key.Value = Key(p);
                cmd.ExecuteNonQuery();
            }

            return 0;
        });

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

    private sealed class CorruptCacheException : Exception;
}
