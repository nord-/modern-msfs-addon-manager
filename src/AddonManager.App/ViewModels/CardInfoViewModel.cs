using AddonManager.App.Session;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;

namespace AddonManager.App.ViewModels;

/// <summary>A built-in part of a card: its display name and <c>"{name} (built-in)"</c>.</summary>
public sealed record BuiltInRow(string DisplayName, string Package);

/// <summary>Another card the dialog can switch to.</summary>
public sealed record OtherCard(string Name, CardRef Ref);

/// <summary>One Airport of a card (spec 5.2): its ICAO code and name, whether a Package replaces the built-in Airport, and the other cards there.</summary>
public sealed record AirportRow(string Code, string? Name, bool ReplacesBuiltIn, IReadOnlyList<OtherCard> OtherCards)
{
    /// <summary>"EHAM Amsterdam Schiphol", or the code alone when no name is known.</summary>
    public string Title => Name is null ? Code : $"{Code} {Name}";
}

/// <summary>The info part of the card dialog (spec 5): built-in parts, Airports and tags. Pure; rebuilt on every update.</summary>
public sealed record CardInfoViewModel(IReadOnlyList<BuiltInRow> BuiltIns, IReadOnlyList<AirportRow> Airports, string? Tags)
{
    public static CardInfoViewModel Build(Card card, Board board, IReadOnlyDictionary<AirportCode, Airport> airports)
    {
        var self = CardRef.Of(card);
        var codes = card.Codes
            .Concat(card.Packages.SelectMany(p => p.Codes).Where(c => !card.Codes.Contains(c)).Distinct().OrderBy(c => c.Value, StringComparer.Ordinal))
            .ToList();

        var rows = codes.Select(code => new AirportRow(
            code.Value,
            NameOf(code, card, airports),
            card.Packages.Any(p => p.ReplacesBuiltIn.Contains(code)),
            [.. board.CardsAt(code).Where(c => CardRef.Of(c) != self).Select(c => new OtherCard(c.Name, CardRef.Of(c)))]));

        return new CardInfoViewModel(
            [.. card.BuiltIns.Select(b => new BuiltInRow(b.DisplayName, $"{b.Name.Value} (built-in)"))],
            [.. rows],
            card.Tags.Count == 0 ? null : string.Join(", ", card.Tags));
    }

    /// <summary>The analysis' name, else the first name remembered for a Package of the card (so a Not found Package still names its Airport).</summary>
    private static string? NameOf(AirportCode code, Card card, IReadOnlyDictionary<AirportCode, Airport> airports)
        => airports.TryGetValue(code, out var airport) && !string.IsNullOrWhiteSpace(airport.Name)
            ? airport.Name
            : card.Packages.SelectMany(p => p.Airports).FirstOrDefault(r => r.Code == code && !string.IsNullOrWhiteSpace(r.Name))?.Name;
}
