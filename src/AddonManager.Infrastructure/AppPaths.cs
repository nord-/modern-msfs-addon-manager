namespace AddonManager.Infrastructure;

/// <summary>Where the app keeps its files. Reading these never creates anything.</summary>
public static class AppPaths
{
    private const string AppFolder = "ModernMsfsAddonManager";

    public static string UserDataFile { get; } = Path.Combine(Folder(Environment.SpecialFolder.ApplicationData), AppFolder, "userdata.json");

    public static string CacheDirectory { get; } = Path.Combine(Folder(Environment.SpecialFolder.LocalApplicationData), AppFolder);

    public static string ScanCacheFile { get; } = Path.Combine(CacheDirectory, "scan-cache.db");

    private static string Folder(Environment.SpecialFolder folder)
        => Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
}
