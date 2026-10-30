using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using Microsoft.Data.Sqlite;

namespace AddonManager.Infrastructure;

/// <summary>
/// Stores <see cref="UserState"/> in the user tables of the shared <see cref="UserDataDatabase"/> (spec 11). <see cref="Save"/> replaces every
/// user row in one transaction. <see cref="Load"/> repairs what a hand edit can break (ICAO codes, card invariants) and throws
/// <see cref="UserDataDamagedException"/> for a row it cannot read, leaving the file untouched.
/// </summary>
public sealed class SqliteUserStateStore(UserDataDatabase db) : IUserStateStore
{
    private const string NextCardIdKey = "next_card_id";
    private const string ContentPassKey = "content_pass_completed";
    private const string PackagesRootKey = "packages_root";
    private const string ContentKey = "content";
    private const string SnapshotAcceptedKey = "snapshot_accepted";

    // The scan cache's options plus enum names: a reordered enum must not change a stored memo, and an unknown name or a number is damage.
    private static readonly JsonSerializerOptions Json = new(ContentJson.Options)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public UserState Load()
    {
        try
        {
            return db.Read(Load);
        }
        catch (SqliteException ex)
        {
            throw Damaged(ex.Message);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidCastException or OverflowException)
        {
            throw Damaged(ex.Message);
        }
    }

    public IDisposable BeginChange() => db.BeginUserChange();

    public void Save(UserState state)
        => db.Write(WriteScope.User, (c, tx) =>
        {
            // Children first: foreign keys are on.
            foreach (var table in new[] { "package", "card_code", "card", "library", "pending", "snapshot_entry", "setting" })
                Run(c, tx, $"DELETE FROM {table}");

            InsertLibraries(c, tx, state);
            InsertCards(c, tx, state);
            InsertPackages(c, tx, state);
            InsertPending(c, tx, state);
            InsertSnapshot(c, tx, state);
            InsertSettings(c, tx, state);
            return 0;
        });

    private UserState Load(SqliteConnection c)
    {
        var state = new UserState();
        var settings = Rows(c, "SELECT key, value FROM setting", r => (Key: r.GetString(0), Value: r.GetString(1))).ToDictionary(s => s.Key, s => s.Value);

        foreach (var row in Rows(c, "SELECT path, sections FROM library ORDER BY position", r => (Path: r.GetString(0), Sections: r.GetString(1))))
        {
            var sections = row.Sections.Split(',', StringSplitOptions.TrimEntries).Select(s => ParseEnum<Category>(s, "library")).ToList();
            state.Libraries.Add(new LibraryRoot(row.Path, sections));
        }

        var cards = ReadCards(c);
        var maxId = cards.Count == 0 ? 0 : cards.Max(card => card.Id);
        ReadPackages(c, state);
        foreach (var card in cards)
        {
            // R2: a card with a Package; the dropped ones keep their ids reserved through NextCardId below.
            if (state.PackagesOn(card.Id).Count > 0)
                state.Cards.Add(card);
        }

        var onCards = state.Cards.Select(card => card.Id).ToHashSet();
        foreach (var package in state.Packages.Keys.ToList().Where(k => state.Packages[k].CardId is { } id && !onCards.Contains(id)))
            state.Packages[package] = state.Packages[package] with { CardId = null };
        foreach (var package in state.Profiles.Keys.ToList().Where(k => state.Profiles[k].CardId is { } id && !onCards.Contains(id)))
            state.Profiles[package] = state.Profiles[package] with { CardId = null };

        foreach (var name in Rows(c, "SELECT name FROM pending", r => r.GetString(0)))
            state.Pending.Add(new PackageName(name));

        if (settings.GetValueOrDefault(SnapshotAcceptedKey) == "1")
        {
            state.Snapshot = [.. Rows(c, "SELECT name, kind, target, target_was_package FROM snapshot_entry ORDER BY rowid", r => new SnapshotEntry(
                r.GetString(0),
                ParseEnum<EntryKind>(r.GetString(1), "snapshot"),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt64(3) != 0))];
        }

        state.ContentPassCompleted = settings.GetValueOrDefault(ContentPassKey) == "1";
        state.PackagesRoot = settings.GetValueOrDefault(PackagesRootKey);
        if (settings.TryGetValue(ContentKey, out var content))
            state.Content = JsonSerializer.Deserialize<ContentSettings>(content, Json) ?? throw Damaged("The content settings are empty.");

        var stored = settings.TryGetValue(NextCardIdKey, out var next) ? long.Parse(next, NumberStyles.None, CultureInfo.InvariantCulture) : 0;
        state.NextCardId = Math.Max(Math.Max(stored, maxId + 1), 1);
        return state;
    }

    private List<CardRecord> ReadCards(SqliteConnection c)
    {
        var cards = new Dictionary<long, CardRecord>();
        foreach (var r in Rows(c, "SELECT id, section, tab, name, image_path, created FROM card ORDER BY id", r => (
            Id: r.GetInt64(0), Section: r.GetString(1), Tab: r.GetString(2), Name: r.IsDBNull(3) ? null : r.GetString(3), Image: r.IsDBNull(4) ? null : r.GetString(4), Created: r.GetString(5))))
        {
            if (!cards.TryAdd(r.Id, new CardRecord
            {
                Id = r.Id,
                Section = ParseEnum<Category>(r.Section, "card"),
                Tab = ParseEnum<CardTab>(r.Tab, "card"),
                Name = r.Name,
                ImagePath = r.Image,
                Created = DateTimeOffset.Parse(r.Created, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            }))
            {
                throw Damaged($"The card id {r.Id} is stored twice.");
            }
        }

        var codes = new Dictionary<long, (HashSet<AirportCode> Codes, HashSet<AirportCode> Removed)>();
        foreach (var row in Rows(c, "SELECT card_id, code, removed FROM card_code", r => (CardId: r.GetInt64(0), Code: r.GetString(1), Removed: r.GetInt64(2) != 0)))
        {
            // An invalid ICAO code is dropped (C3); so is any ICAO code on a card that is not Scenery (R2).
            if (!cards.TryGetValue(row.CardId, out var card) || card.Section != Category.Scenery || !AirportCode.TryParse(row.Code, out var code))
                continue;
            var sets = codes.TryGetValue(row.CardId, out var s) ? s : codes[row.CardId] = ([], []);
            (row.Removed ? sets.Removed : sets.Codes).Add(code);
        }

        foreach (var (id, (kept, removed)) in codes)
        {
            // Codes and RemovedCodes are disjoint; an ICAO code in both stays in Codes.
            cards[id].Codes = [.. kept.OrderBy(x => x.Value, StringComparer.Ordinal)];
            cards[id].RemovedCodes = [.. removed.Where(x => !kept.Contains(x)).OrderBy(x => x.Value, StringComparer.Ordinal)];
        }

        return [.. cards.Values];
    }

    private void ReadPackages(SqliteConnection c, UserState state)
    {
        foreach (var r in Rows(c, "SELECT kind, name, card_id, section, is_on, active_copy, memo FROM package", r => (
            Kind: r.GetString(0), Name: r.GetString(1), CardId: r.IsDBNull(2) ? (long?)null : r.GetInt64(2), Section: r.IsDBNull(3) ? null : r.GetString(3),
            On: r.GetInt64(4) != 0, Copy: r.IsDBNull(5) ? null : r.GetString(5), Memo: r.IsDBNull(6) ? null : r.GetString(6))))
        {
            var package = new PackageState(r.On, r.Copy)
            {
                CardId = r.CardId,
                Section = r.Section is null ? null : ParseEnum<Category>(r.Section, "package"),
                Memo = r.Memo is null ? null : JsonSerializer.Deserialize<PackageMemo>(r.Memo, Json) ?? throw Damaged($"The memo of '{r.Name}' is empty."),
            };
            (ParseEnum<PackageKind>(r.Kind, "package") == PackageKind.Folder ? state.Packages : state.Profiles)[new PackageName(r.Name)] = package;
        }
    }

    private static void InsertLibraries(SqliteConnection c, SqliteTransaction tx, UserState state)
    {
        using var cmd = Command(c, tx, "INSERT INTO library(position, path, sections) VALUES ($position, $path, $sections)");
        for (var i = 0; i < state.Libraries.Count; i++)
        {
            Set(cmd, "$position", i);
            Set(cmd, "$path", state.Libraries[i].Path);
            Set(cmd, "$sections", string.Join(',', state.Libraries[i].Sections));
            cmd.ExecuteNonQuery();
        }
    }

    private static void InsertCards(SqliteConnection c, SqliteTransaction tx, UserState state)
    {
        using var card = Command(c, tx, "INSERT INTO card(id, section, tab, name, image_path, created) VALUES ($id, $section, $tab, $name, $image, $created)");
        using var code = Command(c, tx, "INSERT INTO card_code(card_id, code, removed) VALUES ($id, $code, $removed)");
        foreach (var record in state.Cards)
        {
            Set(card, "$id", record.Id);
            Set(card, "$section", record.Section.ToString());
            Set(card, "$tab", record.Tab.ToString());
            Set(card, "$name", record.Name);
            Set(card, "$image", record.ImagePath);
            Set(card, "$created", record.Created.ToString("O", CultureInfo.InvariantCulture));
            card.ExecuteNonQuery();

            foreach (var (codes, removed) in new[] { (record.Codes, 0), (record.RemovedCodes, 1) })
            {
                foreach (var value in codes)
                {
                    Set(code, "$id", record.Id);
                    Set(code, "$code", value.Value);
                    Set(code, "$removed", removed);
                    code.ExecuteNonQuery();
                }
            }
        }
    }

    private static void InsertPackages(SqliteConnection c, SqliteTransaction tx, UserState state)
    {
        using var cmd = Command(c, tx, "INSERT INTO package(kind, name_key, name, card_id, section, is_on, active_copy, memo) VALUES ($kind, $key, $name, $card, $section, $on, $copy, $memo)");
        var cardIds = state.Cards.Select(card => card.Id).ToHashSet();
        foreach (var (kind, states) in new[] { (PackageKind.Folder, state.Packages), (PackageKind.GsxProfile, state.Profiles) })
        {
            foreach (var (name, package) in states)
            {
                Set(cmd, "$kind", kind.ToString());
                Set(cmd, "$key", name.Value.ToUpperInvariant());
                Set(cmd, "$name", name.Value);
                // A Package pointing at a card that is not stored would break the foreign key; it is saved without a card.
                Set(cmd, "$card", package.CardId is { } id && cardIds.Contains(id) ? id : null);
                Set(cmd, "$section", package.Section?.ToString());
                Set(cmd, "$on", package.On ? 1 : 0);
                Set(cmd, "$copy", package.ActiveCopyPath);
                Set(cmd, "$memo", package.Memo is null ? null : JsonSerializer.Serialize(package.Memo, Json));
                cmd.ExecuteNonQuery();
            }
        }
    }

    private static void InsertPending(SqliteConnection c, SqliteTransaction tx, UserState state)
    {
        using var cmd = Command(c, tx, "INSERT INTO pending(name_key, name) VALUES ($key, $name)");
        foreach (var name in state.Pending)
        {
            Set(cmd, "$key", name.Value.ToUpperInvariant());
            Set(cmd, "$name", name.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private static void InsertSnapshot(SqliteConnection c, SqliteTransaction tx, UserState state)
    {
        using var cmd = Command(c, tx, "INSERT INTO snapshot_entry(name, kind, target, target_was_package) VALUES ($name, $kind, $target, $was)");
        foreach (var entry in state.Snapshot ?? [])
        {
            Set(cmd, "$name", entry.Name);
            Set(cmd, "$kind", entry.Kind.ToString());
            Set(cmd, "$target", entry.Target);
            Set(cmd, "$was", entry.TargetWasPackage is { } was ? (was ? 1 : 0) : null);
            cmd.ExecuteNonQuery();
        }
    }

    private static void InsertSettings(SqliteConnection c, SqliteTransaction tx, UserState state)
    {
        using var cmd = Command(c, tx, "INSERT INTO setting(key, value) VALUES ($key, $value)");
        void Put(string key, string? value)
        {
            if (value is null)
                return;
            Set(cmd, "$key", key);
            Set(cmd, "$value", value);
            cmd.ExecuteNonQuery();
        }

        Put(NextCardIdKey, state.NextCardId.ToString(CultureInfo.InvariantCulture));
        Put(ContentPassKey, state.ContentPassCompleted ? "1" : "0");
        Put(PackagesRootKey, state.PackagesRoot);
        Put(ContentKey, JsonSerializer.Serialize(state.Content, Json));
        Put(SnapshotAcceptedKey, state.Snapshot is null ? null : "1");
    }

    private UserDataDamagedException Damaged(string detail) => new(db.FilePath, [], detail);

    private T ParseEnum<T>(string text, string table) where T : struct, Enum
        => Enum.TryParse<T>(text, out var value) && Enum.IsDefined(value) && !int.TryParse(text, out _)
            ? value
            : throw Damaged($"The {table} table holds '{text}', which is not a {typeof(T).Name}.");

    private static List<T> Rows<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> read)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
            rows.Add(read(reader));
        return rows;
    }

    private static SqliteCommand Command(SqliteConnection c, SqliteTransaction tx, string sql)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static void Run(SqliteConnection c, SqliteTransaction tx, string sql)
    {
        using var cmd = Command(c, tx, sql);
        cmd.ExecuteNonQuery();
    }

    private static void Set(SqliteCommand cmd, string name, object? value)
    {
        if (cmd.Parameters.Contains(name))
            cmd.Parameters[name].Value = value ?? DBNull.Value;
        else
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
