using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Cards;

/// <summary>
/// How one card shows (spec 6.4, 6.5): section, tab, ICAO codes, name and image. A stored card's section, tab, ICAO codes, name and
/// image are the user's or were fixed when the card was made (rule R6); the board only computes what is still unset.
/// </summary>
internal sealed record CardLayout(
    Category Section,
    CardTab Tab,
    IReadOnlyList<AirportCode> Codes,
    IReadOnlyList<AirportCode> RemovedCodes,
    string? AirportName,
    string Name,
    string? ImagePath)
{
    /// <summary>The layout of a card; <paramref name="packages"/> must be sorted by name and not empty. <paramref name="card"/> is null for a provisional card, which sits in <paramref name="provisionalSection"/>.</summary>
    public static CardLayout Compute(CardRecord? card, IReadOnlyList<PackageSummary> packages, ContentAnalysis analysis, Category provisionalSection)
    {
        var section = card?.Section ?? provisionalSection;
        var tab = card?.Tab ?? (section == Category.Scenery ? CardTab.Landscapes : CardTab.Other);
        IReadOnlyList<AirportCode> codes = card is null ? [] : [.. card.Codes];
        IReadOnlyList<AirportCode> removed = card is null ? [] : [.. card.RemovedCodes];

        // The analysis knows only present content; a Package that is now Not found still carries the names it was last seen with.
        var airportName = codes.Count == 1
            ? analysis.Airports.TryGetValue(codes[0], out var airport) && !string.IsNullOrWhiteSpace(airport.Name)
                ? airport.Name
                : packages.SelectMany(p => p.Airports).FirstOrDefault(r => r.Code == codes[0] && !string.IsNullOrWhiteSpace(r.Name))?.Name
            : null;

        // The user's name and image always win (rule R6); the rest is the fallback chain of spec 6.5.
        // GSX profiles never name a card or give it an image (spec 6.5).
        var sources = packages.Where(p => p.Kind != PackageKind.GsxProfile).ToList();

        var name = card?.Name
            ?? SceneryName(section, codes, airportName)
            ?? AircraftName(section, sources, analysis)
            ?? (sources.Count > 0 ? sources[0].DisplayName : ProfileName(codes, packages));
        var image = card?.ImagePath ?? sources.FirstOrDefault(p => p.ThumbnailPath is not null)?.ThumbnailPath;
        return new CardLayout(section, tab, codes, removed, airportName, name, image);
    }

    /// <summary>The display name of a Sim-managed Package: its manifest title, else its folder name.</summary>
    internal static string TitleOrName(PackageContent content)
        => !string.IsNullOrWhiteSpace(content.Manifest.Title) ? content.Manifest.Title : content.Name.Value;

    /// <summary>
    /// The name of a card of GSX profiles only that the one-ICAO rule did not name (spec 6.5): its ICAO codes, else, for a card with none (moved out
    /// of Scenery, or provisional), the distinct ICAO codes of its profiles, sorted; never empty.
    /// </summary>
    private static string ProfileName(IReadOnlyList<AirportCode> codes, IReadOnlyList<PackageSummary> profiles)
    {
        IEnumerable<AirportCode> shown = codes.Count > 0 ? codes : profiles.SelectMany(p => p.OwnCodes).Distinct().OrderBy(c => c.Value, StringComparer.Ordinal);
        var joined = string.Join(", ", shown.Select(c => c.Value));
        return joined.Length > 0 ? joined : profiles[0].DisplayName;
    }

    /// <summary>A Scenery card with exactly one ICAO code whose Airport has a known name: "EHAM Schiphol".</summary>
    private static string? SceneryName(Category section, IReadOnlyList<AirportCode> codes, string? airportName)
        => section == Category.Scenery && codes.Count == 1 && airportName is not null ? $"{codes[0].Value} {airportName}" : null;

    /// <summary>
    /// An Aircraft card: its first Provider or Mod Package by name, else its first locked base, else the first Sim-managed locked base of
    /// its Containers, else its first Container by name.
    /// </summary>
    private static string? AircraftName(Category section, IReadOnlyList<PackageSummary> packages, ContentAnalysis analysis)
    {
        if (section != Category.Aircraft)
        {
            return null;
        }

        var provider = packages.FirstOrDefault(p => p.Role is AircraftRole.Provider or AircraftRole.Mod);
        var lockedBase = packages.FirstOrDefault(p => p.Role == AircraftRole.LockedBase);
        var containers = packages.SelectMany(p => p.Containers).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        // The built-in base with the smallest name across all of the card's Containers.
        var builtInBase = containers
            .SelectMany(c => analysis.Aircraft.Groups.TryGetValue(c, out var group) ? group.LockedBases : [])
            .Where(b => b.Source == PackageSource.SimManaged)
            .OrderBy(b => b.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return provider?.DisplayName
            ?? lockedBase?.DisplayName
            ?? (builtInBase is null ? null : TitleOrName(builtInBase))
            ?? containers.FirstOrDefault();
    }
}
