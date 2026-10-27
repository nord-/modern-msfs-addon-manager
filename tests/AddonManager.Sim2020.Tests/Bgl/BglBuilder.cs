using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AddonManager.Sim2020.Tests.Bgl;

/// <summary>Builds synthetic BGL files in memory; no real sim bytes are ever used.</summary>
public sealed class BglBuilder
{
    private const int HeaderSize = 0x38;
    private const int SectionEntrySize = 20;

    private readonly List<(uint Type, int EntrySize, List<(int RecordCount, byte[] Data)> Subsections)> _sections = [];

    public BglBuilder Section(uint type, int entrySize = 16)
    {
        _sections.Add((type, entrySize, []));
        return this;
    }

    public BglBuilder Subsection(params byte[][] records)
    {
        _sections[^1].Subsections.Add((records.Length, [.. records.SelectMany(r => r)]));
        return this;
    }

    /// <summary>A subsection with an explicit record count and raw data, for shapes whose records are not described.</summary>
    public BglBuilder RawSubsection(int recordCount, byte[] data)
    {
        _sections[^1].Subsections.Add((recordCount, data));
        return this;
    }

    public byte[] Build()
    {
        var tableStart = HeaderSize + _sections.Count * SectionEntrySize;
        var subsectionTables = new List<int>();
        var cursor = tableStart;
        foreach (var section in _sections)
        {
            subsectionTables.Add(cursor);
            cursor += section.Subsections.Count * section.EntrySize;
        }

        var file = new List<byte>();
        file.AddRange(new byte[HeaderSize]);
        WriteU32(file, 0x00, 0x19920201);
        WriteU32(file, 0x14, (uint)_sections.Count);

        for (var i = 0; i < _sections.Count; i++)
        {
            var (type, entrySize, subs) = _sections[i];
            AddU32(file, type);
            AddU32(file, entrySize == 20 ? 0x10000u : 0u);
            AddU32(file, (uint)subs.Count);
            AddU32(file, (uint)subsectionTables[i]);
            AddU32(file, (uint)(subs.Count * entrySize));
        }

        var dataCursor = cursor;
        var data = new List<byte>();
        foreach (var (_, entrySize, subs) in _sections)
        {
            foreach (var (recordCount, bytes) in subs)
            {
                AddU32(file, 0);
                if (entrySize == 20) AddU32(file, 0);
                AddU32(file, (uint)recordCount);
                AddU32(file, (uint)(dataCursor + data.Count));
                AddU32(file, (uint)bytes.Length);
                data.AddRange(bytes);
            }
        }

        file.AddRange(data);
        return [.. file];
    }

    public static byte[] Record(ushort id, byte[] body)
    {
        var record = new byte[6 + body.Length];
        BitConverter.GetBytes(id).CopyTo(record, 0);
        BitConverter.GetBytes((uint)record.Length).CopyTo(record, 2);
        body.CopyTo(record, 6);
        return record;
    }

    public static byte[] Airport(string code, string? name, double lat, double lon, int fixedSize)
    {
        var nameChild = name is null ? [] : Record(0x0019, [.. Encoding.UTF8.GetBytes(name), 0]);
        var record = new byte[fixedSize + nameChild.Length];
        BitConverter.GetBytes((ushort)0x0056).CopyTo(record, 0);
        BitConverter.GetBytes((uint)record.Length).CopyTo(record, 2);
        BitConverter.GetBytes((uint)((lon + 180.0) * 3 * 0x10000000 / 360.0)).CopyTo(record, 0x0C);
        BitConverter.GetBytes((uint)((90.0 - lat) * 2 * 0x10000000 / 180.0)).CopyTo(record, 0x10);
        BitConverter.GetBytes(AirportCodeCodec.Encode(code)).CopyTo(record, 0x28);
        nameChild.CopyTo(record, fixedSize);
        return record;
    }

    public static BglShape LoadShape(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "bgl-shapes", name + ".json");
        return JsonSerializer.Deserialize<BglShape>(File.ReadAllText(path), BglShapes.JsonOptions)!;
    }

    /// <summary>Rebuilds a file with the shape's ids and sizes; code, position and name sit at the confirmed offsets, every other byte is zero.</summary>
    public static byte[] FromShape(BglShape shape)
    {
        var builder = new BglBuilder();
        foreach (var section in shape.Sections)
        {
            builder.Section(ParseHex(section.Type), section.SubsectionEntrySize);
            foreach (var sub in section.Subsections)
            {
                if (sub.Records is null) builder.RawSubsection(sub.RecordCount, new byte[sub.DataSize]);
                else builder.Subsection([.. sub.Records.Select(RecordFromShape)]);
            }
        }

        return builder.Build();
    }

    private static uint ParseHex(string hex) => uint.Parse(hex.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static byte[] RecordFromShape(BglRecordShape shape)
    {
        var record = new byte[shape.Size];
        BitConverter.GetBytes((ushort)ParseHex(shape.Id)).CopyTo(record, 0);
        BitConverter.GetBytes((uint)shape.Size).CopyTo(record, 2);
        if (shape.Code is not null && shape.Latitude is { } lat && shape.Longitude is { } lon && record.Length >= 0x2C)
        {
            BitConverter.GetBytes((uint)Math.Round((lon + 180.0) * 3 * 0x10000000 / 360.0)).CopyTo(record, 0x0C);
            BitConverter.GetBytes((uint)Math.Round((90.0 - lat) * 2 * 0x10000000 / 180.0)).CopyTo(record, 0x10);
            BitConverter.GetBytes(AirportCodeCodec.Encode(shape.Code)).CopyTo(record, 0x28);
        }

        if (shape.FixedSize is { } offset)
        {
            var nameWritten = false;
            foreach (var child in shape.Children)
            {
                BitConverter.GetBytes((ushort)ParseHex(child.Id)).CopyTo(record, offset);
                BitConverter.GetBytes((uint)child.Size).CopyTo(record, offset + 2);
                if (shape.Name is not null && !nameWritten && ParseHex(child.Id) == 0x19)
                {
                    // The name is padded to the child's size and not always NUL-terminated: copy the bytes, never a terminator.
                    Encoding.UTF8.GetBytes(shape.Name).CopyTo(record, offset + 6);
                    nameWritten = true;
                }

                offset += child.Size;
            }
        }

        return record;
    }

    public static byte[] Broken(string kind)
    {
        var valid = new BglBuilder().Section(0x03)
            .Subsection(Airport("EHAM", "Amsterdam Schiphol", 52.3086, 4.7639, 0x44)).Build();
        switch (kind)
        {
            case "wrong-magic":
                WriteU32(valid, 0x00, 0xDEADBEEF);
                return valid;
            case "truncated-header":
                return valid[..10];
            case "table-past-end":
                WriteU32(valid, 0x14, 1000);
                return valid;
            case "data-past-end":
                // First (only) subsection entry sits right after the one section entry: id, recordCount, dataOffset, dataSize.
                WriteU32(valid, HeaderSize + SectionEntrySize + 12, 100000);
                return valid;
            case "random":
                var random = new byte[300];
                new Random(42).NextBytes(random);
                return random;
            case "bad-record-size":
                var good = Airport("EHAM", "Amsterdam Schiphol", 52.3086, 4.7639, 0x44);
                return new BglBuilder().Section(0x03).Subsection(good, [0x19, 0x00, 0x02, 0, 0, 0]).Build();
            default:
                throw new ArgumentException(kind);
        }
    }

    private static void AddU32(List<byte> list, uint value) => list.AddRange(BitConverter.GetBytes(value));

    private static void WriteU32(List<byte> list, int offset, uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        for (var i = 0; i < 4; i++) list[offset + i] = bytes[i];
    }

    private static void WriteU32(byte[] array, int offset, uint value) => BitConverter.GetBytes(value).CopyTo(array, offset);
}
