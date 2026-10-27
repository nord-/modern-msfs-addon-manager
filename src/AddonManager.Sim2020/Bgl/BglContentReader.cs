using AddonManager.Core.Content;

namespace AddonManager.Sim2020.Bgl;

/// <summary>What a BGL file holds. <paramref name="Readable"/> is false for a corrupt, truncated or encrypted file, which then has no Airports.</summary>
public sealed record BglScan(bool Readable, IReadOnlyList<uint> SectionTypes, IReadOnlyList<AirportRecord> Airports);

/// <summary>Reads the airport records of a BGL file; only the header, the tables and the data of 0x03 subsections are read. Never throws for bad content.</summary>
public static class BglContentReader
{
    private static readonly BglScan Unreadable = new(false, [], []);

    public static BglScan Read(string bglPath)
    {
        using var stream = new FileStream(bglPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Read(stream);
    }

    public static BglScan Read(Stream stream)
    {
        var sections = BglFile.ReadSections(stream);
        if (sections is null) return Unreadable;

        var airports = new List<AirportRecord>();
        try
        {
            foreach (var sub in sections.Where(s => s.Type == BglFile.AirportSectionType).SelectMany(s => s.Subsections))
            {
                var data = new byte[sub.DataSize];
                stream.Position = sub.DataOffset;
                stream.ReadExactly(data);

                var records = BglFile.Records(data).ToList();
                if (!BglFile.IsConsistent(data) || records.Count != sub.RecordCount) return Unreadable;

                foreach (var record in records.Where(r => r.Id == BglFile.AirportRecordId))
                {
                    if (AirportRecordReader.Read(data.AsMemory(record.Offset, record.Size)) is { } airport) airports.Add(airport);
                }
            }
        }
        catch (Exception e) when (e is IOException or NotSupportedException or ObjectDisposedException)
        {
            return Unreadable;
        }

        return new BglScan(true, [.. sections.Select(s => s.Type).Distinct()], airports);
    }
}
