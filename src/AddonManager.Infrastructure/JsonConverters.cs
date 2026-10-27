using System.Text.Json;
using System.Text.Json.Serialization;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure;

/// <summary>JSON converters for the domain value types, shared by the user-data store and the scan cache.</summary>
internal static class JsonConverters
{
    public sealed class PackageNameConverter : JsonConverter<PackageName>
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

    /// <summary>Reads an invalid code as <c>default</c> (C3); callers drop those.</summary>
    public sealed class AirportCodeConverter : JsonConverter<AirportCode>
    {
        public override AirportCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                return default;
            }

            AirportCode.TryParse(reader.GetString(), out var code);
            return code;
        }

        public override void Write(Utf8JsonWriter writer, AirportCode value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);

        public override AirportCode ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // The token is a PropertyName here, so Read would skip it as a non-string.
            AirportCode.TryParse(reader.GetString(), out var code);
            return code;
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, AirportCode value, JsonSerializerOptions options)
            => writer.WritePropertyName(value.Value);
    }
}
