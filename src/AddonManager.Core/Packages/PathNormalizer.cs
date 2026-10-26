namespace AddonManager.Core.Packages;

/// <summary>Compares link targets and folders as resolved, normalised paths (spec 6.2).</summary>
public static class PathNormalizer
{
    private const string NtPrefix = @"\??\";
    private const string LongPrefix = @"\\?\";

    public static string Normalize(string path, string? baseDirectory = null)
    {
        var p = path.Replace('/', '\\');

        if (StartsWith(p, NtPrefix + @"UNC\") || StartsWith(p, LongPrefix + @"UNC\"))
        {
            p = @"\\" + p[8..];
        }
        else if (StartsWith(p, NtPrefix) || StartsWith(p, LongPrefix))
        {
            var rest = p[4..];
            if (StartsWith(rest, "Volume{"))
                return TrimTrailingSeparator(LongPrefix + rest);
            p = rest;
        }

        p = baseDirectory is null ? Path.GetFullPath(p) : Path.GetFullPath(p, baseDirectory);
        return TrimTrailingSeparator(p);
    }

    public static bool AreEqual(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or lies beneath it.</summary>
    public static bool IsUnder(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase))
            return true;
        var prefix = r.EndsWith('\\') ? r : r + '\\';
        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWith(string s, string prefix) => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static string TrimTrailingSeparator(string p)
    {
        var trimmed = p.TrimEnd('\\');
        // A drive root keeps its separator ("C:\").
        if (trimmed.Length == 2 && trimmed[1] == ':')
            return trimmed + '\\';
        return trimmed.Length == 0 ? p : trimmed;
    }
}
