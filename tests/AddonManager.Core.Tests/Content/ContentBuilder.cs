using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.Content;

/// <summary>Builds <see cref="PackageContent"/> instances for tests.</summary>
public static class ContentBuilder
{
    public static AirportCode Code(string text)
    {
        Assert.True(AirportCode.TryParse(text, out var code));
        return code;
    }

    /// <summary>A full airport record; the position is set when both <paramref name="lat"/> and <paramref name="lon"/> are given.</summary>
    public static AirportRecord Rec(string code, string? name = null, double? lat = null, double? lon = null)
        => new(Code(code), name, lat is { } la && lon is { } lo ? new GeoPoint(la, lo) : null);

    /// <summary>A Scenery Package. <paramref name="records"/> are bare codes; <paramref name="fullRecords"/> are used instead when given.</summary>
    public static PackageContent Scenery(
        string name,
        string[]? records = null,
        string? title = null,
        bool hasBgl = true,
        PackageSource source = PackageSource.Community,
        string? creator = null,
        string[]? dependencies = null,
        AirportRecord[]? fullRecords = null,
        bool isAirTraffic = false)
        => Build(
            name,
            "SCENERY",
            title,
            creator,
            dependencies,
            source,
            hasBgl,
            isAirTraffic,
            fullRecords ?? (records ?? []).Select(c => Rec(c)).ToArray());

    public static PackageContent Build(
        string name,
        string? contentType,
        string? title = null,
        string? creator = null,
        string[]? dependencies = null,
        PackageSource source = PackageSource.Community,
        bool hasBgl = false,
        bool isAirTraffic = false,
        AirportRecord[]? airports = null)
        => new(
            new PackageName(name),
            @"C:\Sim\" + name,
            source,
            new ChangeMarker(0, 0, null, null),
            new Manifest(title, creator, contentType, null, dependencies ?? []),
            hasBgl,
            [],
            0,
            airports ?? [],
            isAirTraffic,
            [],
            [],
            [],
            null);

    /// <summary>An aircraft Package. Encrypted folders are also listed among the airplane folders.</summary>
    public static PackageContent Aircraft(
        string name,
        (string Container, string? BaseContainer)[]? cfgs = null,
        string[]? airplaneFolders = null,
        string[]? encryptedFolders = null,
        PackageSource source = PackageSource.Library,
        string[]? dependencies = null,
        bool isAirTraffic = false,
        string contentType = "AIRCRAFT")
        => Build(name, contentType, dependencies: dependencies, source: source, isAirTraffic: isAirTraffic) with
        {
            AircraftConfigs = (cfgs ?? []).Select(c => new AircraftConfig(c.Container, c.BaseContainer)).ToArray(),
            AirplaneFolders = [.. airplaneFolders ?? [], .. encryptedFolders ?? []],
            EncryptedAirplaneFolders = encryptedFolders ?? [],
        };
}
