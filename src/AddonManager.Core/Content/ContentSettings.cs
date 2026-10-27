using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Content;

/// <summary>The user's content-scan choices: exclusions, stop words and Package-level Airport link inputs (plan 2).</summary>
public sealed class ContentSettings
{
    public static readonly IReadOnlyList<string> DefaultExclusionPatterns =
        ["navigraph-navdata*", "*-point-of-interest", "fsdreamteam-gsx-world-of-jetways*", "microsoft-airport-voloport", "neofly-*"];

    public static readonly IReadOnlyList<string> DefaultStopList = ["PACK", "SAVE", "SWIM", "SANS"];

    private int version;
    private (int Key, Regex[] Regexes)? matchers;

    public int AirportThreshold { get; set; } = 10;

    // Only the user's changes to the two lists are stored, so defaults added in a later release reach existing users.
    // Populate makes a JSON load fill these case-insensitive sets instead of replacing them.
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public HashSet<string> AddedExclusionPatterns { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public HashSet<string> RemovedExclusionPatterns { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public HashSet<string> AddedStopWords { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public HashSet<string> RemovedStopWords { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<PackageName> OptedIn { get; set; } = [];

    public Dictionary<PackageName, HashSet<AirportCode>> ManualAirportLinks { get; set; } = [];

    public Dictionary<PackageName, HashSet<AirportCode>> RemovedAirportLinks { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<string> ExclusionPatterns => Effective(DefaultExclusionPatterns, AddedExclusionPatterns, RemovedExclusionPatterns);

    [JsonIgnore]
    public IReadOnlyList<string> StopList => Effective(DefaultStopList, AddedStopWords, RemovedStopWords);

    public void AddExclusionPattern(string pattern)
    {
        Add(pattern, DefaultExclusionPatterns, AddedExclusionPatterns, RemovedExclusionPatterns);
        version++;
    }

    public void RemoveExclusionPattern(string pattern)
    {
        Remove(pattern, DefaultExclusionPatterns, AddedExclusionPatterns, RemovedExclusionPatterns);
        version++;
    }

    public void AddStopWord(string word) => Add(word, DefaultStopList, AddedStopWords, RemovedStopWords);

    public void RemoveStopWord(string word) => Remove(word, DefaultStopList, AddedStopWords, RemovedStopWords);

    /// <summary>Whole-name, case-insensitive match against the exclusion patterns; <c>*</c> is the only wildcard.</summary>
    public bool MatchesExclusion(PackageName name)
    {
        var key = HashCode.Combine(version, AddedExclusionPatterns.Count, RemovedExclusionPatterns.Count);
        var cached = matchers;
        if (cached is null || cached.Value.Key != key)
        {
            // Rebuilt after Add/Remove (version) or when a load changed the sets (counts); Populate mutates them in place.
            cached = (key, ExclusionPatterns
                .Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline))
                .ToArray());
            matchers = cached;
        }

        return cached.Value.Regexes.Any(r => r.IsMatch(name.Value));
    }

    public bool IsStopWord(string token) => StopList.Contains(token, StringComparer.OrdinalIgnoreCase);

    public void LinkManually(PackageName name, AirportCode code)
    {
        Include(ManualAirportLinks, name, code);
        Exclude(RemovedAirportLinks, name, code);
    }

    public void RemoveLink(PackageName name, AirportCode code)
    {
        Include(RemovedAirportLinks, name, code);
        Exclude(ManualAirportLinks, name, code);
    }

    public ContentSettings Clone() => new()
    {
        AirportThreshold = AirportThreshold,
        AddedExclusionPatterns = new(AddedExclusionPatterns, StringComparer.OrdinalIgnoreCase),
        RemovedExclusionPatterns = new(RemovedExclusionPatterns, StringComparer.OrdinalIgnoreCase),
        AddedStopWords = new(AddedStopWords, StringComparer.OrdinalIgnoreCase),
        RemovedStopWords = new(RemovedStopWords, StringComparer.OrdinalIgnoreCase),
        OptedIn = [.. OptedIn],
        ManualAirportLinks = CloneLinks(ManualAirportLinks),
        RemovedAirportLinks = CloneLinks(RemovedAirportLinks),
    };

    /// <summary>Drops the settings of a Package that is gone, so a later Package with the same name starts clean (spec 6.1).</summary>
    public void Forget(PackageName name)
    {
        OptedIn.Remove(name);
        ManualAirportLinks.Remove(name);
        RemovedAirportLinks.Remove(name);
    }

    private static List<string> Effective(IReadOnlyList<string> defaults, HashSet<string> added, HashSet<string> removed)
        => defaults.Concat(added)
            .Where(x => !removed.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void Add(string value, IReadOnlyList<string> defaults, HashSet<string> added, HashSet<string> removed)
    {
        // Un-removes a default; anything else is stored as an addition.
        removed.Remove(value);
        if (!IsDefault(value, defaults))
        {
            added.Add(value);
        }
    }

    private static void Remove(string value, IReadOnlyList<string> defaults, HashSet<string> added, HashSet<string> removed)
    {
        // Un-adds an added entry; a default is stored as a removal.
        added.Remove(value);
        if (IsDefault(value, defaults))
        {
            removed.Add(value);
        }
    }

    private static bool IsDefault(string value, IReadOnlyList<string> defaults) => defaults.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static void Include(Dictionary<PackageName, HashSet<AirportCode>> map, PackageName name, AirportCode code)
    {
        if (!map.TryGetValue(name, out var set))
        {
            map[name] = set = [];
        }

        set.Add(code);
    }

    private static void Exclude(Dictionary<PackageName, HashSet<AirportCode>> map, PackageName name, AirportCode code)
    {
        if (map.TryGetValue(name, out var set) && set.Remove(code) && set.Count == 0)
        {
            map.Remove(name);
        }
    }

    private static Dictionary<PackageName, HashSet<AirportCode>> CloneLinks(Dictionary<PackageName, HashSet<AirportCode>> map)
        => map.ToDictionary(kv => kv.Key, kv => new HashSet<AirportCode>(kv.Value));
}
