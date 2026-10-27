using System.Buffers.Binary;
using System.Text;
using AddonManager.Core.Content;

namespace AddonManager.Sim2020.Bgl;

/// <summary>Reads one airport record (id 0x0056): code, position and name. Never throws for bad content.</summary>
public static class AirportRecordReader
{
    private const int LongitudeOffset = 0x0C;
    private const int LatitudeOffset = 0x10;
    private const int CodeOffset = 0x28;
    private const int ChildHeaderSize = 6;
    private const uint MaxRawLongitude = 3 * 0x10000000;
    private const uint MaxRawLatitude = 2 * 0x10000000;

    /// <summary>The record, or null when its code is missing or not a valid Airport code. The name is raw (a <c>TT:</c> key stays a key) and null when no name child can be found.</summary>
    public static AirportRecord? Read(ReadOnlyMemory<byte> record)
    {
        var span = record.Span;
        if (span.Length < CodeOffset + 4) return null;

        var code = AirportCodeCodec.Decode(BinaryPrimitives.ReadUInt32LittleEndian(span[CodeOffset..]));
        if (!AirportCode.TryParse(code, out var airportCode)) return null;

        return new AirportRecord(airportCode, ReadName(record), ReadPosition(span));
    }

    /// <summary>The position, or null when the raw values lie outside the globe (a corrupt record).</summary>
    public static GeoPoint? ReadPosition(ReadOnlySpan<byte> record)
    {
        var rawLongitude = BinaryPrimitives.ReadUInt32LittleEndian(record[LongitudeOffset..]);
        var rawLatitude = BinaryPrimitives.ReadUInt32LittleEndian(record[LatitudeOffset..]);
        if (rawLongitude > MaxRawLongitude || rawLatitude > MaxRawLatitude)
            return null;

        return new GeoPoint(90.0 - rawLatitude * 180.0 / MaxRawLatitude, rawLongitude * 360.0 / MaxRawLongitude - 180.0);
    }

    /// <summary>The first 0x0019 child's bytes after its header up to the first NUL or the end of the child (a name that fills its padded child has no NUL), or null when no record size fits or there is no name child.</summary>
    public static string? ReadName(ReadOnlyMemory<byte> record)
    {
        if (BglFile.FindAirportFixedSize(record.Span) is not { } start) return null;

        foreach (var child in BglFile.Records(record[start..]))
        {
            if (child.Id != BglFile.NameRecordId) continue;
            var text = record.Span.Slice(start + child.Offset + ChildHeaderSize, child.Size - ChildHeaderSize);
            var end = text.IndexOf((byte)0);
            return Encoding.UTF8.GetString(end < 0 ? text : text[..end]);
        }

        return null;
    }
}
