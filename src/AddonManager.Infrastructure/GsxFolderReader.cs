using AddonManager.Core.Gsx;

namespace AddonManager.Infrastructure;

/// <summary>Lists the GSX profile folder (spec 12: "GSX folder access"). Read on every scan run and never cached (plan 4, D2).</summary>
public static class GsxFolderReader
{
    /// <summary>Reads the top level of <paramref name="folder"/>; a folder that is missing or cannot be listed is <see cref="GsxFolderContent.Missing"/>.</summary>
    public static GsxFolderContent Read(string folder) => Read(folder, Directory.EnumerateFiles);

    internal static GsxFolderContent Read(string folder, Func<string, IEnumerable<string>> listFiles)
    {
        try
        {
            var names = listFiles(folder).Select(path => Path.GetFileName(path)).ToList();
            return GsxFolderContent.FromFiles(folder, names);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // DirectoryNotFoundException is an IOException: a folder deleted between the check and the listing lands here too.
            return GsxFolderContent.Missing;
        }
    }
}
