using AddonManager.Core.Packages;

namespace AddonManager.Core.Content;

/// <summary>Everything derived from the scanned content in one pass (content rule C6). <c>Categories</c> is keyed by normalised path, case-insensitively.</summary>
public sealed record ContentAnalysis(
    IReadOnlyDictionary<string, Category> Categories,
    IReadOnlyList<AirportLink> AirportLinks,
    IReadOnlyDictionary<AirportCode, Airport> Airports,
    IReadOnlyList<BuiltInReplacement> ReplacesBuiltIn,
    AircraftIndex Aircraft,
    IReadOnlyList<PackageContent> Excluded);

/// <summary>The single entry point that turns scanned content into categories, Airport links, Airports and the aircraft index.</summary>
public static class ContentAnalyzer
{
    /// <summary>
    /// <paramref name="overrides"/> and <paramref name="remembered"/> come from the cards (<c>CardLinks</c>); a scan knows neither,
    /// so callers that build a board re-analyse with the current cards (<c>BoardInput.Reanalyze</c>).
    /// </summary>
    public static ContentAnalysis Analyze(
        IReadOnlyList<PackageContent> contents,
        ContentSettings settings,
        AirportLinkOverrides? overrides = null,
        IReadOnlyList<AirportRecord>? remembered = null)
    {
        var aircraft = AircraftIndex.Build(contents);
        var links = AirportLinker.Link(contents, settings, overrides ?? AirportLinkOverrides.None, remembered);
        var airports = AirportCatalog.Build(contents, links, settings, remembered);

        var categories = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in contents)
        {
            categories[c.Path] = CategoryRules.Final(c.Manifest.ContentType, c.HasBgl, c.IsAirTraffic);
        }

        var excluded = contents
            .Where(c => c.UsableAirports().Count > 0 && AirportLinker.IsExcluded(c, settings))
            .OrderBy(c => c.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ContentAnalysis(categories, links, airports, AirportCatalog.ReplacesBuiltIn(contents, links, airports, settings), aircraft, excluded);
    }
}
