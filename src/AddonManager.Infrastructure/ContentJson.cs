using System.Text.Json;

namespace AddonManager.Infrastructure;

/// <summary>
/// JSON options for <c>PackageContent</c> rows in the scan cache. A missing constructor member, or <c>null</c> for a
/// non-nullable one, throws <see cref="JsonException"/>, so the cache treats such a row as corrupt and rebuilds.
/// </summary>
internal static class ContentJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonConverters.PackageNameConverter(), new JsonConverters.AirportCodeConverter() },
    };
}
