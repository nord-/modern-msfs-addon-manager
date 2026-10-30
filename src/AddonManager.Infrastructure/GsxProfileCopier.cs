using AddonManager.Core.Gsx;

namespace AddonManager.Infrastructure;

/// <summary>
/// What a copy did, with the target paths in the GSX folder. <c>Refusal</c> is set when something stopped the copy; <c>NeedsConfirmation</c>
/// is true when the only thing in the way was the Two profiles question (nothing was copied).
/// </summary>
public sealed record GsxCopyResult(IReadOnlyList<string> Copied, IReadOnlyList<string> Skipped, string? Refusal, bool NeedsConfirmation);

/// <summary>The one write of plan 4: an explicit copy of a shipped profile into the GSX folder, which never overwrites (spec 8, plan 4 D5).</summary>
public static class GsxProfileCopier
{
    /// <summary>
    /// Reads the GSX folder fresh, counts the active profiles at the code from that read and checks the copy with <see cref="GsxCopy"/>. Copies
    /// the <c>.ini</c> first, then its scripts, with overwrite off; a failing copy ends it with a refusal naming the file and leaves what was already copied.
    /// </summary>
    public static GsxCopyResult Copy(ShippedProfile shipped, string gsxFolder, bool confirmed)
    {
        var folder = GsxFolderReader.Read(gsxFolder);
        var active = folder.Profiles.Count(p => p.Code == shipped.Code && !p.Disabled);
        var check = GsxCopy.Check(shipped, folder, active);
        var skipped = check.Skip.Select(source => Path.Combine(gsxFolder, Path.GetFileName(source))).ToList();

        if (check.Verdict == GsxCopyVerdict.Refused)
        {
            return new GsxCopyResult([], [], check.Reason, false);
        }

        if (check.Verdict == GsxCopyVerdict.ConfirmTwoProfiles && !confirmed)
        {
            return new GsxCopyResult([], skipped, check.Reason, true);
        }

        var copied = new List<string>();
        foreach (var source in check.Copy)
        {
            var target = Path.Combine(gsxFolder, Path.GetFileName(source));
            try
            {
                File.Copy(source, target, overwrite: false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The target appeared meanwhile, the source is gone, or the folder is read-only.
                return new GsxCopyResult(copied, skipped, $"Could not copy {Path.GetFileName(source)}: {e.Message}", false);
            }

            copied.Add(target);
        }

        return new GsxCopyResult(copied, skipped, null, false);
    }
}
