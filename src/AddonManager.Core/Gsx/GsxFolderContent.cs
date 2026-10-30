using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Gsx;

/// <summary>A <c>.py</c> file in the GSX folder; it is only listed by Airport code, never parsed (spec 8).</summary>
public sealed record GsxScript(AirportCode Code, string Path);

/// <summary>What one read of the GSX folder found; it is never cached (plan 4, D2).</summary>
/// <param name="Found">False when the folder does not exist or cannot be listed.</param>
/// <param name="Unrecognised">File names of <c>.ini</c>, <c>.off</c> and <c>.py</c> files that yield no Airport code.</param>
public sealed record GsxFolderContent(
    bool Found,
    IReadOnlyList<GsxProfile> Profiles,
    IReadOnlyList<GsxScript> Scripts,
    IReadOnlyList<string> Unrecognised)
{
    public static readonly GsxFolderContent Missing = new(false, [], [], []);

    /// <summary>All file names in the folder, case-insensitive: the copy check and the shipped-profile flag read them.</summary>
    public IReadOnlySet<string> FileNames { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The pure part of reading the folder: pairs <c>.ini</c> and <c>.off</c> files into profiles and sorts the rest.</summary>
    public static GsxFolderContent FromFiles(string folder, IEnumerable<string> fileNames)
    {
        var names = fileNames.ToList();
        var profiles = new List<GsxProfile>();
        var scripts = new List<GsxScript>();
        var unrecognised = new List<string>();

        foreach (var file in names.Where(n => IsKind(n, GsxFileKind.Script)))
        {
            GsxFileName.TryParse(file, out _, out var code);
            if (code == default)
            {
                unrecognised.Add(file);
            }
            else
            {
                scripts.Add(new GsxScript(code, System.IO.Path.Combine(folder, file)));
            }
        }

        var stems = names
            .Where(n => !IsKind(n, GsxFileKind.Script) && GsxFileName.TryParse(n, out _, out _))
            .GroupBy(System.IO.Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase);
        foreach (var group in stems)
        {
            var ini = group.FirstOrDefault(n => IsKind(n, GsxFileKind.Profile));
            var off = group.FirstOrDefault(n => IsKind(n, GsxFileKind.Disabled));
            var main = ini ?? off!;
            GsxFileName.TryParse(main, out _, out var code);
            if (code == default)
            {
                unrecognised.AddRange(group);
                continue;
            }

            profiles.Add(new GsxProfile(
                new PackageName(System.IO.Path.GetFileNameWithoutExtension(main)),
                code,
                System.IO.Path.Combine(folder, main),
                Disabled: ini is null)
            {
                OffVariantPath = ini is not null && off is not null ? System.IO.Path.Combine(folder, off) : null,
            });
        }

        return new GsxFolderContent(
            true,
            profiles.OrderBy(p => p.Name.Value, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            scripts.OrderBy(s => System.IO.Path.GetFileName(s.Path), StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Path, StringComparer.OrdinalIgnoreCase).ToList(),
            unrecognised.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList())
        {
            FileNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static bool IsKind(string fileName, GsxFileKind kind)
        => GsxFileName.TryParse(fileName, out var actual, out _) && actual == kind;
}
