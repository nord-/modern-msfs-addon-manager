namespace AddonManager.Infrastructure;

/// <summary>Where the app keeps its files. Reading these never creates anything.</summary>
public static class AppPaths
{
    private const string AppFolder = "ModernMsfsAddonManager";

    /// <summary>The folder of <see cref="DatabaseFile"/>.</summary>
    public static string DataDirectory { get; } = Path.Combine(Folder(Environment.SpecialFolder.LocalApplicationData), AppFolder);

    /// <summary>The one SQLite file holding the user's data and the scan cache.</summary>
    public static string DatabaseFile { get; } = Path.Combine(DataDirectory, "userdata.db");

    private static string Folder(Environment.SpecialFolder folder)
        => Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
}
