using AddonManager.Core.Linking;
using AddonManager.Infrastructure;
using AddonManager.Sim2020;

namespace AddonManager.App.Tests;

public class AppServicesTests
{
    [Fact]
    public void Opening_creates_only_the_database()
    {
        using var temp = new TempFolder();
        var package = temp.MakePackage("Addons/pkg");
        var community = Directory.CreateDirectory(Path.Combine(temp.Root, "Community")).FullName;
        new JunctionFileSystem().CreateJunction(Path.Combine(community, "pkg"), package);
        var gsx = Directory.CreateDirectory(Path.Combine(temp.Root, "Gsx")).FullName;
        File.WriteAllText(Path.Combine(gsx, "eham.ini"), "[x]");
        var databaseFile = Path.Combine(temp.Root, "data", "userdata.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databaseFile)!);

        var communityBefore = ListCommunity(community);
        var gsxBefore = ListFolder(gsx);

        using (var services = AppServices.Open(databaseFile))
        {
            services.UseSim(new SimPaths(null, temp.Root, community, null), gsx);
            Assert.Single(services.ReadCommunity());
        }

        Assert.True(File.Exists(databaseFile));
        Assert.Equal(communityBefore, ListCommunity(community));
        Assert.Equal(gsxBefore, ListFolder(gsx));
    }

    [Fact]
    public void UseSim_composes_the_write_path_with_the_set_sim()
    {
        using var temp = new TempFolder();
        var community = Directory.CreateDirectory(Path.Combine(temp.Root, "Community")).FullName;
        var gsx = Directory.CreateDirectory(Path.Combine(temp.Root, "Gsx")).FullName;
        var databaseFile = Path.Combine(temp.Root, "data", "userdata.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databaseFile)!);
        var sim = new StoppedSim();

        using var services = AppServices.Open(databaseFile);
        Assert.Null(services.Links);
        Assert.IsType<SimProcessMonitor>(services.Sim);
        services.Sim = sim;
        services.UseSim(new SimPaths(null, temp.Root, community, null), gsx);

        Assert.NotNull(services.Links);
        Assert.Same(sim, services.Links!.Sim);
        Assert.NotNull(services.Links.Applier);
        Assert.NotNull(services.Links.Sync);
        Assert.NotNull(services.Links.Remover);
    }

    private sealed class StoppedSim : ISimProcessMonitor
    {
        public bool IsRunning => false;
    }

    private static List<string> ListCommunity(string community) =>
        Directory.EnumerateFileSystemEntries(community)
            .Select(p => $"{Path.GetFileName(p)} -> {JunctionFileSystem.ReadLinkTarget(p)}")
            .Order().ToList();

    private static List<string> ListFolder(string folder) =>
        new DirectoryInfo(folder).EnumerateFileSystemInfos()
            .Select(i => $"{i.Name}|{(i as FileInfo)?.Length}|{i.LastWriteTimeUtc.Ticks}")
            .Order().ToList();
}
