namespace AddonManager.Core.Cards;

/// <summary>
/// The subtitles that tell apart cards with the same title on one tab (spec 6.4, decision D7). Two or more cards with equal ICAO codes
/// and the same name (case-insensitive) each show the name of their first Package; every other card gets none.
/// </summary>
public static class CardTitles
{
    /// <summary>One entry per card, in the same order: the first Package's name for a clashing card, else null.</summary>
    public static IReadOnlyList<string?> Subtitles(IReadOnlyList<Card> cards)
    {
        var keys = cards.Select(TitleKey).ToList();
        var counts = keys.GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return [.. cards.Select((card, i) => counts[keys[i]] > 1 && card.Packages.Count > 0 ? card.Packages[0].Name.Value : null)];
    }

    /// <summary>The codes and the name that make two cards clash; the name is compared case-insensitively.</summary>
    private static string TitleKey(Card card)
        => string.Join(' ', card.Codes.Select(c => c.Value)) + "\n" + card.Name.ToUpperInvariant();
}
