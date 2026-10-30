namespace AddonManager.Infrastructure;

/// <summary>The DDL of the single user data file. <c>scan_package</c> is the only cache table; the rest is the user's.</summary>
internal static class UserDataSchema
{
    /// <summary>Cache tables: dropped and recreated whenever the cache version changes.</summary>
    public const string CacheTables = """
        CREATE TABLE scan_package (key TEXT PRIMARY KEY, content TEXT NOT NULL);
        """;

    public const string UserTables = """
        CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE library (position INTEGER PRIMARY KEY, path TEXT NOT NULL, sections TEXT NOT NULL);
        CREATE TABLE card (id INTEGER PRIMARY KEY, section TEXT NOT NULL, tab TEXT NOT NULL, name TEXT, image_path TEXT, created TEXT NOT NULL);
        CREATE TABLE card_code (card_id INTEGER NOT NULL REFERENCES card(id) ON DELETE CASCADE, code TEXT NOT NULL, removed INTEGER NOT NULL, PRIMARY KEY (card_id, code));
        CREATE TABLE package (kind TEXT NOT NULL, name_key TEXT NOT NULL, name TEXT NOT NULL, card_id INTEGER REFERENCES card(id),
                              section TEXT, is_on INTEGER NOT NULL, active_copy TEXT, memo TEXT, PRIMARY KEY (kind, name_key));
        CREATE TABLE pending (name_key TEXT PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE snapshot_entry (name TEXT NOT NULL, kind TEXT NOT NULL, target TEXT, target_was_package INTEGER);
        CREATE TABLE setting (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        """;

    public const string DropCacheTables = "DROP TABLE IF EXISTS scan_package;";
}
