using System.Text.Json;

namespace AddonManager.Sim2020.Content;

/// <summary>Reads the file list of a Package from its <c>layout.json</c>, or by walking the folder when there is none. Read-only.</summary>
public static class LayoutReader
{
    /// <summary>
    /// The <c>content[].path</c> values with <c>\</c> separators (entries outside the Package or with invalid characters dropped), or null when <c>layout.json</c> is missing or not valid.
    /// An <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> from opening an existing file propagates.
    /// </summary>
    public static IReadOnlyList<string>? Read(string packagePath)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(Path.Combine(packagePath, "layout.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        using (stream)
        {
            try
            {
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Array)
                    return null;

                var paths = new List<string>();
                foreach (var item in content.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("path", out var path)
                        && path.ValueKind == JsonValueKind.String
                        && path.GetString() is { Length: > 0 } text
                        && text.Replace('/', '\\') is var relative
                        && IsInsidePackage(relative))
                        paths.Add(relative);
                }
                return paths;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// False for an entry that is rooted, has a <c>..</c> segment or contains an invalid path character:
    /// such a path would either throw when opened or read a file outside the Package.
    /// </summary>
    private static bool IsInsidePackage(string relative) =>
        !Path.IsPathRooted(relative)
        && relative.IndexOfAny(Path.GetInvalidPathChars()) < 0
        && !relative.Split('\\').Contains("..");

    /// <summary>
    /// <see cref="Read"/>, or when that is null a recursive walk (reparse points skipped) as relative paths.
    /// Throws when the Package root can't be listed; inaccessible subfolders are skipped.
    /// </summary>
    public static IReadOnlyList<string> ListFiles(string packagePath)
    {
        if (Read(packagePath) is { } listed)
            return listed;

        return ListDiskFiles(packagePath);
    }

    /// <summary>A recursive walk of the folder (reparse points skipped) as relative paths, whatever <c>layout.json</c> lists. Throws when the Package root can't be listed.</summary>
    public static IReadOnlyList<string> ListDiskFiles(string packagePath)
    {
        var files = new List<string>();
        Walk(new DirectoryInfo(packagePath), "", files, isRoot: true);
        return files;
    }

    private static void Walk(DirectoryInfo folder, string prefix, List<string> files, bool isRoot)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = folder.EnumerateFileSystemInfos().ToList();
        }
        catch (Exception e) when (!isRoot && e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            try
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;
                var relative = prefix + entry.Name;
                if (entry is DirectoryInfo sub)
                    Walk(sub, relative + "\\", files, isRoot: false);
                else
                    files.Add(relative);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Below the root, an entry that vanished (FileNotFound/DirectoryNotFound are IOExceptions) or can't be read is skipped.
            }
        }
    }
}
