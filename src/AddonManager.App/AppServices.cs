using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.State;
using AddonManager.Infrastructure;
using AddonManager.Sim2020;
using AddonManager.Sim2020.Content;

namespace AddonManager.App;

/// <summary>
/// The composition root: the only place the app creates services. It opens the user database and
/// the scan service and reads Community; it never creates a link writer or a GSX writer.
/// </summary>
public sealed class AppServices : IDisposable
{
    /// <summary>How long the content watcher waits after the last file event before it reports (D8).</summary>
    public static readonly TimeSpan WatchQuietPeriod = TimeSpan.FromSeconds(2);

    private AppServices(UserDataDatabase database, IUserStateStore store, UserState state)
    {
        Database = database;
        Store = store;
        State = state;
        CreateWatcher = libraries => new ContentWatcher(Paths!.CommunityPath, libraries, WatchQuietPeriod, GsxFolder);
    }

    public UserDataDatabase Database { get; }

    /// <summary>The user state store; settable inside the assembly so tests can wrap it.</summary>
    public IUserStateStore Store { get; internal set; }

    public UserState State { get; }

    public SimPaths? Paths { get; private set; }

    public string? GsxFolder { get; private set; }

    public ScanService? Scans { get; private set; }

    /// <summary>Creates the content watcher for the given Library paths; tests replace it.</summary>
    internal Func<IReadOnlyList<string>, IContentWatcher> CreateWatcher { get; set; }

    /// <summary>Opens the database and loads the user state. Call off the UI thread; throws <see cref="UserDataException"/> unchanged.</summary>
    public static AppServices Open(string databaseFile)
    {
        var database = UserDataDatabase.Open(databaseFile, UserDataAccess.Full);
        try
        {
            var store = new SqliteUserStateStore(database);
            return new AppServices(database, store, store.Load());
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    public void UseSim(SimPaths paths, string gsxFolder) =>
        UseSim(paths, gsxFolder, new PackageContentReader(new AirportNames(paths.OfficialPath)));

    internal void UseSim(SimPaths paths, string gsxFolder, IPackageContentReader reader)
    {
        var scans = new ScanService(reader, new SqliteScanCache(Database), paths.CommunityPath, new OfficialPackages(paths.OfficialPath), gsxFolder);
        Scans?.Dispose();
        Paths = paths;
        GsxFolder = gsxFolder;
        Scans = scans;
    }

    public IReadOnlyList<CommunityEntryInfo> ReadCommunity() => new CommunityReader(Paths!.CommunityPath).Read();

    public void Dispose()
    {
        Scans?.Dispose();
        Database.Dispose();
    }
}
