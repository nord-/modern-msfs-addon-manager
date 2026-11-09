using AddonManager.Core.Cards;
using AddonManager.Core.Content;

namespace AddonManager.App.ViewModels;

/// <summary>The lines of the banner above the wall, in the order the spec shows them.</summary>
public static class BannerText
{
    public static IReadOnlyList<string> Lines(Banner? banner, ScanProgress? progress, string? error, string gsxFolder, bool linksWaiting = false)
    {
        var lines = new List<string>();
        if (error is not null)
            lines.Add(error);
        if (progress is not null)
            lines.Add($"Scanning… {progress.Done} of {progress.Total}");
        if (banner is null)
            return lines;

        if (banner.Grouping > 0)
            lines.Add($"Grouping cards… ({banner.Grouping})");
        if (banner.Broken.Count > 0 || banner.Stray.Count > 0)
            lines.Add($"Community: {banner.Broken.Count} Broken, {banner.Stray.Count} Stray");
        if (linksWaiting)
            lines.Add("Some links wait for MSFS to close. Close MSFS, then press F5.");
        lines.AddRange(banner.LibrariesNotFound.Select(l => $"Library not found: {l.Path}"));
        if (banner.Unreadable.Count > 0)
            lines.Add("Unreadable: " + string.Join(", ", banner.Unreadable.Select(n => n.Value)));
        if (banner.OnButNotShown.Count == 1)
            lines.Add($"1 package is on but not shown: {banner.OnButNotShown[0].Value}");
        else if (banner.OnButNotShown.Count > 1)
            lines.Add($"{banner.OnButNotShown.Count} packages are on but not shown: " + string.Join(", ", banner.OnButNotShown.Select(n => n.Value)));
        if (banner.GsxFolderMissing)
            lines.Add($"GSX folder missing: {gsxFolder}");
        if (banner.Pending > 0)
            lines.Add($"{banner.Pending} changes wait for the next sim start");
        return lines;
    }
}
