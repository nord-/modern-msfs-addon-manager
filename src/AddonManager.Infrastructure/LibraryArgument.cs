using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure;

/// <summary>Parses a Library given as text: <c>&lt;path&gt;[=&lt;types&gt;]</c> with types a comma list of Scenery, Aircraft, Other. A path alone holds all three.</summary>
public static class LibraryArgument
{
    public static bool TryParse(string value, out LibraryRoot? library, out string? error)
    {
        library = null;
        error = null;

        var path = value;
        IReadOnlyList<Category> types = LibraryRoot.AllSections;

        // Split at the last '=' only when what follows is not a path, so a '=' inside a folder name stays part of the path.
        var at = value.LastIndexOf('=');
        if (at >= 0 && value.IndexOfAny(['\\', '/', ':'], at + 1) < 0)
        {
            var parsed = new List<Category>();
            foreach (var item in value[(at + 1)..].Split(',', StringSplitOptions.TrimEntries))
            {
                if (item.Length == 0)
                {
                    error = "Missing Library type after '=' or ','. Use Scenery, Aircraft or Other.";
                    return false;
                }

                // Matching the names keeps a number such as "1" from parsing as an enum value.
                var name = Enum.GetNames<Category>().FirstOrDefault(n => string.Equals(n, item, StringComparison.OrdinalIgnoreCase));
                if (name is null)
                {
                    error = $"Unknown Library type '{item}'. Use Scenery, Aircraft or Other.";
                    return false;
                }

                parsed.Add(Enum.Parse<Category>(name));
            }

            path = value[..at];
            types = parsed;
        }

        library = new LibraryRoot(path, types);
        return true;
    }
}
