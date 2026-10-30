using AddonManager.Core.Packages;
using AddonManager.Infrastructure;

namespace AddonManager.Sim2020.Tests.Integration;

/// <summary>A fact that runs only on a machine with an MSFS 2020 install and <c>MMAM_SIM_TESTS=1</c>. CI never sets the variable.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SimFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "MMAM_SIM_TESTS";
    public const string LibrariesVariable = "MSFS_LIBRARIES";

    public SimFactAttribute()
    {
        if (!Enabled)
            Skip = $"Set {EnvironmentVariable}=1 to run tests against the local MSFS install.";
        else if (EnvironmentLibraries().Count == 0 && !HasStoredLibraries())
            Skip = $"Set {LibrariesVariable} (entries as for --library, separated by ';') or register Libraries in the app.";
    }

    public static bool Enabled => Environment.GetEnvironmentVariable(EnvironmentVariable) == "1";

    /// <summary>Libraries from MSFS_LIBRARIES: entries in the <c>--library</c> format separated by ';'. Invalid entries are skipped.</summary>
    public static List<LibraryRoot> EnvironmentLibraries()
        => (Environment.GetEnvironmentVariable(LibrariesVariable) ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => LibraryArgument.TryParse(e, out var library, out _) ? library : null)
            .OfType<LibraryRoot>()
            .ToList();

    private static bool HasStoredLibraries()
    {
        try
        {
            if (!File.Exists(AppPaths.DatabaseFile))
                return false;

            using var db = UserDataDatabase.Open(AppPaths.DatabaseFile, UserDataAccess.CacheOnly);
            return new SqliteUserStateStore(db).Load().Libraries.Count > 0;
        }
        catch (UserDataException)
        {
            return false;
        }
    }
}
