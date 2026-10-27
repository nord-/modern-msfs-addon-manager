using System.Text.Json;
using AddonManager.Core.Content;

namespace AddonManager.Sim2020.Content;

/// <summary>
/// Resolves airport record names. Official Asobo airports carry a localisation key (<c>TT:AIRPORTLR.LOWI.name</c>)
/// instead of a name; the text is in <c>fs-base-genericairports\en-US.locPak</c> in the Official folder.
/// The file is read once, on the first key. Read-only; never throws for bad content.
/// </summary>
public sealed class AirportNames(string? officialPath)
{
    private const string KeyPrefix = "TT:";
    private const string NameSuffix = ".name";

    private readonly Lazy<Table> _table = new(() => Load(officialPath), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The airport's name, or null when it has none: empty, or a key the locPak can't resolve. A key is looked up exactly;
    /// failing that, the code in the key picks the first region in file order that has it. <paramref name="code"/> is
    /// intentionally unused: the key's own code decides, not the record's.
    /// </summary>
    public string? Resolve(string? rawName, AirportCode code)
    {
        var name = rawName?.Trim();
        if (string.IsNullOrEmpty(name))
            return null;
        if (!name.StartsWith(KeyPrefix, StringComparison.Ordinal))
            return name;

        var key = name[KeyPrefix.Length..];
        var table = _table.Value;
        if (table.ByKey.TryGetValue(key, out var exact))
            return exact;
        return CodeOf(key) is { } keyCode && table.ByCode.TryGetValue(keyCode, out var byCode) ? byCode : null;
    }

    /// <summary>The code part of a key shaped <c>AIRPORT&lt;region&gt;.&lt;code&gt;.name</c>, else null.</summary>
    private static string? CodeOf(string key)
    {
        var parts = key.Split('.');
        return parts.Length == 3
            && parts[0].StartsWith("AIRPORT", StringComparison.Ordinal)
            && parts[2] == "name"
            && parts[1].Length > 0
                ? parts[1]
                : null;
    }

    private static Table Load(string? officialPath)
    {
        var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var byCode = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (officialPath is null)
                return new Table(byKey, byCode);

            var path = Path.Combine(officialPath, "fs-base-genericairports", "en-US.locPak");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            foreach (var entry in doc.RootElement.GetProperty("LocalisationPackage").GetProperty("Strings").EnumerateObject())
            {
                if (!entry.Name.EndsWith(NameSuffix, StringComparison.Ordinal) || entry.Value.ValueKind != JsonValueKind.String)
                    continue;
                var value = entry.Value.GetString()!;
                byKey[entry.Name] = value;
                if (CodeOf(entry.Name) is { } code)
                    byCode.TryAdd(code, value);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
            or KeyNotFoundException or NotSupportedException or ArgumentException)
        {
            byKey.Clear();
            byCode.Clear();
        }
        return new Table(byKey, byCode);
    }

    private sealed record Table(Dictionary<string, string> ByKey, Dictionary<string, string> ByCode);
}
