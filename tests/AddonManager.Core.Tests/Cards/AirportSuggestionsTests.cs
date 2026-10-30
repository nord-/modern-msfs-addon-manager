using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

public sealed class AirportSuggestionsTests
{
    private static Dictionary<AirportCode, Airport> Airports() => new[]
    {
        new Airport(Code("ESSA"), "Stockholm Arlanda", null, true, false),
        new Airport(Code("EHAM"), "Amsterdam Schiphol", null, true, false),
        new Airport(Code("EHAA"), "Arlanda lookalike", null, true, false),
        new Airport(Code("LFPG"), "Paris Charles de Gaulle", null, true, false),
        new Airport(Code("ARL"), null, null, true, false),
    }.ToDictionary(a => a.Code);

    [Fact]
    public void Suggestions_put_ICAO_code_prefixes_before_name_matches()
    {
        var found = AirportSuggestions.Find(Airports(), "ar");

        Assert.Equal([Code("ARL"), Code("EHAA"), Code("ESSA"), Code("LFPG")], found.Select(a => a.Code));
    }

    [Fact]
    public void Empty_text_gives_no_suggestions_and_the_list_is_capped()
    {
        Assert.Empty(AirportSuggestions.Find(Airports(), ""));
        Assert.Empty(AirportSuggestions.Find(Airports(), "   "));
        Assert.Single(AirportSuggestions.Find(Airports(), "ar", max: 1));
    }
}
