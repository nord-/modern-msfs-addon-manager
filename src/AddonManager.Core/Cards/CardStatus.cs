using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Cards;

/// <summary>The traffic light of a card (spec 6.3). <c>Locked</c>: no Package of the card can be switched (a GSX profile, a Package held by a Community entry, one that is Not found or in no registered Library).</summary>
public enum CardState { On, Off, Mixed, Locked }

public enum WarningKind { DuplicatePackage, DuplicateCopy, LiveryWithoutAircraft, NotFound }

/// <summary>A warning about one Package of a card (spec 6.7).</summary>
public sealed record CardWarning(WarningKind Kind, PackageName Package);

/// <summary>Sim-managed content that belongs to a card but is never a Package of it: an official Airport, or an aircraft base, livery or add-on (spec 6.4, rule A6).</summary>
public sealed record BuiltInPart(PackageName Name, string DisplayName, AirportCode? Code, string? Container, AircraftRole Role);

/// <summary>
/// What the banner above the wall shows (spec 10). <c>Provisional</c> counts every provisional card on the wall;
/// <c>Unreadable</c> names Found Packages that still have no content after a completed scan, and Packages under a folder
/// discovery could not read; <c>LibrariesNotFound</c> lists the registered Libraries that don't answer, by path.
/// </summary>
public sealed record Banner(
    IReadOnlyList<ClassifiedEntry> Broken,
    IReadOnlyList<ClassifiedEntry> Stray,
    int Pending,
    int Provisional,
    IReadOnlyList<PackageName> Unreadable,
    IReadOnlyList<LibraryRoot> LibrariesNotFound)
{
    /// <summary>The GSX folder does not exist, so its profiles are Library not found (spec 8).</summary>
    public bool GsxFolderMissing { get; init; }
}
