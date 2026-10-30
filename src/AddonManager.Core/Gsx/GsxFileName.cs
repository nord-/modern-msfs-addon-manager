using AddonManager.Core.Content;

namespace AddonManager.Core.Gsx;

/// <summary>What a file in a GSX profile folder is, by its extension (spec 8).</summary>
public enum GsxFileKind
{
    Profile,
    Disabled,
    Script,
}

/// <summary>The file-name rule of spec 8: which GSX file a name is and which Airport code it yields.</summary>
public static class GsxFileName
{
    private const int MinCodeLength = 3;
    private const int MaxCodeLength = 4;

    /// <summary>
    /// Returns false for any extension other than <c>.ini</c>, <c>.off</c> and <c>.py</c>. For those it returns true and sets
    /// <paramref name="code"/> to the leading 3 or 4 letters/digits when they are followed by <c>-</c>, <c>_</c>, <c>.</c> or the end
    /// of the name, else to <c>default</c>.
    /// </summary>
    public static bool TryParse(string fileName, out GsxFileKind kind, out AirportCode code)
    {
        code = default;
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".ini", StringComparison.OrdinalIgnoreCase))
        {
            kind = GsxFileKind.Profile;
        }
        else if (extension.Equals(".off", StringComparison.OrdinalIgnoreCase))
        {
            kind = GsxFileKind.Disabled;
        }
        else if (extension.Equals(".py", StringComparison.OrdinalIgnoreCase))
        {
            kind = GsxFileKind.Script;
        }
        else
        {
            kind = default;
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var run = 0;
        while (run < stem.Length && char.IsAsciiLetterOrDigit(stem[run]))
        {
            run++;
        }

        var endsRun = run == stem.Length || stem[run] is '-' or '_' or '.';
        if (run is >= MinCodeLength and <= MaxCodeLength && endsRun)
        {
            AirportCode.TryParse(stem[..run], out code);
        }

        return true;
    }
}
