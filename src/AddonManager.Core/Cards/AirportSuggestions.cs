using AddonManager.Core.Content;

namespace AddonManager.Core.Cards;

/// <summary>Known Airports to offer while the user types an ICAO code (spec 6.5).</summary>
public static class AirportSuggestions
{
    /// <summary>Codes starting with the text first, then names containing it; each group by code. Case-insensitive; empty text gives nothing.</summary>
    public static IReadOnlyList<Airport> Find(IReadOnlyDictionary<AirportCode, Airport> airports, string text, int max = 10)
    {
        var needle = text.Trim();
        if (needle.Length == 0)
        {
            return [];
        }

        var byCode = airports.Values.Where(a => a.Code.Value.StartsWith(needle, StringComparison.OrdinalIgnoreCase)).OrderBy(a => a.Code.Value, StringComparer.Ordinal);
        var byName = airports.Values
            .Where(a => !a.Code.Value.StartsWith(needle, StringComparison.OrdinalIgnoreCase) && a.Name?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(a => a.Code.Value, StringComparer.Ordinal);
        return [.. byCode.Concat(byName).Take(max)];
    }
}

/// <summary>What happened to the ICAO code the user typed into a Scenery card.</summary>
public enum CodeResult { Added, Invalid, Taken }

/// <summary>The result of adding an ICAO code; for <see cref="CodeResult.Taken"/>, the card that has it and the name that card shows.</summary>
public sealed record CodeOutcome(CodeResult Result, long? Holder = null, string? HolderName = null);
