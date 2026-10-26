using System.Text.Json;
using System.Text.Json.Serialization;
using AddonManager.Core.Community;
using AddonManager.Core.Packages;
using AddonManager.Core.Tests.Fakes;

namespace AddonManager.Core.Tests;

/// <summary>Loads a fixture written by <c>tools/Export-Fixture.ps1</c>.</summary>
internal static class FixtureLoader
{
    // A constant, never the real environment variable, so tests behave the same in CI.
    private const string UserProfile = @"C:\Users\user";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static (IReadOnlyList<CommunityEntryInfo> Community, InMemoryFolderTree Tree, IReadOnlyList<PackageCopy> Packages) Load(string name)
    {
        var file = JsonSerializer.Deserialize<FixtureFile>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json")), Options)!;

        var community = file.Community
            .Select(e => new CommunityEntryInfo(e.Name, e.Kind, Expand(e.Target), e.TargetExists, e.HasManifest))
            .ToList();

        var packages = file.Packages
            .Select(p => new PackageCopy(new PackageName(Path.GetFileName(Expand(p.Path)!)), Expand(p.Path)!, new LibraryRoot(Expand(p.Root)!),
                new Manifest(p.Title, p.Creator, p.ContentType, null, [])))
            .ToList();

        // Folders known to hold Packages that weren't exported (outside the Library roots) get a stand-in Package.
        var packagePaths = packages.Select(p => p.Path).ToList();
        var standIns = file.Folders
            .Where(f => f.HasPackageBeneath && !packagePaths.Any(p => PathNormalizer.IsUnder(p, Expand(f.Path)!)))
            .Select(f => Path.Combine(Expand(f.Path)!, "(package)"));
        var empty = file.Folders.Where(f => !f.HasPackageBeneath).Select(f => Expand(f.Path)!);

        return (community, new InMemoryFolderTree(packagePaths.Concat(standIns), empty), packages);
    }

    private static string? Expand(string? path) => path?.Replace("%USERPROFILE%", UserProfile, StringComparison.OrdinalIgnoreCase);

    private sealed record FixtureFile(List<EntryDto> Community, List<PackageDto> Packages, List<FolderDto> Folders);

    private sealed record EntryDto(string Name, EntryKind Kind, string? Target, bool TargetExists, bool HasManifest);

    private sealed record PackageDto(string Path, string Root, string? Title, string? Creator, string? ContentType);

    private sealed record FolderDto(string Path, bool HasPackageBeneath);
}
