using AddonManager.Core.State;
using AddonManager.Sim2020;

namespace AddonManager.Cli;

/// <summary>Finds the install for a CLI command: the stored Packages root wins over <c>UserCfg.opt</c> (D14).</summary>
internal static class InstallPaths
{
    /// <summary>Null when no install is found; the reason is then written to <paramref name="output"/>.</summary>
    public static SimPaths? Resolve(UserState state, TextWriter output)
    {
        if (state.PackagesRoot is { } root)
        {
            var picked = InstallLocator.FromPackagesRoot(root);
            if (picked is null)
                output.WriteLine($"The stored Packages root {root} has no Community folder.");
            return picked;
        }

        var paths = InstallLocator.Locate(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify));
        if (paths is null)
            output.WriteLine("No MSFS 2020 install found (no UserCfg.opt with InstalledPackagesPath).");
        return paths;
    }
}
