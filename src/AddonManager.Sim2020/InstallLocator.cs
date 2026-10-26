namespace AddonManager.Sim2020;

/// <param name="OfficialPath">The folder of Sim-managed packages, or <c>null</c> when none is found.</param>
public sealed record SimPaths(string UserCfgPath, string PackagesPath, string CommunityPath, string? OfficialPath);

/// <summary>Finds the MSFS 2020 install from <c>UserCfg.opt</c> (spec 7.1 step 1).</summary>
public static class InstallLocator
{
    private const string Key = "InstalledPackagesPath";

    public static SimPaths? Locate(string roamingAppData, string localAppData)
    {
        string[] candidates =
        [
            Path.Combine(roamingAppData, "Microsoft Flight Simulator", "UserCfg.opt"),                                 // Steam
            Path.Combine(localAppData, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"), // Microsoft Store
        ];

        foreach (var userCfg in candidates.Where(File.Exists))
        {
            var packages = ParseInstalledPackagesPath(File.ReadAllText(userCfg));
            if (packages is null)
                continue;

            var official = new[] { "Steam", "OneStore" }
                .Select(store => Path.Combine(packages, "Official", store))
                .FirstOrDefault(Directory.Exists);
            return new SimPaths(userCfg, packages, Path.Combine(packages, "Community"), official);
        }

        return null;
    }

    public static string? ParseInstalledPackagesPath(string userCfgText)
    {
        foreach (var raw in userCfgText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > Key.Length && char.IsWhiteSpace(line[Key.Length]) && line.StartsWith(Key, StringComparison.OrdinalIgnoreCase))
                return line[Key.Length..].Trim().Trim('"');
        }
        return null;
    }
}
