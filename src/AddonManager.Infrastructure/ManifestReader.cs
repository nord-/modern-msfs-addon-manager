using System.Text.Json;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure;

/// <summary>Reads the fields the app uses from <c>manifest.json</c>. Never throws for bad content.</summary>
public static class ManifestReader
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static Manifest Read(string manifestPath)
    {
        try
        {
            // ReadAllText drops a UTF-8 BOM.
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath), Options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Manifest.Empty;

            return new Manifest(
                String(root, "title"),
                String(root, "creator"),
                String(root, "content_type"),
                String(root, "package_version"),
                Dependencies(root));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Manifest.Empty;
        }
    }

    private static string? String(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static List<string> Dependencies(JsonElement root)
    {
        if (!root.TryGetProperty("dependencies", out var deps) || deps.ValueKind != JsonValueKind.Array)
            return [];

        return deps.EnumerateArray()
            .Where(d => d.ValueKind == JsonValueKind.Object)
            .Select(d => String(d, "name"))
            .OfType<string>()
            .ToList();
    }
}
