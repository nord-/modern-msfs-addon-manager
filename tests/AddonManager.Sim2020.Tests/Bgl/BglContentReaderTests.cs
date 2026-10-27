using System.Text;
using AddonManager.Core.Content;
using AddonManager.Sim2020.Bgl;

namespace AddonManager.Sim2020.Tests.Bgl;

public class BglContentReaderTests
{
    private static BglScan Read(string shape) => BglContentReader.Read(new MemoryStream(BglBuilder.FromShape(BglBuilder.LoadShape(shape))));

    private static BglScan Read(byte[] bgl) => BglContentReader.Read(new MemoryStream(bgl));

    private static BglScan ReadAirports(params byte[][] records) => Read(new BglBuilder().Section(0x03).Subsection(records).Build());

    /// <summary>An airport whose name child is exactly the given body bytes, so padding and a missing terminator can be controlled.</summary>
    private static byte[] AirportWithNameBody(byte[] nameBody)
    {
        var airport = BglBuilder.Airport("EHAM", null, 52.3086, 4.7639, 0x44);
        byte[] record = [.. airport, .. BglBuilder.Record(0x0019, nameBody)];
        BitConverter.GetBytes((uint)record.Length).CopyTo(record, 2);
        return record;
    }

    [Fact]
    public void Reads_eham_from_its_real_shape()
    {
        var scan = Read("eham-bbd");
        var eham = Assert.Single(scan.Airports, a => a.Code.Value == "EHAM");
        Assert.Equal(52.31, eham.Position!.Value.Latitude, 1);
        Assert.Equal(4.76, eham.Position!.Value.Longitude, 1);
        Assert.Equal("Schiphol", eham.Name);
        Assert.Contains(0x03u, scan.SectionTypes);
    }

    [Fact]
    public void Reads_five_character_codes() => Assert.Contains(Read("yalr1").Airports, a => a.Code.Value == "YALR1");

    [Fact]
    public void Returns_localisation_keys_raw()
    {
        var scan = ReadAirports(BglBuilder.Airport("LOWI", "TT:AIRPORTLR.LOWI.name", 47.26, 11.34, 0x44));
        Assert.Equal("TT:AIRPORTLR.LOWI.name", Assert.Single(scan.Airports).Name);
    }

    [Fact]
    public void A_bgl_without_airport_records_is_readable_and_empty()
    {
        var scan = Read("lfcs-saucats");
        Assert.True(scan.Readable);
        Assert.Empty(scan.Airports);
    }

    [Fact]
    public void Generic_file_yields_its_airport_record()
    {
        // The real generic dump holds one airport record.
        Assert.Equal("UHMI", Assert.Single(Read("generic").Airports).Code.Value);
    }

    [Fact]
    public void A_position_outside_the_globe_is_dropped_but_the_airport_is_kept()
    {
        var airport = BglBuilder.Airport("EHAM", "Schiphol", 52.3086, 4.7639, 0x44);
        BitConverter.GetBytes(uint.MaxValue).CopyTo(airport, 0x0C);

        var record = Assert.Single(ReadAirports(airport).Airports);

        Assert.Equal("EHAM", record.Code.Value);
        Assert.Null(record.Position);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB")]
    public void Invalid_codes_are_ignored(string code)
    {
        var scan = ReadAirports(BglBuilder.Airport(code, "Nowhere", 10, 10, 0x44), BglBuilder.Airport("EHAM", null, 52.3, 4.7, 0x44));
        Assert.True(scan.Readable);
        Assert.Equal("EHAM", Assert.Single(scan.Airports).Code.Value);
    }

    [Fact]
    public void Airport_without_name_child_has_null_name()
    {
        var record = Assert.Single(ReadAirports(BglBuilder.Airport("EHAM", null, 52.3, 4.7, 0x44)).Airports);
        Assert.Null(record.Name);
    }

    [Fact]
    public void Record_whose_size_fits_no_known_layout_keeps_code_and_position_with_a_null_name()
    {
        var record = Assert.Single(ReadAirports(BglBuilder.Airport("EHAM", null, 52.3, 4.7, 0x50)).Airports);
        Assert.Equal("EHAM", record.Code.Value);
        Assert.Equal(52.3, record.Position!.Value.Latitude, 1);
        Assert.Null(record.Name);
    }

    [Fact]
    public void A_padded_name_ends_at_the_first_nul()
    {
        // "Foo", NUL, then bytes that must not be part of the name.
        var record = Assert.Single(ReadAirports(AirportWithNameBody("Foo\0xyz\0"u8.ToArray())).Airports);
        Assert.Equal("Foo", record.Name);
    }

    [Fact]
    public void A_name_that_fills_its_child_has_no_nul_and_is_read_whole()
    {
        var record = Assert.Single(ReadAirports(AirportWithNameBody(Encoding.UTF8.GetBytes("TT:AIRPORTLR.LOWI.name"))).Airports);
        Assert.Equal("TT:AIRPORTLR.LOWI.name", record.Name);
    }

    [Theory]
    [InlineData("wrong-magic")]
    [InlineData("data-past-end")]
    [InlineData("random")]
    public void Broken_files_are_unreadable(string kind) => Assert.False(Read(BglBuilder.Broken(kind)).Readable);

    [Fact]
    public void Bad_record_size_inside_an_airport_subsection_makes_the_file_unreadable()
    {
        var scan = Read(BglBuilder.Broken("bad-record-size"));
        Assert.False(scan.Readable);
        Assert.Empty(scan.Airports);
    }

    [Fact]
    public void Trailing_bytes_after_the_last_record_make_the_file_unreadable_even_when_the_count_matches()
    {
        byte[] data = [.. BglBuilder.Airport("EHAM", "Schiphol", 52.3, 4.7, 0x44), 1, 2, 3];
        var scan = Read(new BglBuilder().Section(0x03).RawSubsection(1, data).Build());
        Assert.False(scan.Readable);
        Assert.Empty(scan.Airports);
    }

    [Fact]
    public void A_record_count_that_differs_from_the_walk_makes_the_file_unreadable()
    {
        // The data walks cleanly (one record to the end) but the subsection claims two.
        var bgl = new BglBuilder().Section(0x03)
            .RawSubsection(2, BglBuilder.Airport("EHAM", "Schiphol", 52.3, 4.7, 0x44)).Build();
        var scan = Read(bgl);
        Assert.False(scan.Readable);
        Assert.Empty(scan.Airports);
    }

    [Fact]
    public void Reads_a_file_from_disk_while_another_handle_has_it_open_for_writing()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Root, "a.bgl");
        File.WriteAllBytes(path, BglBuilder.FromShape(BglBuilder.LoadShape("eham-bbd")));
        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        Assert.Contains(BglContentReader.Read(path).Airports, a => a.Code.Value == "EHAM");
    }
}
