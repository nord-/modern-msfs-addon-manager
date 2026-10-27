using System.Buffers.Binary;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AddonManager.Sim2020.Bgl;

public sealed record BglShape(IReadOnlyList<BglSectionShape> Sections)
{
    public static string Hex(uint value) => $"0x{value:X}";
}

public sealed record BglSectionShape(string Type, int SubsectionEntrySize, IReadOnlyList<BglSubsectionShape> Subsections);

public sealed record BglSubsectionShape(int RecordCount, int DataSize, IReadOnlyList<BglRecordShape>? Records);

public sealed record BglRecordShape(
    string Id,
    int Size,
    int? FixedSize,
    string? Code,
    string? Name,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<BglChildShape> Children);

public sealed record BglChildShape(string Id, int Size);

/// <summary>Describes the structure of a BGL file as a serialisable shape, for <c>bgl-dump</c> and the structure fixtures.</summary>
public static class BglShapes
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const int CodeOffset = 0x28;

    /// <summary>The shape of the file, or null when it is unreadable.</summary>
    public static BglShape? Describe(Stream bgl)
    {
        var sections = BglFile.ReadSections(bgl);
        if (sections is null) return null;

        try
        {
            return new BglShape([.. sections.Select(s => DescribeSection(bgl, s))]);
        }
        catch (Exception e) when (e is IOException or NotSupportedException or ObjectDisposedException)
        {
            return null;
        }
    }

    private static BglSectionShape DescribeSection(Stream bgl, BglSection section)
    {
        var isAirportSection = section.Type == BglFile.AirportSectionType;
        var subsections = section.Subsections.Select(sub =>
        {
            IReadOnlyList<BglRecordShape>? records = null;
            if (isAirportSection)
            {
                var data = new byte[sub.DataSize];
                bgl.Position = sub.DataOffset;
                bgl.ReadExactly(data);
                records = [.. BglFile.Records(data).Select(r => DescribeRecord(data.AsMemory(r.Offset, r.Size)))];
            }

            return new BglSubsectionShape(sub.RecordCount, sub.DataSize, records);
        });

        return new BglSectionShape(BglShape.Hex(section.Type), section.SubsectionEntrySize, [.. subsections]);
    }

    private static BglRecordShape DescribeRecord(ReadOnlyMemory<byte> record)
    {
        var span = record.Span;
        var id = BinaryPrimitives.ReadUInt16LittleEndian(span);
        var hexId = BglShape.Hex(id);
        if (id != BglFile.AirportRecordId) return new BglRecordShape(hexId, span.Length, null, null, null, null, null, []);

        var fixedSize = BglFile.FindAirportFixedSize(span);
        var children = fixedSize is { } start
            ? BglFile.Records(record[start..]).Select(c => new BglChildShape(BglShape.Hex(c.Id), c.Size)).ToList()
            : [];
        var name = AirportRecordReader.ReadName(record);

        string? code = null;
        double? latitude = null, longitude = null;
        if (span.Length >= CodeOffset + 4)
        {
            code = AirportCodeCodec.Decode(BinaryPrimitives.ReadUInt32LittleEndian(span[CodeOffset..]));
            var position = AirportRecordReader.ReadPosition(span);
            latitude = position?.Latitude;
            longitude = position?.Longitude;
        }

        return new BglRecordShape(hexId, span.Length, fixedSize, code, name, latitude, longitude, children);
    }
}
