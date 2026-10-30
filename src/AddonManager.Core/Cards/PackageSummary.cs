using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Cards;

/// <summary>
/// <c>Unreadable</c>: discovery could not read the folder the Package lies in, so it is neither gone nor seen.
/// </summary>
public enum Presence { Found, LibraryNotFound, NotFound, Unreadable }

/// <summary>
/// <c>NotInRegisteredLibrary</c>: no registered Library holds the Package any more; it is off and locked until a Library holds it again (spec 6.1).
/// <c>GsxProfile</c>: switching profiles is Later, so a profile is locked and never counted (spec 6.3, 8).
/// </summary>
public enum LockReason { None, HeldByEntry, NameTaken, NotInRegisteredLibrary, GsxProfile }

/// <summary>
/// Whether a Package's BGL files place objects. <c>None</c>: no readable BGL section, or any unreadable BGL (encrypted scenery
/// may place objects, so it starts in Landscapes). <c>Places</c>: has section 0x25 or 0x65. <c>PlacesNothing</c>: readable
/// sections only, none of them placing.
/// </summary>
public enum BglPlacement { None, PlacesNothing, Places }

/// <summary>Everything the board, placement and editor need to know about one Package (spec 6.1 to 6.4).</summary>
/// <param name="Path">The active copy, the Community folder or link target, or the last known path.</param>
/// <param name="Codes">The Package's real Airport links as the card shows them (manual links added, removed ICAO codes gone), sorted.</param>
/// <param name="OwnCodes">The ICAO codes the Package's own content gives it (BGL records and name or title tokens), whatever its card says; what the memo stores.</param>
/// <param name="Copies">Every Library copy path, for the Duplicate copy choice.</param>
/// <param name="Airports">The records (name and position) of the Airports among <paramref name="Codes"/>, sorted by ICAO code; an ICAO code with neither is absent.</param>
/// <param name="ManifestSection">The section the manifest's <c>content_type</c> gives, before any content is read.</param>
/// <param name="FixedSection">The section the first settled read fixed (rule R4); null until then.</param>
/// <param name="Section">The fixed section, else the category of the content read, else the manifest section.</param>
/// <param name="TypeDesignators">The distinct ICAO type designators of the Package's <c>aircraft.cfg</c> files, sorted; the memo's for a Package whose content isn't read.</param>
/// <param name="LibrarySections">The sections of every Library holding a copy; empty for Community Packages.</param>
public sealed record PackageSummary(
    PackageName Name,
    string DisplayName,
    string Path,
    PackageSource Source,
    Presence Presence,
    LockReason Lock,
    ClassifiedEntry? Holder,
    bool HasContent,
    Category ManifestSection,
    Category? FixedSection,
    Category Section,
    string? ContentType,
    string? Version,
    string? ThumbnailPath,
    BglPlacement Bgl,
    IReadOnlyList<AirportCode> Codes,
    IReadOnlyList<AirportCode> OwnCodes,
    IReadOnlyList<string> Containers,
    AircraftRole Role,
    LibraryRoot? Library,
    IReadOnlyList<Category> LibrarySections,
    IReadOnlyList<string> Copies,
    IReadOnlyList<AirportRecord> Airports,
    IReadOnlyList<string> TypeDesignators)
{
    /// <summary>What the Package is; a GSX profile comes from <see cref="ProfileCatalog"/>.</summary>
    public PackageKind Kind { get; init; } = PackageKind.Folder;

    /// <summary>A <c>.off</c> GSX profile.</summary>
    public bool Disabled { get; init; }

    /// <summary>The <c>.off</c> file next to an enabled GSX profile's <c>.ini</c>, when there is one.</summary>
    public string? OffVariantPath { get; init; }

    /// <summary>The Package's identity across kinds.</summary>
    public PackageKey Key => new(Kind, Name);

    /// <summary>A Community Package or a GSX profile, or one with a copy in a Library registered for <paramref name="section"/> (spec 6.1).</summary>
    public bool IsEligible(Category section) => Source is PackageSource.Community or PackageSource.GsxFolder || LibrarySections.Contains(section);

    /// <summary>True when no Library registered for its section holds the Package, so it gets no card (spec 6.4).</summary>
    public bool IsNotShown => !IsEligible(FixedSection ?? ManifestSection);
}

/// <summary>The facts last seen for a Package, kept so a Not found or Library-not-found Package still shows them (spec 11).</summary>
public sealed record PackageMemo(
    string Path,
    PackageSource Source,
    string DisplayName,
    string? ContentType,
    string? Version,
    string? ThumbnailPath,
    BglPlacement Bgl,
    List<AirportCode> Codes,
    List<string> Containers,
    AircraftRole Role,
    List<AirportRecord> Airports)
{
    /// <summary>The tags of the Package's folders in its Library, so a Not found Package keeps them.</summary>
    public List<string> Tags { get; init; } = [];

    /// <summary>The ICAO type designators from its <c>aircraft.cfg</c> files, sorted.</summary>
    public List<string> TypeDesignators { get; init; } = [];

    /// <summary>Field-by-field equality; record equality would compare the lists by reference.</summary>
    public bool SameAs(PackageMemo? other)
        => other is not null
           && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase)
           && Source == other.Source
           && DisplayName == other.DisplayName
           && ContentType == other.ContentType
           && Version == other.Version
           && ThumbnailPath == other.ThumbnailPath
           && Bgl == other.Bgl
           && Codes.SequenceEqual(other.Codes)
           && Containers.SequenceEqual(other.Containers)
           && Role == other.Role
           && Airports.SequenceEqual(other.Airports)
           && Tags.SequenceEqual(other.Tags)
           && TypeDesignators.SequenceEqual(other.TypeDesignators);
}
