namespace AddonManager.Sim2020.Tests.Bgl;

public class AirportCodeCodecTests
{
    [Theory]
    [InlineData("EHAM")]
    [InlineData("YALR1")]
    [InlineData("U60")]
    [InlineData("3U2")]
    [InlineData("OG20")]
    public void Codec_round_trips(string code)
        => Assert.Equal(code, AirportCodeCodec.Decode(AirportCodeCodec.Encode(code)));
}
