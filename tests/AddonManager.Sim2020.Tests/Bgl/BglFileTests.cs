namespace AddonManager.Sim2020.Tests.Bgl;

public class BglFileTests
{
    [Fact]
    public void Reads_sections_and_subsections_with_both_entry_sizes()
    {
        var bgl = new BglBuilder()
            .Section(0x03).Subsection(BglBuilder.Record(0x0056, new byte[60]))
            .Section(0x65, entrySize: 20).Subsection(BglBuilder.Record(0x0001, new byte[4]), BglBuilder.Record(0x0001, new byte[4]))
            .Build();
        var sections = BglFile.ReadSections(new MemoryStream(bgl))!;
        Assert.Equal([0x03u, 0x65u], sections.Select(s => s.Type));
        Assert.Equal(16, sections[0].SubsectionEntrySize);
        Assert.Equal(20, sections[1].SubsectionEntrySize);
        Assert.Equal(2, sections[1].Subsections.Single().RecordCount);
    }

    [Theory]
    [InlineData("wrong-magic")]
    [InlineData("truncated-header")]
    [InlineData("table-past-end")]
    [InlineData("data-past-end")]
    [InlineData("random")]
    public void Unreadable_files_return_null_and_never_throw(string kind)
        => Assert.Null(BglFile.ReadSections(new MemoryStream(BglBuilder.Broken(kind))));

    [Fact]
    public void Record_walk_stops_at_an_impossible_size()
    {
        var data = BglBuilder.Record(0x0056, new byte[10]).Concat(new byte[] { 0x19, 0x00, 0x02, 0, 0, 0 }).ToArray();
        Assert.Single(BglFile.Records(data));
        Assert.False(BglFile.IsConsistent(data));
    }

    [Fact]
    public void Shape_finds_the_fixed_size_and_the_name_child()
    {
        var bgl = new BglBuilder().Section(0x03)
            .Subsection(BglBuilder.Airport("EHAM", "Amsterdam Schiphol", 52.3086, 4.7639, fixedSize: 0x44)).Build();
        var record = BglShapes.Describe(new MemoryStream(bgl))!.Sections[0].Subsections[0].Records!.Single();
        Assert.Equal(0x44, record.FixedSize);
        Assert.Equal("EHAM", record.Code);
        Assert.Equal("Amsterdam Schiphol", record.Name);
        Assert.Equal(52.3086, record.Latitude!.Value, 3);
        Assert.Equal(4.7639, record.Longitude!.Value, 3);
    }

    [Fact]
    public void An_airport_record_with_another_fixed_size_has_no_fixed_size_and_no_name()
    {
        var record = BglBuilder.Airport("EHAM", "Amsterdam Schiphol", 52.3086, 4.7639, fixedSize: 0x38);

        Assert.Null(BglFile.FindAirportFixedSize(record));
        Assert.Null(AirportRecordReader.ReadName(record));
    }

    [Fact]
    public void Shape_of_an_unreadable_file_is_null()
        => Assert.Null(BglShapes.Describe(new MemoryStream(BglBuilder.Broken("random"))));

    [Fact]
    public void Serialised_shape_keeps_non_ascii_names_literal_and_round_trips()
    {
        var bgl = new BglBuilder().Section(0x03)
            .Subsection(BglBuilder.Airport("LSZH", "Zürich", 47.4647, 8.5492, fixedSize: 0x44)).Build();
        var shape = BglShapes.Describe(new MemoryStream(bgl))!;
        var json = System.Text.Json.JsonSerializer.Serialize(shape, BglShapes.JsonOptions);
        Assert.Contains("Zürich", json);
        var back = System.Text.Json.JsonSerializer.Deserialize<BglShape>(json, BglShapes.JsonOptions)!;
        Assert.Equal("Zürich", back.Sections[0].Subsections[0].Records!.Single().Name);
    }
}
