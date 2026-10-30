using AddonManager.Core.Packages;
using AddonManager.Infrastructure;

namespace AddonManager.Cli;

/// <summary>Extracts the repeatable <c>--library &lt;path&gt;[=&lt;types&gt;]</c> option from a command's arguments.</summary>
internal static class LibraryOptions
{
    public const string NoneRegistered = "No Libraries registered; pass --library <path>[=sections].";

    public static bool TryParse(string[] args, out IReadOnlyList<LibraryRoot> libraries, out string[] rest, out string? error)
    {
        var parsed = new List<LibraryRoot>();
        var others = new List<string>();
        libraries = parsed;
        rest = [];
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != "--library")
            {
                others.Add(args[i]);
                continue;
            }

            // An option right after --library is a missing value, not a folder named "--full".
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = "--library needs a value: <path>[=sections].";
                return false;
            }

            if (!LibraryArgument.TryParse(args[++i], out var library, out error))
                return false;
            parsed.Add(library!);
        }

        rest = [.. others];
        return true;
    }
}
