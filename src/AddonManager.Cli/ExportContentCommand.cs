using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AddonManager.Core.Content;

namespace AddonManager.Cli;

/// <summary>Runs a scan and writes the content fixture. Writes only the scan cache and the explicit --out file.</summary>
internal static class ExportContentCommand
{
    public const string Usage = "Usage: AddonManager.Cli export-content --out <file>";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\r\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is not ["--out", var outFile])
        {
            error.WriteLine(Usage);
            return 2;
        }

        var run = ScanCommand.Execute(full: false, output, out var exitCode);
        if (run is null)
            return exitCode;

        var fixture = Build(run.Outcome.Content.Contents, run.Outcome.Analysis, DateTime.Today, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        ScanCommand.WriteText(outFile, JsonSerializer.Serialize(fixture, JsonOptions));
        output.WriteLine($"Wrote {fixture.Packages.Count} Packages to {outFile}");
        return 0;
    }

    internal static Fixture Build(IReadOnlyList<PackageContent> contents, ContentAnalysis analysis, DateTime exported, string userProfile)
    {
        var excluded = analysis.Excluded.Select(c => c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linkedCodes = analysis.AirportLinks.Select(l => l.Code).ToHashSet();

        var packages = contents
            .OrderBy(c => c.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .Select(c =>
            {
                var isExcluded = excluded.Contains(c.Path);
                // An excluded Package keeps full records only for codes some Package links to; the rest become a plain code list.
                var full = c.Airports.Where(a => !isExcluded || linkedCodes.Contains(a.Code)).ToList();
                var codes = isExcluded
                    ? c.Airports.Where(a => !linkedCodes.Contains(a.Code)).Select(a => a.Code.Value).ToList()
                    : [];
                return new FixturePackage(
                    c.Name.Value,
                    ReplaceProfile(c.Path, userProfile),
                    c.Source.ToString(),
                    c.Manifest.Title,
                    c.Manifest.Creator,
                    c.Manifest.ContentType,
                    c.Manifest.PackageVersion,
                    c.Manifest.Dependencies,
                    c.HasBgl,
                    c.BglSectionTypes,
                    c.UnreadableBglCount,
                    isExcluded && full.Count == 0 ? null : full.Select(a => new FixtureAirport(a.Code.Value, a.Name, a.Position?.Latitude, a.Position?.Longitude)).ToList(),
                    codes.Count > 0 ? codes : null,
                    c.IsAirTraffic,
                    c.AircraftConfigs,
                    c.AirplaneFolders,
                    c.EncryptedAirplaneFolders,
                    c.ThumbnailPath is not null);
            })
            .ToList();

        return new Fixture(exported.ToString("yyyy-MM-dd"), packages);
    }

    internal static string ReplaceProfile(string path, string userProfile)
        => userProfile.Length > 0 && path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase)
            ? "%USERPROFILE%" + path[userProfile.Length..]
            : path;

    internal sealed record Fixture(string Exported, IReadOnlyList<FixturePackage> Packages);

    internal sealed record FixtureAirport(string Code, string? Name, double? Lat, double? Lon);

    internal sealed record FixturePackage(
        string Name,
        string Path,
        string Source,
        string? Title,
        string? Creator,
        string? ContentType,
        string? PackageVersion,
        IReadOnlyList<string> Dependencies,
        bool HasBgl,
        IReadOnlyList<uint> BglSectionTypes,
        int UnreadableBgl,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FixtureAirport>? Airports,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Codes,
        bool IsAirTraffic,
        IReadOnlyList<AircraftConfig> Aircraft,
        IReadOnlyList<string> AirplaneFolders,
        IReadOnlyList<string> EncryptedAirplaneFolders,
        bool HasThumbnail);
}
