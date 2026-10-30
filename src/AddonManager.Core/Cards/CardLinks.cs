using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Cards;

/// <summary>
/// Derives the manual Airport links and removals from the cards (spec 6.5 rule 3 and 11); nothing is stored per Package.
/// Pure: reads <see cref="UserState"/> and changes nothing.
/// </summary>
public static class CardLinks
{
    /// <summary>
    /// Every folder Package on a Scenery card is linked manually to the card's ICAO codes and unlinked from its removed ICAO codes.
    /// Aircraft and Other cards give no overrides, and neither does a card with no ICAO code.
    /// </summary>
    public static AirportLinkOverrides Overrides(UserState state)
    {
        var scenery = state.Cards.Where(c => c.Section == Category.Scenery).ToDictionary(c => c.Id);
        var manual = new Dictionary<PackageName, IReadOnlySet<AirportCode>>();
        var removed = new Dictionary<PackageName, IReadOnlySet<AirportCode>>();
        foreach (var (name, package) in state.Packages)
        {
            if (package.CardId is not { } id || !scenery.TryGetValue(id, out var card))
            {
                continue;
            }

            if (card.Codes.Count > 0)
            {
                manual[name] = card.Codes.ToHashSet();
            }

            if (card.RemovedCodes.Count > 0)
            {
                removed[name] = card.RemovedCodes.ToHashSet();
            }
        }

        return new AirportLinkOverrides(manual, removed);
    }

    /// <summary>
    /// The memo Airport records of the Packages on a card that have no content by that name, so a card keeps its Airports while its
    /// Package is Not found or the scan cache is gone (spec 11). Sorted by ICAO code, then Package name.
    /// </summary>
    public static IReadOnlyList<AirportRecord> Remembered(UserState state, IReadOnlyList<PackageContent> contents)
    {
        var present = contents.Select(c => c.Name).ToHashSet();
        return [.. state.Packages
            .Where(p => p.Value.Memo is not null && state.CardOf(PackageKey.Folder(p.Key)) is not null && !present.Contains(p.Key))
            .OrderBy(p => p.Key.Value, StringComparer.OrdinalIgnoreCase)
            .SelectMany(p => p.Value.Memo!.Airports ?? [])
            .OrderBy(r => r.Code.Value, StringComparer.Ordinal)];
    }
}
