using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Cards;

/// <summary>
/// The derived card wall (spec 6.4, 10). <c>Cards</c> are every stored card with a Package that can be seen, plus a provisional card
/// for each Package that is on no card yet.
/// </summary>
public sealed record Board(IReadOnlyList<Card> Cards, IReadOnlyList<NotShownPackage> NotShown, Banner Banner)
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>File names in the GSX folder that are not Packages and are not shown on a card, sorted (spec 8: "GSX files not recognised").</summary>
    public IReadOnlyList<string> GsxNotRecognised { get; init; } = [];

    /// <summary>The cards of a section and tab, in display order.</summary>
    public IEnumerable<Card> On(Category section, CardTab tab) => Cards.Where(c => c.Section == section && c.Tab == tab);

    /// <summary>The card with a stored id.</summary>
    public Card? Find(long id) => Cards.FirstOrDefault(c => c.Id == id);

    /// <summary>The card that holds a Package, provisional or stored.</summary>
    public Card? CardOf(PackageKey package) => Cards.FirstOrDefault(c => c.Packages.Any(p => p.Kind == package.Kind && p.Name == package.Name));

    /// <summary>Cards whose ICAO codes or whose Packages' ICAO codes list <paramref name="code"/>, by name, then id.</summary>
    public IReadOnlyList<Card> CardsAt(AirportCode code)
        => [.. Cards
            .Where(c => c.Codes.Contains(code) || c.Packages.Any(p => p.Codes.Contains(code)))
            .OrderBy(c => c.Name, Names)
            .ThenBy(c => c.Id ?? long.MaxValue)
            .ThenBy(c => c.ProvisionalPackage?.Name.Value ?? "", Names)];

    /// <summary>The card a map marker opens (spec 10): the oldest card whose only ICAO code is <paramref name="code"/>, else the first card at it by name.</summary>
    public Card? MarkerCard(AirportCode code)
        => Cards.Where(c => c.Id is not null && c.Codes.Count == 1 && c.Codes[0] == code).MinBy(c => c.Id)
           ?? CardsAt(code).FirstOrDefault();
}

/// <summary>One card: a stored card, or a provisional card for a Package that is on no card yet (<c>Id</c> null, <c>ProvisionalPackage</c> set).</summary>
public sealed record Card(
    long? Id,
    PackageKey? ProvisionalPackage,
    Category Section,
    CardTab Tab,
    string Name,
    string? ImagePath,
    IReadOnlyList<AirportCode> Codes,
    IReadOnlyList<AirportCode> RemovedCodes,
    string? AirportName,
    IReadOnlyList<CardPackage> Packages,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> TypeDesignators,
    CardState State,
    bool Pending,
    IReadOnlyList<BuiltInPart> BuiltIns,
    IReadOnlyList<CardWarning> Warnings)
{
    /// <summary>More than one ICAO code: the card shows as "(several)".</summary>
    public bool Several => Codes.Count > 1;

    /// <summary>The GSX status of the card's first ICAO code (spec 8); <c>None</c> for a card without ICAO codes. The wall shows it on Airports only.</summary>
    public GsxMark Gsx { get; init; }

    /// <summary>The GSX facts of each ICAO code of the card, in <see cref="Codes"/> order.</summary>
    public IReadOnlyList<GsxAirport> GsxAirports { get; init; } = [];
}

/// <summary>A Package on a card, with what the card list shows about it. <c>Removable</c>: the dialog offers "Remove" (Not found, or held by no registered Library).</summary>
public sealed record CardPackage(
    PackageKind Kind,
    PackageName Name,
    string DisplayName,
    string Path,
    string? Version,
    LibraryRoot? Library,
    PackageSource Source,
    Presence Presence,
    LockReason Lock,
    ClassifiedEntry? Holder,
    AircraftRole Role,
    IReadOnlyList<string> Copies,
    IReadOnlyList<AirportCode> Codes,
    IReadOnlyList<string> Tags,
    bool On,
    bool CanSwitch,
    string? DisabledReason,
    IReadOnlyList<AirportCode> ReplacesBuiltIn,
    bool Removable)
{
    /// <summary>A <c>.off</c> GSX profile: shown as disabled, not counted (spec 8).</summary>
    public bool Disabled { get; init; }
}

/// <summary>A Package on no card that is not shown: no Library registered for its section holds it (spec 6.1). <c>CanShowIn</c> is true once its section is fixed.</summary>
public sealed record NotShownPackage(PackageName Name, string DisplayName, LibraryRoot? Library, Category Section, bool CanShowIn);
