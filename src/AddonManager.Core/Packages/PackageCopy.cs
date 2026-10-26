namespace AddonManager.Core.Packages;

/// <summary>One folder of a Package inside a Library. A Duplicate copy has several.</summary>
public sealed record PackageCopy(PackageName Name, string Path, LibraryRoot Library, Manifest Manifest)
{
    /// <summary>The manifest title, or the folder name when the title is empty.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Manifest.Title) ? Name.Value : Manifest.Title;
}
