using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Gsx;

/// <summary>What the card wall shows for an Airport's GSX profile (spec 8).</summary>
public enum GsxMark { None, Installed, Missing, TwoProfiles }

/// <summary>A GSX profile shipped inside a Package (plan 4, D4). Paths are absolute. <c>InGsxFolder</c>: its <c>.ini</c> or <c>.off</c> name is already in the GSX folder.</summary>
public sealed record ShippedProfile(AirportCode Code, PackageName Package, string IniPath, IReadOnlyList<string> ScriptPaths, bool InGsxFolder);

/// <summary>The GSX facts of one Airport code. <c>Disabled</c>: paths of <c>.off</c> profiles and <c>.off</c> variants; <c>Scripts</c>: paths of <c>.py</c> files.</summary>
public sealed record GsxAirport(
    AirportCode Code,
    GsxMark Status,
    IReadOnlyList<PackageName> Active,
    IReadOnlyList<string> Disabled,
    IReadOnlyList<string> Scripts,
    IReadOnlyList<ShippedProfile> Shipped,
    string SearchLink);

/// <summary>The status per Airport (spec 8). Pure.</summary>
public static class GsxStatus
{
    public const string SearchTemplate = "https://flightsim.to/others/gsx-pro/search/{code}";

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    public static string SearchLink(AirportCode code) => SearchTemplate.Replace("{code}", code.Value.ToLowerInvariant());

    /// <summary>
    /// Active profiles are Found profile Packages that are not disabled, whatever their card. No active profile is Missing, one is Installed,
    /// more are Two profiles; scripts and shipped profiles never change it. Scripts and shipped profiles of other codes are left out, so the
    /// caller may pass all of them.
    /// </summary>
    public static GsxAirport For(AirportCode code, IReadOnlyList<PackageSummary> profiles, IReadOnlyList<GsxScript> scripts, IReadOnlyList<ShippedProfile> shipped)
    {
        var found = profiles.Where(p => p.Presence == Presence.Found && p.Codes.Contains(code)).ToList();
        var active = found.Where(p => !p.Disabled).Select(p => p.Name).OrderBy(n => n.Value, Names).ToList();
        var disabled = found
            .SelectMany(p => p.Disabled ? [p.Path] : p.OffVariantPath is { } off ? new[] { off } : [])
            .Order(Names)
            .ToList();
        var status = active.Count switch
        {
            0 => GsxMark.Missing,
            1 => GsxMark.Installed,
            _ => GsxMark.TwoProfiles,
        };

        return new GsxAirport(
            code,
            status,
            active,
            disabled,
            [.. scripts.Where(s => s.Code == code).Select(s => s.Path).Order(Names)],
            [.. shipped.Where(s => s.Code == code).OrderBy(s => s.Package.Value, Names).ThenBy(s => s.IniPath, Names)],
            SearchLink(code));
    }
}
