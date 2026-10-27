namespace AddonManager.Sim2020.Content;

/// <summary>What the scan needs from an <c>aircraft.cfg</c>: the last segment of <c>base_container</c> and whether it is AI traffic.</summary>
public sealed record CfgFacts(string? BaseContainer, bool IsAirTraffic);

/// <summary>Reads <c>aircraft.cfg</c> files. Read-only; never throws for bad content.</summary>
public static class AircraftCfgReader
{
    /// <summary>The facts in the file, or null when it can't be read.</summary>
    public static CfgFacts? Read(string cfgPath)
    {
        try
        {
            using var stream = new FileStream(cfgPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            string? baseContainer = null;
            var airTraffic = false;
            while (reader.ReadLine() is { } line)
            {
                var text = StripComment(line);
                var eq = text.IndexOf('=');
                if (eq < 0)
                    continue;
                var key = text[..eq].Trim();
                var value = text[(eq + 1)..].Trim().Trim('"').Trim();

                if (key.Equals("base_container", StringComparison.OrdinalIgnoreCase))
                    baseContainer ??= LastSegment(value);
                else if (key.Equals("isAirTraffic", StringComparison.OrdinalIgnoreCase) && value == "1")
                    airTraffic = true;
            }
            return new CfgFacts(baseContainer, airTraffic);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The last path segment of a <c>base_container</c> value (<c>"..\X"</c> gives <c>X</c>, <c>../x/</c> gives <c>x</c>), or null when empty.</summary>
    public static string? LastSegment(string? baseContainer)
    {
        var segment = baseContainer?.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        return string.IsNullOrEmpty(segment) ? null : segment;
    }

    private static string StripComment(string line)
    {
        var cut = line.Length;
        var semi = line.IndexOf(';');
        if (semi >= 0)
            cut = semi;
        var slashes = line.IndexOf("//", StringComparison.Ordinal);
        if (slashes >= 0 && slashes < cut)
            cut = slashes;
        return line[..cut];
    }
}
