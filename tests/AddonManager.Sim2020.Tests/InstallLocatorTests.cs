namespace AddonManager.Sim2020.Tests;

public sealed class InstallLocatorTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mmam-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Roaming => Path.Combine(root, "Roaming");

    private string Local => Path.Combine(root, "Local");

    private string WriteUserCfg(string folder, string packagesPath)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "UserCfg.opt"),
            $"Version 72\r\n{{Graphics\r\n\tPreset Custom\r\n}}\r\nInstalledPackagesPath \"{packagesPath}\"\r\n");
        return packagesPath;
    }

    [Fact]
    public void Parses_quoted_path_with_spaces()
        => Assert.Equal(@"D:\MSFS Packages", InstallLocator.ParseInstalledPackagesPath("Version 72\r\nInstalledPackagesPath \"D:\\MSFS Packages\"\r\n"));

    [Fact]
    public void Parses_an_unquoted_path_with_leading_whitespace()
        => Assert.Equal(@"D:\MSFS", InstallLocator.ParseInstalledPackagesPath("  InstalledPackagesPath D:\\MSFS\n"));

    [Fact]
    public void Missing_line_returns_null() => Assert.Null(InstallLocator.ParseInstalledPackagesPath("Version 72"));

    [Fact]
    public void Finds_steam_install_and_official_folder()
    {
        var pkgs = WriteUserCfg(Path.Combine(Roaming, "Microsoft Flight Simulator"), Path.Combine(root, "MSFS Packages"));
        Directory.CreateDirectory(Path.Combine(pkgs, "Official", "Steam"));

        var paths = InstallLocator.Locate(Roaming, Local);

        Assert.NotNull(paths);
        Assert.Equal(Path.Combine(pkgs, "Community"), paths.CommunityPath);
        Assert.Equal(Path.Combine(pkgs, "Official", "Steam"), paths.OfficialPath);
        Assert.Equal(pkgs, paths.PackagesPath);
    }

    [Fact]
    public void Falls_back_to_store_install()
    {
        var pkgs = WriteUserCfg(Path.Combine(Local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache"), Path.Combine(root, "Store"));
        Directory.CreateDirectory(Path.Combine(pkgs, "Official", "OneStore"));

        var paths = InstallLocator.Locate(Roaming, Local);

        Assert.Equal(Path.Combine(pkgs, "Official", "OneStore"), paths!.OfficialPath);
    }

    [Fact]
    public void Missing_official_folder_gives_null_official_path()
    {
        WriteUserCfg(Path.Combine(Roaming, "Microsoft Flight Simulator"), Path.Combine(root, "Pkgs"));

        Assert.Null(InstallLocator.Locate(Roaming, Local)!.OfficialPath);
    }

    [Fact]
    public void Returns_null_when_no_usercfg_exists() => Assert.Null(InstallLocator.Locate(Roaming, Local));
}
