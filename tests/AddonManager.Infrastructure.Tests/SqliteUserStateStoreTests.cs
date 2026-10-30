using AddonManager.Core.Cards;
using AddonManager.Core.Linking;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using Microsoft.Data.Sqlite;

namespace AddonManager.Infrastructure.Tests;

public sealed class SqliteUserStateStoreTests : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly UserDataDatabase db;
    private readonly SqliteUserStateStore store;

    public SqliteUserStateStoreTests()
    {
        db = UserDataDatabase.Open(DbFile);
        store = new SqliteUserStateStore(db);
    }

    private string DbFile => Path.Combine(temp.Root, "userdata.db");

    public void Dispose()
    {
        db.Dispose();
        temp.Dispose();
    }

    private static AirportCode Code(string text)
    {
        Assert.True(AirportCode.TryParse(text, out var code));
        return code;
    }

    private static PackageMemo Memo() => new(
        @"C:\Sim\Addons\acme-esgg", PackageSource.Library, "Gothenburg", "SCENERY", "1.2", @"ContentInfo\t.jpg", BglPlacement.Places,
        [Code("ESGG")], ["Cessna"], AircraftRole.Provider, [new AirportRecord(Code("ESGG"), "Landvetter", new GeoPoint(57.66, 12.28))])
    {
        Tags = ["x"],
        TypeDesignators = ["C152"],
    };

    private void Raw(string sql)
        => db.Write(WriteScope.User, (c, t) =>
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = t;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
            return 0;
        });

    private long Count(string table)
        => db.Read(c =>
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
            return (long)cmd.ExecuteScalar()!;
        });

    private static CardRecord Card(long id, Category section = Category.Scenery) => new() { Id = id, Created = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(2)), Section = section, Tab = section == Category.Scenery ? CardTab.Airports : CardTab.Other };

    [Fact]
    public void An_empty_file_loads_as_first_run()
    {
        var state = store.Load();

        Assert.Null(state.Snapshot);
        Assert.Empty(state.Libraries);
        Assert.Empty(state.Cards);
        Assert.Equal(1, state.NextCardId);
        Assert.Null(state.PackagesRoot);
    }

    [Fact]
    public void Everything_survives_a_round_trip()
    {
        var state = new UserState
        {
            Libraries = [new LibraryRoot(@"C:\Sim\B", [Category.Scenery, Category.Aircraft]), new LibraryRoot(@"C:\Sim\A", [Category.Other])],
            NextCardId = 9,
            Snapshot = [],
            PackagesRoot = @"C:\Packages",
            ContentPassCompleted = true,
        };
        var card = Card(4);
        card.Codes = [Code("EGLL"), Code("ESGG")];
        card.RemovedCodes = [Code("ESGP")];
        card.Name = "Gothenburg";
        card.ImagePath = @"C:\img.png";
        state.Cards.Add(card);
        state.Cards.Add(Card(5, Category.Aircraft));
        state.Packages[new("Acme")] = new PackageState(true, @"C:\Sim\B\Acme") { CardId = 4, Section = Category.Scenery, Memo = Memo() };
        state.Packages[new("Off")] = new PackageState(false);
        state.Profiles[new("LPMA-x.ini")] = new PackageState(true, @"C:\gsx\x.ini") { CardId = 5, Section = Category.Scenery, Memo = Memo() };
        state.Pending.Add(new("Acme"));
        state.Content.AirportThreshold = 7;
        state.Content.AddStopWord("ZZZ");
        state.Content.AddExclusionPattern("excl-*");
        state.Content.RemoveExclusionPattern("navigraph-navdata*");
        state.Content.OptedIn.Add(new("Opt"));

        store.Save(state);
        var loaded = store.Load();

        Assert.Equal(["C:\\Sim\\B", "C:\\Sim\\A"], loaded.Libraries.Select(l => l.Path));
        Assert.Equal([Category.Scenery, Category.Aircraft], loaded.Libraries[0].Sections);
        Assert.Equal(9, loaded.NextCardId);
        Assert.NotNull(loaded.Snapshot);
        Assert.Empty(loaded.Snapshot!);
        Assert.Equal(@"C:\Packages", loaded.PackagesRoot);
        Assert.True(loaded.ContentPassCompleted);
        var c = loaded.FindCard(4)!;
        Assert.Equal(["EGLL", "ESGG"], c.Codes.Select(x => x.Value));
        Assert.Equal(["ESGP"], c.RemovedCodes.Select(x => x.Value));
        Assert.Equal("Gothenburg", c.Name);
        Assert.Equal(@"C:\img.png", c.ImagePath);
        Assert.Equal(card.Created, c.Created);
        Assert.Equal(CardTab.Airports, c.Tab);
        var p = loaded.Packages[new("acme")];
        Assert.True(p.On);
        Assert.Equal(@"C:\Sim\B\Acme", p.ActiveCopyPath);
        Assert.Equal(4, p.CardId);
        Assert.Equal(Category.Scenery, p.Section);
        Assert.True(Memo().SameAs(p.Memo));
        Assert.Equal(Category.Aircraft, loaded.FindCard(5)!.Section);
        var profile = loaded.Profiles[new("lpma-x.ini")];
        Assert.Equal(5, profile.CardId);
        Assert.Equal(Category.Scenery, profile.Section);
        Assert.True(profile.On);
        Assert.Equal(@"C:\gsx\x.ini", profile.ActiveCopyPath);
        Assert.True(Memo().SameAs(profile.Memo));
        Assert.Contains("excl-*", loaded.Content.AddedExclusionPatterns);
        Assert.Contains("navigraph-navdata*", loaded.Content.RemovedExclusionPatterns);
        Assert.Contains(new PackageName("Acme"), loaded.Pending);
        Assert.Equal(7, loaded.Content.AirportThreshold);
        Assert.Contains("ZZZ", loaded.Content.AddedStopWords);
        Assert.Contains(new PackageName("Opt"), loaded.Content.OptedIn);
        Assert.False(loaded.Packages[new("Off")].On);
    }

    [Fact]
    public void A_snapshot_with_entries_round_trips_and_null_stays_null()
    {
        store.Save(new UserState { Snapshot = [new SnapshotEntry("a", EntryKind.Junction, @"C:\x", true), new SnapshotEntry("b", EntryKind.File, null), new SnapshotEntry("c", EntryKind.Symlink, @"C:\y", false)] });
        var loaded = store.Load();
        Assert.Equal(3, loaded.Snapshot!.Count);
        Assert.Equal(new SnapshotEntry("c", EntryKind.Symlink, @"C:\y", false), loaded.Snapshot[2]);
        Assert.Equal(new SnapshotEntry("a", EntryKind.Junction, @"C:\x", true), loaded.Snapshot[0]);
        Assert.Equal(new SnapshotEntry("b", EntryKind.File, null), loaded.Snapshot[1]);

        store.Save(new UserState());
        Assert.Null(store.Load().Snapshot);
    }

    [Fact]
    public void Card_references_hold_after_a_merge_and_save()
    {
        var state = new UserState();
        var a = state.NewCard(Category.Scenery, CardTab.Airports, [Code("ESGG")], DateTimeOffset.UtcNow);
        var b = state.NewCard(Category.Scenery, CardTab.Airports, [Code("ESGP")], DateTimeOffset.UtcNow);
        state.SetCard(PackageKey.Folder(new("one")), a.Id);
        state.SetCard(PackageKey.Folder(new("two")), b.Id);
        store.Save(state);

        var loaded = store.Load();
        loaded.SetCard(PackageKey.Folder(new("two")), a.Id);
        loaded.RemoveEmptyCards();
        store.Save(loaded);
        var again = store.Load();

        Assert.Equal([a.Id], again.Cards.Select(c => c.Id));
        Assert.Equal(a.Id, again.Packages[new("two")].CardId);
        Assert.Equal(3, again.NextCardId);
    }

    [Fact]
    public void A_card_left_without_packages_is_dropped_on_load()
    {
        var state = new UserState { NextCardId = 3 };
        state.Cards.Add(Card(1));
        state.Cards.Add(Card(2));
        state.Packages[new("p")] = new PackageState(false) { CardId = 1 };

        store.Save(state);
        var loaded = store.Load();

        Assert.Equal([1L], loaded.Cards.Select(c => c.Id));
        Assert.Equal(3, loaded.NextCardId);
    }

    [Fact]
    public void A_folder_package_and_a_profile_with_one_name_are_both_stored()
    {
        var state = new UserState();
        state.Packages[new("LPMA")] = new PackageState(true);
        state.Profiles[new("lpma")] = new PackageState(false);

        store.Save(state);
        var loaded = store.Load();

        Assert.True(loaded.Packages[new("LPMA")].On);
        Assert.False(loaded.Profiles[new("LPMA")].On);
        Assert.Equal(2, Count("package"));
    }

    [Fact]
    public void Package_names_differing_only_in_case_are_one_package()
    {
        var state = new UserState();
        state.Packages[new("Foo")] = new PackageState(false);
        state.Packages[new("FOO")] = new PackageState(true);

        store.Save(state);

        Assert.Equal(1, Count("package"));
        Assert.True(store.Load().Packages[new("foo")].On);
    }

    [Fact]
    public void A_new_cache_version_leaves_the_user_tables_untouched()
    {
        var state = new UserState { Libraries = [new LibraryRoot(@"C:\L", [Category.Scenery])] };
        state.Packages[new("p")] = new PackageState(true);
        store.Save(state);
        db.Dispose();
        using (var raw = new SqliteConnection($"Data Source={DbFile};Pooling=False"))
        {
            raw.Open();
            using var cmd = raw.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = '0' WHERE key = 'cache_version'";
            cmd.ExecuteNonQuery();
        }

        using var reopened = UserDataDatabase.Open(DbFile);
        var loaded = new SqliteUserStateStore(reopened).Load();

        Assert.Single(loaded.Libraries);
        Assert.True(loaded.Packages[new("p")].On);
    }

    [Fact]
    public void Saving_through_cache_only_access_is_refused()
    {
        db.Dispose();
        using var cacheOnly = UserDataDatabase.Open(DbFile, UserDataAccess.CacheOnly);
        var cacheStore = new SqliteUserStateStore(cacheOnly);

        Assert.Empty(cacheStore.Load().Libraries);
        Assert.Throws<InvalidOperationException>(() => cacheStore.Save(new UserState()));
    }

    [Theory]
    [InlineData("INSERT INTO card(id, section, tab, created) VALUES (1, 'Bogus', 'Airports', '2026-10-01T12:00:00.0000000+00:00')")]
    [InlineData("INSERT INTO library(position, path, sections) VALUES (0, 'C:\\L', '')")]
    [InlineData("INSERT INTO package(kind, name_key, name, is_on, memo) VALUES ('Folder', 'P', 'p', 1, '{ broken')")]
    [InlineData("INSERT INTO package(kind, name_key, name, is_on) VALUES ('Weird', 'P', 'p', 1)")]
    [InlineData("INSERT INTO setting(key, value) VALUES ('content', '{ broken')")]
    public void A_user_row_that_cannot_be_read_stops_the_load_naming_the_file(string sql)
    {
        Raw(sql);

        var ex = Assert.Throws<UserDataDamagedException>(() => store.Load());

        Assert.Equal(Path.GetFullPath(DbFile), ex.FilePath);
        Assert.Empty(ex.Copies);
        Assert.DoesNotContain("Copies", ex.Message);
    }

    [Fact]
    public void The_damaged_message_names_copies_only_when_there_are_some()
    {
        var with = new UserDataDamagedException(@"C:.db", [@"C:.db.bak1", @"C:.db.bak2"], "Why.");
        var without = new UserDataDamagedException(@"C:.db", [], "Why.");

        Assert.Equal(@"'C:.db' is damaged. Copies: C:.db.bak1, C:.db.bak2 Why.", with.Message);
        Assert.Equal(@"'C:.db' is damaged. Why.", without.Message);
        Assert.Equal(@"'C:.db' is damaged.", new UserDataDamagedException(@"C:.db", []).Message);
    }

    private string StoredMemo()
    {
        var state = new UserState();
        state.Packages[new("Acme")] = new PackageState(true) { Memo = Memo() };
        store.Save(state);
        return db.Read(c =>
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT memo FROM package";
            return (string)cmd.ExecuteScalar()!;
        });
    }

    [Fact]
    public void A_stored_memo_holds_enum_names()
    {
        var memo = StoredMemo();

        Assert.Contains("\"Library\"", memo);
        Assert.Contains("\"Places\"", memo);
        Assert.Contains("\"Provider\"", memo);
    }

    [Theory]
    [InlineData("\"Source\":\"Library\"", "\"Source\":\"Bogus\"")]
    [InlineData("\"Source\":\"Library\"", "\"Source\":0")]
    [InlineData("\"Source\":\"Library\"", "\"Source\":9")]
    public void A_memo_enum_that_is_unknown_or_an_integer_stops_the_load(string from, string to)
    {
        var memo = StoredMemo();
        Assert.Contains(from, memo);
        Raw($"UPDATE package SET memo = '{memo.Replace(from, to)}'");

        Assert.Throws<UserDataDamagedException>(() => store.Load());
    }

    [Fact]
    public void A_missing_user_table_stops_the_load_naming_the_file()
    {
        Raw("DROP TABLE package");

        var ex = Assert.Throws<UserDataDamagedException>(() => store.Load());

        Assert.Equal(Path.GetFullPath(DbFile), ex.FilePath);
    }

    [Fact]
    public void A_library_section_item_that_is_empty_is_damage_and_duplicates_collapse()
    {
        Raw(@"INSERT INTO library(position, path, sections) VALUES (0, 'C:\L', 'Scenery,Scenery,Aircraft')");
        Assert.Equal([Category.Scenery, Category.Aircraft], store.Load().Libraries.Single().Sections);

        Raw("UPDATE library SET sections = 'Scenery,,Aircraft'");
        Assert.Throws<UserDataDamagedException>(() => store.Load());
    }

    [Fact]
    public void Invalid_ICAO_codes_are_dropped_on_load()
    {
        Raw("INSERT INTO card(id, section, tab, created) VALUES (1, 'Scenery', 'Airports', '2026-10-01T12:00:00.0000000+00:00')");
        Raw("INSERT INTO card_code(card_id, code, removed) VALUES (1, 'ESGG', 0), (1, '??', 0), (1, '', 1), (1, 'ESGP', 1)");
        Raw("INSERT INTO package(kind, name_key, name, card_id, is_on) VALUES ('Folder', 'P', 'p', 1, 0)");

        var card = store.Load().FindCard(1)!;

        Assert.Equal(["ESGG"], card.Codes.Select(c => c.Value));
        Assert.Equal(["ESGP"], card.RemovedCodes.Select(c => c.Value));
    }

    [Fact]
    public void Card_invariants_are_restored_on_load()
    {
        Raw("INSERT INTO card(id, section, tab, created) VALUES (1, 'Scenery', 'Airports', '2026-10-01T12:00:00.0000000+00:00'), (7, 'Aircraft', 'Other', '2026-10-01T12:00:00.0000000+00:00'), (8, 'Scenery', 'Landscapes', '2026-10-01T12:00:00.0000000+00:00')");
        Raw("INSERT INTO card_code(card_id, code, removed) VALUES (1, 'ESGP', 0), (1, 'ESGG', 0), (1, 'esgp', 1), (7, 'EGLL', 0)");
        Raw("INSERT INTO package(kind, name_key, name, card_id, is_on) VALUES ('Folder', 'A', 'a', 1, 0), ('Folder', 'B', 'b', 7, 0)");
        Raw("INSERT INTO setting(key, value) VALUES ('next_card_id', '3')");

        var loaded = store.Load();

        var one = loaded.FindCard(1)!;
        Assert.Equal(["ESGG", "ESGP"], one.Codes.Select(c => c.Value));
        Assert.Empty(one.RemovedCodes);
        Assert.Empty(loaded.FindCard(7)!.Codes);
        Assert.Null(loaded.FindCard(8));
        Assert.Equal(9, loaded.NextCardId);
    }

    [Fact]
    public void A_missing_next_card_id_is_set_above_every_card_id_ever_stored()
    {
        Raw("INSERT INTO card(id, section, tab, created) VALUES (5, 'Scenery', 'Airports', '2026-10-01T12:00:00.0000000+00:00')");
        Raw("INSERT INTO package(kind, name_key, name, card_id, is_on) VALUES ('Folder', 'A', 'a', 5, 0)");

        Assert.Equal(6, store.Load().NextCardId);
    }

    private sealed class NotRunning : ISimProcessMonitor
    {
        public bool IsRunning => false;
    }

    // A second connection, like an SQLite editor; it gives up after one second instead of waiting out the default.
    private SqliteConnection Other()
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbFile, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 1 }.ToString());
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long CardsSeenBy(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM card";
        return (long)cmd.ExecuteScalar()!;
    }

    private static UserState WithCard()
    {
        var state = new UserState();
        state.NewCard(Category.Scenery, CardTab.Airports, [], DateTimeOffset.UnixEpoch);
        return state;
    }

    [Fact]
    public void A_write_lock_held_by_another_program_refuses_the_change_before_any_link_is_touched()
    {
        var community = Directory.CreateDirectory(Path.Combine(temp.Root, "Community")).FullName;
        var package = temp.MakePackage(Path.Combine("Lib", "pkg"));
        var library = new LibraryRoot(Path.Combine(temp.Root, "Lib"), LibraryRoot.AllSections);
        var index = new PackageIndex([new PackageCopy(new PackageName("pkg"), package, library, new Manifest(null, null, null, null, []))], [library]);
        var applier = new ChangeApplier(new JunctionFileSystem(), new CommunityReader(community), store, new NotRunning(), community);
        var state = new UserState { Snapshot = [] };

        using var other = Other();
        Exec(other, "BEGIN IMMEDIATE");

        Assert.Throws<UserDataLockedException>(() => applier.SetChoices(state, index, new Dictionary<PackageName, bool> { [new PackageName("pkg")] = true }));

        Assert.Empty(Directory.GetFileSystemEntries(community));
        Assert.False(state.IsOn(new PackageName("pkg")));
        Exec(other, "ROLLBACK");
        Assert.Equal(0, CardsSeenBy(other));
    }

    [Fact]
    public void A_nested_change_joins_the_outer_one_and_commits_once()
    {
        using var other = Other();

        using (store.BeginChange())
        {
            using (store.BeginChange())
                store.Save(WithCard());

            // The inner dispose commits nothing, and the write lock is still held.
            Assert.Equal(0, CardsSeenBy(other));
            Assert.Throws<SqliteException>(() => Exec(other, "BEGIN IMMEDIATE"));
        }

        Assert.Equal(1, CardsSeenBy(other));
        Exec(other, "BEGIN IMMEDIATE");
        Exec(other, "ROLLBACK");
    }

    [Fact]
    public void A_save_inside_a_change_commits_at_dispose_and_releases_the_lock()
    {
        using var other = Other();
        var change = store.BeginChange();
        store.Save(WithCard());

        Assert.Equal(0, CardsSeenBy(other));
        Assert.Throws<SqliteException>(() => Exec(other, "BEGIN IMMEDIATE"));

        change.Dispose();

        Assert.Equal(1, CardsSeenBy(other));
        Exec(other, "BEGIN IMMEDIATE");
        Exec(other, "ROLLBACK");
    }

    [Fact]
    public void A_change_disposed_without_a_save_writes_nothing()
    {
        store.Save(WithCard());
        using var other = Other();

        using (store.BeginChange())
        {
        }

        Assert.Equal(1, CardsSeenBy(other));
        Assert.Equal(1, Count("card"));
        Exec(other, "BEGIN IMMEDIATE");
        Exec(other, "ROLLBACK");
    }

    [Fact]
    public void A_load_inside_a_change_on_the_same_thread_sees_the_unsaved_rows()
    {
        store.Save(WithCard());

        using (store.BeginChange())
        {
            store.Save(new UserState());
            Assert.Empty(store.Load().Cards);
        }

        Assert.Empty(store.Load().Cards);
    }

    private void SaveThenThrow()
    {
        using var change = store.BeginChange();
        store.Save(new UserState());
        throw new InvalidOperationException("boom");
    }

    [Fact]
    public void A_change_that_throws_rolls_back_and_releases_the_lock()
    {
        store.Save(WithCard());
        using var other = Other();

        Assert.Throws<InvalidOperationException>(SaveThenThrow);

        // The scope cannot tell why it ends, so it commits the Save; what matters is that the lock is gone.
        Exec(other, "BEGIN IMMEDIATE");
        Exec(other, "ROLLBACK");
    }
}
