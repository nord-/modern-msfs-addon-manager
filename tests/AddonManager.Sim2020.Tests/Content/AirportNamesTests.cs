using System.Text;
using AddonManager.Core.Content;
using AddonManager.Sim2020.Content;

namespace AddonManager.Sim2020.Tests.Content;

public sealed class AirportNamesTests : IDisposable
{
    private const string LocPak = """
        {
          "LocalisationPackage": {
            "Language": "en-US",
            "Strings": {
              "AIRPORTLR.LOWI.name": "Innsbruck",
              "AIRPORTLR.LOWI.city": "Innsbruck City",
              "AIRPORTLR.LOWI.state": "Tyrol",
              "AIRPORTEK.LOWI.name": "Other",
              "AIRPORT0D.CYTZ.name": "Toronto\/City Centre",
              "AIRPORT0D.0PR.name": "El Paraíso Airport",
              "COUNTRY.CHINA": "China"
            }
          }
        }
        """;

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static AirportCode Code(string text) => AirportCode.TryParse(text, out var code) ? code : throw new ArgumentException(text);

    private string LocPakPath(string official) => Path.Combine(official, "fs-base-genericairports", "en-US.locPak");

    private string WriteOfficial(string content, string folder = "official")
    {
        var official = Path.Combine(_temp.Root, folder);
        Directory.CreateDirectory(Path.GetDirectoryName(LocPakPath(official))!);
        File.WriteAllText(LocPakPath(official), content, new UTF8Encoding(false));
        return official;
    }

    private AirportNames Names() => new(WriteOfficial(LocPak));

    [Fact]
    public void Plain_names_pass_through_trimmed() => Assert.Equal("Schiphol", Names().Resolve(" Schiphol ", Code("EHAM")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_names_are_null(string? raw) => Assert.Null(Names().Resolve(raw, Code("AYPY")));

    [Fact]
    public void Key_resolves_exactly()
    {
        // The same code under two regions: only an exact lookup tells them apart.
        Assert.Equal("Innsbruck", Names().Resolve("TT:AIRPORTLR.LOWI.name", Code("LOWI")));
        Assert.Equal("Other", Names().Resolve("TT:AIRPORTEK.LOWI.name", Code("LOWI")));
    }

    [Fact]
    public void Exact_key_wins_over_the_code_of_the_airport_record()
    {
        // The record's own code (EHAM) is irrelevant: the key decides.
        Assert.Equal("Other", Names().Resolve("TT:AIRPORTEK.LOWI.name", Code("EHAM")));
    }

    [Fact]
    public void Key_without_region_resolves_by_code() =>
        Assert.Equal("Toronto/City Centre", Names().Resolve("TT:AIRPORT.CYTZ.name", Code("CYTZ")));

    [Fact]
    public void Key_with_unknown_region_resolves_to_the_first_region_in_file_order() =>
        Assert.Equal("Innsbruck", Names().Resolve("TT:AIRPORTSZ.LOWI.name", Code("EHAM")));

    [Fact]
    public void Unknown_key_is_null() => Assert.Null(Names().Resolve("TT:AIRPORTLR.XXXX.name", Code("XXXX")));

    [Fact]
    public void City_and_state_keys_are_not_names() => Assert.Null(Names().Resolve("TT:AIRPORTLR.LOWI.city", Code("LOWI")));

    [Fact]
    public void Non_airport_key_is_null() => Assert.Null(Names().Resolve("TT:COUNTRY.CHINA", Code("CHN")));

    [Fact]
    public void Escapes_and_non_ascii_survive() => Assert.Equal("El Paraíso Airport", Names().Resolve("TT:AIRPORT0D.0PR.name", Code("0PR")));

    [Fact]
    public void Escaped_slash_is_unescaped() => Assert.Equal("Toronto/City Centre", Names().Resolve("TT:AIRPORT0D.CYTZ.name", Code("CYTZ")));

    [Theory]
    [InlineData(null)]
    [InlineData("missing-folder")]
    [InlineData("invalid-json")]
    [InlineData("wrong-shape")]
    public void No_table_resolves_keys_to_null_without_throwing(string? official)
    {
        var path = official switch
        {
            null => null,
            "missing-folder" => Path.Combine(_temp.Root, "nowhere"),
            "invalid-json" => WriteOfficial("{ not json", "bad"),
            _ => WriteOfficial("""{"LocalisationPackage": []}""", "shape"),
        };
        var names = new AirportNames(path);

        Assert.Null(names.Resolve("TT:AIRPORTLR.LOWI.name", Code("LOWI")));
        Assert.Equal("Schiphol", names.Resolve("Schiphol", Code("EHAM")));
    }

    [Theory]
    [InlineData("bad\0path")]
    [InlineData("C:\\a:b")]
    public void A_malformed_path_resolves_keys_to_null_without_throwing(string official) =>
        Assert.Null(new AirportNames(official).Resolve("TT:AIRPORTLR.LOWI.name", Code("LOWI")));

    [Fact]
    public void The_file_is_not_read_before_the_first_key()
    {
        var official = WriteOfficial(LocPak);
        AirportNames names;
        using (new FileStream(LocPakPath(official), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            names = new AirportNames(official);
            Assert.Equal("Schiphol", names.Resolve("Schiphol", Code("EHAM")));
        }

        // Only passes if nothing was read while the file was locked.
        Assert.Equal("Innsbruck", names.Resolve("TT:AIRPORTLR.LOWI.name", Code("LOWI")));
    }

    [Fact]
    public void The_file_is_read_with_shared_access()
    {
        var official = WriteOfficial(LocPak);
        using var writer = new FileStream(LocPakPath(official), FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal("Innsbruck", new AirportNames(official).Resolve("TT:AIRPORTLR.LOWI.name", Code("LOWI")));
    }

    [Fact]
    public void The_file_is_read_once_per_instance()
    {
        var official = WriteOfficial(LocPak);
        var names = new AirportNames(official);
        Assert.Equal("Innsbruck", names.Resolve("TT:AIRPORTLR.LOWI.name", Code("LOWI")));

        File.Delete(LocPakPath(official));

        Assert.Equal("Innsbruck", names.Resolve("TT:AIRPORTLR.LOWI.name", Code("LOWI")));
    }
}
