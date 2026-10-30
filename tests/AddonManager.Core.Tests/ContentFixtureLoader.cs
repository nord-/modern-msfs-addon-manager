using System.Text.Json;
using System.Text.Json.Serialization;
using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests;

/// <summary>Loads a content fixture written by <c>AddonManager.Cli export-content</c>.</summary>
internal static class ContentFixtureLoader
{
    // A constant, never the real environment variable, so tests behave the same in CI.
    private const string UserProfile = @"C:\Users\user";

    private static readonly ChangeMarker Marker = new(0, 0, null, null);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    // The GSX folder of the exporting machine is never in a fixture (names only); this stands in for it, like UserProfile does for paths.
    private const string GsxFolder = @"C:\Users\user\AppData\Roaming\Virtuali\GSX\MSFS";

    public static IReadOnlyList<PackageContent> Load(string name) => Read(name).Packages.Select(ToContent).ToList();

    /// <summary>The GSX folder the fixture was exported with, rebuilt by the same pairing code the reader runs; missing when it was not found or the fixture has none.</summary>
    public static GsxFolderContent LoadGsx(string name)
        => Read(name).GsxFolder is { Found: true } folder ? GsxFolderContent.FromFiles(GsxFolder, folder.Files) : GsxFolderContent.Missing;

    private static FixtureFile Read(string name)
        => JsonSerializer.Deserialize<FixtureFile>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json")), Options)!;

    private static PackageContent ToContent(PackageDto p)
    {
        var path = PathNormalizer.Normalize(p.Path.Replace("%USERPROFILE%", UserProfile, StringComparison.OrdinalIgnoreCase));

        // An excluded Package lists the codes nobody links to as plain codes: records without name or position.
        var records = (p.Airports ?? [])
            .Select(a => new AirportRecord(Code(a.Code), a.Name, a.Lat is { } lat && a.Lon is { } lon ? new GeoPoint(lat, lon) : null))
            .Concat((p.Codes ?? []).Select(c => new AirportRecord(Code(c), null, null)))
            .ToList();

        return new PackageContent(
            new PackageName(p.Name),
            path,
            p.Source,
            Marker,
            new Manifest(p.Title, p.Creator, p.ContentType, p.PackageVersion, p.Dependencies),
            p.HasBgl,
            p.BglSectionTypes,
            p.UnreadableBgl,
            records,
            p.IsAirTraffic,
            p.Aircraft,
            p.AirplaneFolders,
            p.EncryptedAirplaneFolders,
            p.HasThumbnail ? Path.Combine(path, "ContentInfo", p.Name, "Thumbnail.jpg") : null)
        {
            GsxFiles = p.GsxFiles ?? [],
        };
    }

    private static AirportCode Code(string text)
        => AirportCode.TryParse(text, out var code) ? code : throw new InvalidDataException($"Bad airport code in fixture: {text}");

    private sealed record FixtureFile(List<PackageDto> Packages, GsxFolderDto? GsxFolder = null);

    private sealed record GsxFolderDto(bool Found, List<string> Files);

    private sealed record AirportDto(string Code, string? Name, double? Lat, double? Lon);

    private sealed record PackageDto(
        string Name,
        string Path,
        PackageSource Source,
        string? Title,
        string? Creator,
        string? ContentType,
        string? PackageVersion,
        List<string> Dependencies,
        bool HasBgl,
        List<uint> BglSectionTypes,
        int UnreadableBgl,
        List<AirportDto>? Airports,
        List<string>? Codes,
        bool IsAirTraffic,
        List<AircraftConfig> Aircraft,
        List<string> AirplaneFolders,
        List<string> EncryptedAirplaneFolders,
        bool HasThumbnail,
        List<string>? GsxFiles = null);
}
