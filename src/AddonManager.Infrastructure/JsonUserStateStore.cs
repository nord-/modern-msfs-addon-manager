using System.Text.Json;
using System.Text.Json.Serialization;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Infrastructure;

public sealed class UserStateCorruptException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Stores user data as human-readable JSON; keeps the previous file as <c>.bak</c>.</summary>
public sealed class JsonUserStateStore(string filePath) : IUserStateStore
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new JsonConverters.PackageNameConverter(), new JsonConverters.AirportCodeConverter() },
    };

    public UserState Load()
    {
        if (!File.Exists(filePath))
            return new UserState();

        try
        {
            var state = JsonSerializer.Deserialize<UserState>(File.ReadAllText(filePath), Options)
                        ?? throw new UserStateCorruptException($"'{filePath}' holds no user data.");
            FillNullContent(state);
            DropInvalidCodes(state.Content.ManualAirportLinks);
            DropInvalidCodes(state.Content.RemovedAirportLinks);
            return state;
        }
        catch (JsonException ex)
        {
            throw new UserStateCorruptException($"'{filePath}' is not valid user data.", ex);
        }
    }

    // A hand-edited file may hold null for the content settings or their collections; null means empty, like a missing member.
    private static void FillNullContent(UserState state)
    {
        var content = state.Content ??= new ContentSettings();
        content.AddedExclusionPatterns ??= new(StringComparer.OrdinalIgnoreCase);
        content.RemovedExclusionPatterns ??= new(StringComparer.OrdinalIgnoreCase);
        content.AddedStopWords ??= new(StringComparer.OrdinalIgnoreCase);
        content.RemovedStopWords ??= new(StringComparer.OrdinalIgnoreCase);
        content.OptedIn ??= [];
        content.ManualAirportLinks ??= [];
        content.RemovedAirportLinks ??= [];
    }

    // An invalid code in user data reads as default(AirportCode); it is dropped, never an error (C3). A null set is dropped too.
    private static void DropInvalidCodes(Dictionary<PackageName, HashSet<AirportCode>> links)
    {
        foreach (var (name, codes) in links.ToList())
        {
            codes?.Remove(default);
            if (codes is null || codes.Count == 0)
            {
                links.Remove(name);
            }
        }
    }

    public void Save(UserState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);

        var json = JsonSerializer.SerializeToNode(state, Options)!.AsObject();
        json.Insert(0, "schemaVersion", SchemaVersion);

        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, json.ToJsonString(Options));
        if (File.Exists(filePath))
            File.Copy(filePath, filePath + ".bak", overwrite: true);
        File.Move(tmp, filePath, overwrite: true);
    }
}
