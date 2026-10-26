namespace AddonManager.Core.Packages;

/// <summary>The fields of a Package's <c>manifest.json</c> the app uses.</summary>
public sealed record Manifest(string? Title, string? Creator, string? ContentType, string? PackageVersion, IReadOnlyList<string> Dependencies)
{
    public static Manifest Empty { get; } = new(null, null, null, null, []);
}
