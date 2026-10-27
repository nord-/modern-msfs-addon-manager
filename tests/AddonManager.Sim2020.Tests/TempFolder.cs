namespace AddonManager.Sim2020.Tests;

/// <summary>
/// A temporary folder that can hold junctions. Dispose removes links without following them,
/// because a non-elevated recursive delete throws on a tree that contains a junction.
/// </summary>
public sealed class TempFolder : IDisposable
{
    public string Root { get; } = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "mmam-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>Creates a folder with an empty <c>manifest.json</c> and returns its full path.</summary>
    public string MakePackage(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "manifest.json"), "{}");
        return path;
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
            return;
        RemoveLinks(Root);
        Directory.Delete(Root, recursive: true);
    }

    private static void RemoveLinks(string folder)
    {
        foreach (var sub in Directory.EnumerateDirectories(folder, "*", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            if (new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint))
                Directory.Delete(sub, recursive: false);
            else
                RemoveLinks(sub);
        }
    }
}
