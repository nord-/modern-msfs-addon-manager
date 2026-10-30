using System.Text.Json;
using System.Text.Json.Serialization;
using AddonManager.Core.Community;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests;

/// <summary>Loads a fixture written by <c>tools/Export-Fixture.ps1</c>; its <c>folders</c> member is ignored.</summary>
internal static class FixtureLoader
{
    // A constant, never the real environment variable, so tests behave the same in CI.
    private const string UserProfile = @"C:\Users\user";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static (IReadOnlyList<CommunityEntryInfo> Community, IReadOnlyList<PackageCopy> Packages) Load(string name)
    {
        var file = JsonSerializer.Deserialize<FixtureFile>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json")), Options)!;

        var community = file.Community
            .Select(e => new CommunityEntryInfo(e.Name, e.Kind, Expand(e.Target), e.TargetExists, e.HasManifest))
            .ToList();

        var packages = file.Packages
            .Select(p => new PackageCopy(new PackageName(Path.GetFileName(Expand(p.Path)!)), Expand(p.Path)!, new LibraryRoot(Expand(p.Root)!, LibraryRoot.AllSections),
                new Manifest(p.Title, p.Creator, p.ContentType, null, [])))
            .ToList();

        return (community, packages);
    }

    private static string? Expand(string? path) => path?.Replace("%USERPROFILE%", UserProfile, StringComparison.OrdinalIgnoreCase);

    private sealed record FixtureFile(List<EntryDto> Community, List<PackageDto> Packages);

    private sealed record EntryDto(string Name, EntryKind Kind, string? Target, bool TargetExists, bool HasManifest);

    private sealed record PackageDto(string Path, string Root, string? Title, string? Creator, string? ContentType);

}
