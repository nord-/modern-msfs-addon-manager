using AddonManager.Core.Packages;

namespace AddonManager.Core.Content;

/// <summary>The facts read from one Package folder. <c>Path</c> is normalised; <c>BglSectionTypes</c> is sorted and distinct.</summary>
/// <param name="AirplaneFolders">Folder names under <c>SimObjects\Airplanes</c> seen in the Package.</param>
/// <param name="EncryptedAirplaneFolders">The subset of <c>AirplaneFolders</c> without an <c>aircraft.cfg</c>, when the Package has a <c>.fsarchive</c> file anywhere (the sim's archives sit in the Package root or <c>SimObjects</c>, never inside an airplane folder).</param>
public sealed record PackageContent(
    PackageName Name,
    string Path,
    PackageSource Source,
    ChangeMarker Marker,
    Manifest Manifest,
    bool HasBgl,
    IReadOnlyList<uint> BglSectionTypes,
    int UnreadableBglCount,
    IReadOnlyList<AirportRecord> Airports,
    bool IsAirTraffic,
    IReadOnlyList<AircraftConfig> AircraftConfigs,
    IReadOnlyList<string> AirplaneFolders,
    IReadOnlyList<string> EncryptedAirplaneFolders,
    string? ThumbnailPath)
{
    /// <summary>Package-relative paths (<c>\</c> separators, sorted) of the <c>.ini</c> and <c>.py</c> files whose names yield an Airport code: the GSX profiles the Package ships.</summary>
    public IReadOnlyList<string> GsxFiles { get; init; } = [];

    /// <summary>
    /// The airport records that derivation uses (C6: the raw <c>Airports</c> stay as read). A record at exactly 0,0 is a
    /// template (SayIntentions ships one with code "ICAO") and is not an Airport; every rule reads records through here.
    /// </summary>
    public IReadOnlyList<AirportRecord> UsableAirports()
        => Airports.Any(a => a.Position is { Latitude: 0, Longitude: 0 })
            ? Airports.Where(a => a.Position is not { Latitude: 0, Longitude: 0 }).ToList()
            : Airports;
}
