using System.Text.Json;
using AddonManager.Core.Content;

namespace AddonManager.Infrastructure.Tests;

public class JsonConvertersTests
{
    [Fact]
    public void Airport_code_round_trips_as_a_dictionary_key()
    {
        AirportCode.TryParse("EHAM", out var code);
        var json = JsonSerializer.Serialize(new Dictionary<AirportCode, int> { [code] = 7 }, ContentJson.Options);

        var back = JsonSerializer.Deserialize<Dictionary<AirportCode, int>>(json, ContentJson.Options)!;

        Assert.Equal(7, back[code]);
    }
}
