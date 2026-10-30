using System.Text.Json.Serialization;
using AddonManager.Core.Content;

namespace AddonManager.Core.Cards;

/// <summary>
/// A stored card (spec 6.5, 11): its place on the wall, the ICAO codes it covers and the user's name and image. The Packages on a
/// card are not listed here; each Package points at its card through <c>PackageState.CardId</c> (rules R1, R2).
/// </summary>
public sealed class CardRecord
{
    /// <summary>From <c>UserState.NextCardId</c>; never reused, so a smaller id is an older card.</summary>
    public required long Id { get; init; }

    public required DateTimeOffset Created { get; init; }

    public Category Section { get; set; }

    public CardTab Tab { get; set; }

    /// <summary>Scenery cards only. Distinct, sorted by <c>Value</c> (ordinal), disjoint from <see cref="RemovedCodes"/>.</summary>
    public List<AirportCode> Codes { get; set; } = [];

    /// <summary>ICAO codes the user removed from the card; they still match Packages (spec 6.5).</summary>
    public List<AirportCode> RemovedCodes { get; set; } = [];

    /// <summary>The user's name; <c>null</c> lets the board compute one (rule R6).</summary>
    public string? Name { get; set; }

    /// <summary>The user's image; <c>null</c> lets the board take the first thumbnail (rule R6).</summary>
    public string? ImagePath { get; set; }

    /// <summary>The ICAO codes a Package is matched against: <see cref="Codes"/> and <see cref="RemovedCodes"/>, sorted.</summary>
    [JsonIgnore]
    public IReadOnlyList<AirportCode> MatchCodes => [.. Codes.Concat(RemovedCodes).Distinct().OrderBy(c => c.Value, StringComparer.Ordinal)];

    /// <summary>Puts <paramref name="code"/> into <see cref="Codes"/> in order and takes it out of <see cref="RemovedCodes"/>.</summary>
    public void AddCode(AirportCode code)
    {
        RemovedCodes.Remove(code);
        if (!Codes.Contains(code))
        {
            Codes.Add(code);
            Codes.Sort(ByValue);
        }
    }

    /// <summary>Takes <paramref name="code"/> out of <see cref="Codes"/> and remembers it in <see cref="RemovedCodes"/>.</summary>
    public void RemoveCode(AirportCode code)
    {
        Codes.Remove(code);
        if (!RemovedCodes.Contains(code))
        {
            RemovedCodes.Add(code);
            RemovedCodes.Sort(ByValue);
        }
    }

    public CardRecord Clone() => new()
    {
        Id = Id,
        Created = Created,
        Section = Section,
        Tab = Tab,
        Codes = [.. Codes],
        RemovedCodes = [.. RemovedCodes],
        Name = Name,
        ImagePath = ImagePath,
    };

    private static int ByValue(AirportCode a, AirportCode b) => string.CompareOrdinal(a.Value, b.Value);
}
