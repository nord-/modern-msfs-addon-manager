using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Infrastructure;
using AddonManager.Sim2020;

namespace AddonManager.App.Tests.Session;

/// <summary>
/// A temp "sim machine": a Packages root with Community and Official\Steam, Libraries, a GSX folder and a database file. Every folder and
/// file it creates or changes gets a write time one hour ago, so the real reader sees the Packages as quiet (settled) and caches them.
/// Junctions are made with <see cref="JunctionFileSystem"/>, which only tests may use.
/// </summary>
public sealed class SimMachine : IDisposable
{
    private static readonly TimeSpan Age = TimeSpan.FromHours(1);

    private readonly TempFolder temp = new();
    private readonly JunctionFileSystem links = new();

    public SimMachine()
    {
        PackagesRoot = Directory.CreateDirectory(Path.Combine(temp.Root, "Packages")).FullName;
        Community = Directory.CreateDirectory(Path.Combine(PackagesRoot, "Community")).FullName;
        Directory.CreateDirectory(Path.Combine(PackagesRoot, "Official", "Steam"));
        Gsx = Directory.CreateDirectory(Path.Combine(temp.Root, "Gsx")).FullName;
        Directory.CreateDirectory(Path.Combine(temp.Root, "data"));
        DatabaseFile = Path.Combine(temp.Root, "data", "userdata.db");
        Paths = InstallLocator.FromPackagesRoot(PackagesRoot)!;
    }

    public string Root => temp.Root;

    public string PackagesRoot { get; }

    public string Community { get; }

    public string Gsx { get; }

    public string DatabaseFile { get; }

    public SimPaths Paths { get; }

    /// <summary>Creates a Library folder (empty) and returns its path.</summary>
    public string AddLibrary(string name = "Lib")
    {
        var path = Directory.CreateDirectory(Path.Combine(temp.Root, name)).FullName;
        Quiet(path);
        return path;
    }

    /// <summary>Creates a Package folder with a manifest in a Library and returns its path.</summary>
    public string AddPackage(string name, string library = "Lib", string contentType = "SCENERY")
    {
        var path = Directory.CreateDirectory(Path.Combine(temp.Root, library, name)).FullName;
        var manifest = Path.Combine(path, "manifest.json");
        File.WriteAllText(manifest, $$"""{"title":"{{name}}","content_type":"{{contentType}}","package_version":"1.0.0"}""");
        Quiet(manifest);
        Quiet(path);
        Quiet(Path.GetDirectoryName(path)!);
        return path;
    }

    public void Link(string name, string library = "Lib")
    {
        links.CreateJunction(Path.Combine(Community, name), Path.Combine(temp.Root, library, name));
        Quiet(Community);
    }

    public void Unlink(string name)
    {
        links.RemoveLink(Path.Combine(Community, name));
        Quiet(Community);
    }

    public void WriteGsx(string fileName)
    {
        var file = Path.Combine(Gsx, fileName);
        File.WriteAllText(file, "[x]");
        Quiet(file);
        Quiet(Gsx);
    }

    /// <summary>Stores a state with one Library in the database, as if a first run had been completed earlier.</summary>
    public void SeedLibrary(string library)
    {
        using var database = UserDataDatabase.Open(DatabaseFile, UserDataAccess.Full);
        var store = new SqliteUserStateStore(database);
        var state = store.Load();
        state.Libraries.Add(new LibraryRoot(library, LibraryRoot.AllSections));
        store.Save(state);
    }

    public IReadOnlyList<string> ListCommunity()
        => [.. Directory.EnumerateFileSystemEntries(Community)
            .Select(p => $"{System.IO.Path.GetFileName(p)} -> {JunctionFileSystem.ReadLinkTarget(p)}")
            .Order()];

    public IReadOnlyList<string> ListGsx()
        => [.. new DirectoryInfo(Gsx).EnumerateFileSystemInfos()
            .Select(i => $"{i.Name}|{(i as FileInfo)?.Length}|{i.LastWriteTimeUtc.Ticks}")
            .Order()];

    private static void Quiet(string path)
    {
        var when = DateTime.UtcNow - Age;
        if (File.Exists(path))
        {
            File.SetCreationTimeUtc(path, when);
            File.SetLastWriteTimeUtc(path, when);
        }
        else
        {
            Directory.SetCreationTimeUtc(path, when);
            Directory.SetLastWriteTimeUtc(path, when);
        }
    }

    public void Dispose() => temp.Dispose();
}
