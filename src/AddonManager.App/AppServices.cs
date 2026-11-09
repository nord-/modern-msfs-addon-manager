using AddonManager.Core.Community;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Linking;
using AddonManager.Core.State;
using AddonManager.Core.Sync;
using AddonManager.Infrastructure;
using AddonManager.Sim2020;
using AddonManager.Sim2020.Content;

namespace AddonManager.App;

/// <summary>The composed write path: every Community change goes through these.</summary>
public sealed record AppLinks(ChangeApplier Applier, SyncCoordinator Sync, PackageRemover Remover, ISimProcessMonitor Sim);

/// <summary>
/// The composition root: the only place the app creates services. It opens the user database and
/// the scan service and reads Community; it creates the link writer only here, for <see cref="ChangeApplier"/>, and never a GSX writer.
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

    /// <summary>The write path, composed by <c>UseSim</c> from the current <see cref="Store"/> and <see cref="Sim"/>; null before it.</summary>
    public AppLinks? Links { get; private set; }

    /// <summary>The sim process monitor; tests set it before <c>UseSim</c>.</summary>
    internal ISimProcessMonitor Sim { get; set; } = new SimProcessMonitor();

    /// <summary>Creates the link writer for <c>ChangeApplier</c>; tests set it before <c>UseSim</c> to make a link change fail.</summary>
    internal Func<ILinkFileSystem> LinkFileSystem { get; set; } = () => new JunctionFileSystem();

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

        var community = new CommunityReader(paths.CommunityPath);
        var applier = new ChangeApplier(LinkFileSystem(), community, Store, Sim, paths.CommunityPath);
        Links = new AppLinks(applier, new SyncCoordinator(applier, community, Store), new PackageRemover(applier, community, Sim, Store), Sim);
    }

    public IReadOnlyList<CommunityEntryInfo> ReadCommunity() => new CommunityReader(Paths!.CommunityPath).Read();

    public void Dispose()
    {
        Scans?.Dispose();
        Database.Dispose();
    }
}
