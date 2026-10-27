using AddonManager.Core.Content;

namespace AddonManager.Core.Tests.Content;

public class AirportCodeTests
{
    [Theory]
    [InlineData("eham", "EHAM")]
    [InlineData(" U60 ", "U60")]
    [InlineData("YALR1", "YALR1")]
    [InlineData("3u2", "3U2")]
    public void Parses_valid_codes_upper_case(string text, string expected)
    {
        Assert.True(AirportCode.TryParse(text, out var code));
        Assert.Equal(expected, code.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("ABCDEF")]
    [InlineData("EH-M")]
    [InlineData("ÅÄÖX")]
    public void Rejects_invalid_codes(string? text) => Assert.False(AirportCode.TryParse(text, out _));

    [Fact]
    public void Equality_ignores_case()
    {
        AirportCode.TryParse("eham", out var a);
        AirportCode.TryParse("EHAM", out var b);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Default_code_is_safe_to_compare_and_hash()
    {
        var code = default(AirportCode);
        Assert.Equal(code, default);
        Assert.Equal(code.GetHashCode(), default(AirportCode).GetHashCode());
        Assert.Equal("", code.ToString());
        AirportCode.TryParse("EHAM", out var eham);
        Assert.True(code != eham);
    }
}
