namespace AddonManager.Core.Gsx;

public enum GsxCopyVerdict { Ready, ConfirmTwoProfiles, Refused }

/// <summary>What copying a shipped profile would do; <c>Copy</c> and <c>Skip</c> are source paths, the <c>.ini</c> first.</summary>
public sealed record GsxCopyCheck(GsxCopyVerdict Verdict, string? Reason, IReadOnlyList<string> Copy, IReadOnlyList<string> Skip);

/// <summary>The rules of copying a shipped profile into the GSX folder (plan 4, D5). Pure: the caller supplies a fresh read of the folder.</summary>
public static class GsxCopy
{
    private static readonly GsxCopyCheck NoFiles = new(GsxCopyVerdict.Refused, null, [], []);

    public static GsxCopyCheck Check(ShippedProfile shipped, GsxFolderContent folder, int activeProfilesAtCode)
    {
        if (!folder.Found)
        {
            return NoFiles with { Reason = "The GSX folder was not found" };
        }

        // An existing .off would be switched back on by a new .ini of the same name, so it refuses the copy like an existing .ini.
        var stem = Path.GetFileNameWithoutExtension(shipped.IniPath);
        foreach (var taken in new[] { stem + ".ini", stem + ".off" })
        {
            if (folder.FileNames.Contains(taken))
            {
                return NoFiles with { Reason = $"{taken} already exists in the GSX folder" };
            }
        }

        var scripts = shipped.ScriptPaths.ToLookup(script => folder.FileNames.Contains(Path.GetFileName(script)));
        IReadOnlyList<string> copy = [shipped.IniPath, .. scripts[false]];
        IReadOnlyList<string> skip = [.. scripts[true]];
        return activeProfilesAtCode > 0
            ? new GsxCopyCheck(GsxCopyVerdict.ConfirmTwoProfiles, $"Copying gives Two profiles for {shipped.Code}", copy, skip)
            : new GsxCopyCheck(GsxCopyVerdict.Ready, null, copy, skip);
    }
}
