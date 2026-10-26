using System.Text.Json;
using System.Text.Json.Serialization;
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
        Converters = { new JsonStringEnumConverter(), new PackageNameConverter() },
    };

    public UserState Load()
    {
        if (!File.Exists(filePath))
            return new UserState();

        try
        {
            return JsonSerializer.Deserialize<UserState>(File.ReadAllText(filePath), Options)
                   ?? throw new UserStateCorruptException($"'{filePath}' holds no user data.");
        }
        catch (JsonException ex)
        {
            throw new UserStateCorruptException($"'{filePath}' is not valid user data.", ex);
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

    private sealed class PackageNameConverter : JsonConverter<PackageName>
    {
        public override PackageName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString() ?? throw new JsonException("Package name is null."));

        public override void Write(Utf8JsonWriter writer, PackageName value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);

        public override PackageName ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Read(ref reader, typeToConvert, options);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, PackageName value, JsonSerializerOptions options)
            => writer.WritePropertyName(value.Value);
    }
}
