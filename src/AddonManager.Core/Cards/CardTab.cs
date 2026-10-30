using AddonManager.Core.Content;

namespace AddonManager.Core.Cards;

public enum CardTab
{
    Airports,
    Landscapes,
    Assets,
    Aircraft,
    Instruments,
    Other,
}

public static class CardTabs
{
    private static readonly CardTab[] Scenery = [CardTab.Airports, CardTab.Landscapes, CardTab.Assets];
    private static readonly CardTab[] Aircraft = [CardTab.Aircraft, CardTab.Instruments, CardTab.Other];
    private static readonly CardTab[] Other = [CardTab.Other];

    /// <summary>The tabs of a section, in display order.</summary>
    public static IReadOnlyList<CardTab> Of(Category section) => section switch
    {
        Category.Scenery => Scenery,
        Category.Aircraft => Aircraft,
        _ => Other,
    };

    public static bool IsValid(Category section, CardTab tab) => Of(section).Contains(tab);

    /// <summary>
    /// The tab a new card starts on (spec 6.5). Scenery: Airports with ICAO codes, else Assets when no Package's BGL files place
    /// anything, else Landscapes. Aircraft: Instruments for an INSTRUMENTS Package, Aircraft when a Package's Container has a
    /// provider or a locked base, else Other.
    /// </summary>
    public static CardTab Initial(Category section, IReadOnlyList<PackageSummary> packages, int codeCount, AircraftIndex aircraft)
    {
        var ordered = packages.OrderBy(p => p.Name.Value, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToList();
        switch (section)
        {
            case Category.Scenery:
                if (codeCount > 0)
                {
                    return CardTab.Airports;
                }

                return ordered.Count > 0 && ordered.All(p => p.Bgl == BglPlacement.PlacesNothing) ? CardTab.Assets : CardTab.Landscapes;
            case Category.Aircraft:
                if (ordered.Count > 0 && CategoryRules.IsInstruments(ordered[0].ContentType))
                {
                    return CardTab.Instruments;
                }

                return ordered.Any(p => p.Containers.Any(c => HasProviderOrBase(aircraft, c))) ? CardTab.Aircraft : CardTab.Other;
            default:
                return CardTab.Other;
        }
    }

    private static bool HasProviderOrBase(AircraftIndex aircraft, string container)
    {
        var group = aircraft.Groups.FirstOrDefault(g => string.Equals(g.Key, container, StringComparison.OrdinalIgnoreCase)).Value;
        return group is not null && (group.Providers.Count > 0 || group.LockedBases.Count > 0);
    }
}
