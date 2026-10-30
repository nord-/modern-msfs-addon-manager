namespace AddonManager.Core.Content;

/// <summary>Reads a Package's content. Unreadable content gives an empty result; only cancellation and a Package that can't be accessed throw.</summary>
public interface IPackageContentReader
{
    /// <summary>The change marker, or null when <c>manifest.json</c> is gone. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the Package can't be accessed.</summary>
    ChangeMarker? ReadMarker(string packagePath);

    /// <summary>Whether the Package's <c>layout.json</c> lists a BGL file, or null without a <c>layout.json</c>.</summary>
    bool? HasBgl(string packagePath);

    /// <summary>The Package's content, or null when the folder vanished. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the Package can't be accessed.</summary>
    PackageContent? Read(ScanTarget target, ChangeMarker marker);

    /// <summary>The newest write time of the folder and everything below it; null when the folder is gone. Throws like <see cref="ReadMarker"/>.</summary>
    DateTimeOffset? NewestWrite(string packagePath);
}
