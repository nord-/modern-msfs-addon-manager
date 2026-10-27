using System.Buffers.Binary;

namespace AddonManager.Sim2020.Bgl;

public sealed record BglSubsection(int RecordCount, long DataOffset, int DataSize);

public sealed record BglSection(uint Type, int SubsectionEntrySize, IReadOnlyList<BglSubsection> Subsections);

/// <summary><paramref name="Offset"/> is relative to the start of the buffer the record was read from.</summary>
public readonly record struct BglRecord(ushort Id, int Offset, int Size);

/// <summary>Reads the structure of a BGL file (header, section and subsection tables, record headers). Never throws for bad content.</summary>
public static class BglFile
{
    public const uint AirportSectionType = 0x03;
    public const ushort AirportRecordId = 0x0056;
    public const ushort NameRecordId = 0x0019;

    private const uint Magic = 0x19920201;
    private const int HeaderSize = 0x38;
    private const int SectionEntrySize = 20;
    private const int RecordHeaderSize = 6;
    private const int AirportFixedSize = 0x44;

    /// <summary>The sections of the file, or null when it is unreadable (short, wrong magic or a table or data range outside the file).</summary>
    public static IReadOnlyList<BglSection>? ReadSections(Stream stream)
    {
        try
        {
            var length = stream.Length;
            if (length < HeaderSize) return null;

            var header = ReadAt(stream, 0, HeaderSize);
            if (header is null || BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic) return null;

            var sectionCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0x14));
            if (sectionCount > (length - HeaderSize) / SectionEntrySize) return null;

            var table = ReadAt(stream, HeaderSize, (int)sectionCount * SectionEntrySize);
            if (table is null) return null;

            var sections = new List<BglSection>();
            for (var i = 0; i < sectionCount; i++)
            {
                var entry = table.AsSpan(i * SectionEntrySize, SectionEntrySize);
                var type = BinaryPrimitives.ReadUInt32LittleEndian(entry);
                var sizeFlag = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
                var count = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
                var tableOffset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);

                var entrySize = (int)(((sizeFlag & 0x10000) | 0x40000) >> 0x0E);
                if (count > length / entrySize || tableOffset + (long)count * entrySize > length) return null;

                var subTable = ReadAt(stream, tableOffset, (int)count * entrySize);
                if (subTable is null) return null;

                var subsections = new List<BglSubsection>();
                for (var j = 0; j < count; j++)
                {
                    var sub = subTable.AsSpan(j * entrySize, entrySize);
                    var tail = entrySize - 12;
                    var recordCount = BinaryPrimitives.ReadUInt32LittleEndian(sub[tail..]);
                    var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(sub[(tail + 4)..]);
                    var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(sub[(tail + 8)..]);
                    if (dataSize > int.MaxValue || recordCount > int.MaxValue || (long)dataOffset + dataSize > length) return null;
                    subsections.Add(new BglSubsection((int)recordCount, dataOffset, (int)dataSize));
                }

                sections.Add(new BglSection(type, entrySize, subsections));
            }

            return sections;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>Walks the <c>u16 id, u32 size</c> records of a buffer; stops at a size below 6 or past the end.</summary>
    public static IEnumerable<BglRecord> Records(ReadOnlyMemory<byte> data)
    {
        var offset = 0;
        while (TryReadHeader(data.Span, offset, out var id, out var size))
        {
            yield return new BglRecord(id, offset, size);
            offset += size;
        }
    }

    /// <summary>True when the records walk exactly to the end of the buffer.</summary>
    public static bool IsConsistent(ReadOnlyMemory<byte> data)
    {
        var end = 0;
        foreach (var record in Records(data)) end = record.Offset + record.Size;
        return end == data.Length;
    }

    /// <summary>
    /// The size of the fixed part of an airport record (header included): 0x44 when the child records end exactly at the record's end, else null.
    /// Every one of 81,521 airport records on the install has a 0x44 fixed part; other sizes only walk cleanly by coincidence (ANALYS.md).
    /// </summary>
    public static int? FindAirportFixedSize(ReadOnlySpan<byte> record)
    {
        if (AirportFixedSize > record.Length) return null;
        var position = AirportFixedSize;
        while (TryReadHeader(record, position, out _, out var size)) position += size;
        return position == record.Length ? AirportFixedSize : null;
    }

    private static bool TryReadHeader(ReadOnlySpan<byte> data, int offset, out ushort id, out int size)
    {
        id = 0;
        size = 0;
        if (offset < 0 || offset + RecordHeaderSize > data.Length) return false;
        id = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 2)..]);
        if (raw < RecordHeaderSize || raw > data.Length - offset) return false;
        size = (int)raw;
        return true;
    }

    private static byte[]? ReadAt(Stream stream, long offset, int count)
    {
        var buffer = new byte[count];
        stream.Position = offset;
        return stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false) == count ? buffer : null;
    }
}
