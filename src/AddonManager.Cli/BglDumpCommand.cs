using System.Text.Json;
using AddonManager.Sim2020.Bgl;

namespace AddonManager.Cli;

/// <summary>Dumps the structure of a BGL file as JSON. Read-only; the only file ever written is the explicit --out file.</summary>
internal static class BglDumpCommand
{
    public const string Usage = "Usage: AddonManager.Cli bgl-dump <file-or-package-folder> [--code <code>] [--out <file>]";

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? path = null, code = null, outFile = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--code" when i + 1 < args.Length: code = args[++i]; break;
                case "--out" when i + 1 < args.Length: outFile = args[++i]; break;
                case var a when !a.StartsWith("--") && path is null: path = a; break;
                default:
                    stderr.WriteLine(Usage);
                    return 2;
            }
        }

        if (path is null)
        {
            stderr.WriteLine(Usage);
            return 2;
        }

        var chosen = Directory.Exists(path) ? ChooseInFolder(path, code, stderr) : ChooseFile(path, code, stderr);
        if (chosen is null) return 1;

        stderr.WriteLine(chosen.Value.File);
        var json = JsonSerializer.Serialize(chosen.Value.Shape, BglShapes.JsonOptions);
        if (outFile is null) stdout.WriteLine(json);
        else File.WriteAllText(outFile, json);
        return 0;
    }

    private static (string File, BglShape Shape)? ChooseFile(string file, string? code, TextWriter stderr)
    {
        var shape = Describe(file);
        if (shape is null)
        {
            stderr.WriteLine($"Not a readable BGL file: {file}");
            return null;
        }

        if (code is not null && !HasCode(shape, code))
        {
            stderr.WriteLine($"No airport record with code {code} in {file}");
            return null;
        }

        return (file, shape);
    }

    private static (string File, BglShape Shape)? ChooseInFolder(string folder, string? code, TextWriter stderr)
    {
        var files = Directory.EnumerateFiles(folder, "*.bgl", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Order(StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
        {
            stderr.WriteLine($"No .bgl files under {folder}");
            return null;
        }

        (string File, BglShape Shape)? firstReadable = null;
        foreach (var file in files)
        {
            var shape = Describe(file);
            if (shape is null) continue;
            if (code is not null)
            {
                if (HasCode(shape, code)) return (file, shape);
                continue;
            }

            if (shape.Sections.Any(s => s.Type == BglShape.Hex(BglFile.AirportSectionType))) return (file, shape);
            firstReadable ??= (file, shape);
        }

        if (code is not null)
        {
            stderr.WriteLine($"No airport record with code {code} under {folder}");
            return null;
        }

        if (firstReadable is null) stderr.WriteLine($"No readable .bgl files under {folder}");
        return firstReadable;
    }

    private static bool HasCode(BglShape shape, string code) => shape.Sections
        .SelectMany(s => s.Subsections)
        .SelectMany(s => s.Records ?? [])
        .Any(r => r.Id == BglShape.Hex(BglFile.AirportRecordId) && string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));

    private static BglShape? Describe(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return BglShapes.Describe(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
