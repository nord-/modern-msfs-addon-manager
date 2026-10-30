using AddonManager.Sim2020.Content;

namespace AddonManager.Sim2020.Tests.Content;

public sealed class AircraftCfgReaderTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string name, string text)
    {
        var path = Path.Combine(_temp.Root, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Theory]
    [InlineData("[VARIATION]\nbase_container = \"..\\Aerosoft_CRJ_900\"", "Aerosoft_CRJ_900", false)]
    [InlineData("[GENERAL]\nisAirTraffic=1 ; AI", null, true)]
    [InlineData("[GENERAL]\nIsAirTraffic = 0", null, false)]
    [InlineData("; base_container = \"..\\X\"", null, false)]
    [InlineData("// base_container = \"..\\X\"\n[VARIATION]\nBASE_CONTAINER=../x/ // c", "x", false)]
    public void Reads_cfg_facts(string text, string? baseContainer, bool airTraffic)
    {
        var facts = AircraftCfgReader.Read(Write("aircraft.cfg", text))!;
        Assert.Equal(baseContainer, facts.BaseContainer);
        Assert.Equal(airTraffic, facts.IsAirTraffic);
    }

    [Fact]
    public void Reads_the_type_designator_from_the_general_section()
        => Assert.Equal("C152", AircraftCfgReader.Read(Write("aircraft.cfg", "[GENERAL]\nicao_type_designator = \"C152\"\n"))!.TypeDesignator);

    [Fact]
    public void Ignores_a_type_designator_outside_the_general_section()
        => Assert.Null(AircraftCfgReader.Read(Write("aircraft.cfg", "[FLTSIM.0]\nicao_type_designator = A20N\n"))!.TypeDesignator);

    [Fact]
    public void An_empty_type_designator_reads_as_none()
        => Assert.Null(AircraftCfgReader.Read(Write("aircraft.cfg", "[general]\nicao_type_designator = \"\"\n"))!.TypeDesignator);

    [Fact]
    public void Unreadable_cfg_is_null() => Assert.Null(AircraftCfgReader.Read(Path.Combine(_temp.Root, "missing.cfg")));

    [Theory]
    [InlineData(@"..\Aerosoft_CRJ_900", "Aerosoft_CRJ_900")]
    [InlineData("../x/", "x")]
    [InlineData("plain", "plain")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Last_segment(string? input, string? expected) => Assert.Equal(expected, AircraftCfgReader.LastSegment(input));
}
