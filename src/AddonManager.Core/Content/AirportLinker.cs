using System.Text.RegularExpressions;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Content;

public enum AirportLinkReason
{
    Record,
    Token,
    PackageName,
    Manual,
}

public sealed record AirportLink(PackageContent Package, AirportCode Code, AirportLinkReason Reason);

/// <summary>
/// Manual ICAO code links and removals per Package name, derived from the cards (spec 6.5 rule 3 and 11). A removal drops an
/// automatic link first; a manual link is applied after it, so a manual link wins.
/// </summary>
public sealed record AirportLinkOverrides(
    IReadOnlyDictionary<PackageName, IReadOnlySet<AirportCode>> Manual,
    IReadOnlyDictionary<PackageName, IReadOnlySet<AirportCode>> Removed)
{
    public static AirportLinkOverrides None { get; } = new(
        new Dictionary<PackageName, IReadOnlySet<AirportCode>>(),
        new Dictionary<PackageName, IReadOnlySet<AirportCode>>());
}

/// <summary>Links Packages to Airport codes (spec 6.5, content rules C4, C5 and C7).</summary>
public static class AirportLinker
{
    private const int MinCodeLength = 3;

    private static readonly Regex OfficialAirportName = new(
        "-airport-([a-z0-9]{3,5})(?=-|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>One name or title token that is a known code; <c>Reason</c> says why it is not linked and is null when it is.</summary>
    public sealed record TokenCandidate(string Source, AirportCode Code, bool Linked, string? Reason);

    /// <summary><paramref name="remembered"/> records count as known ICAO codes for the name token rule (spec 11).</summary>
    public static IReadOnlyList<AirportLink> Link(
        IReadOnlyList<PackageContent> contents,
        ContentSettings settings,
        AirportLinkOverrides overrides,
        IReadOnlyList<AirportRecord>? remembered = null)
    {
        var known = KnownCodes(contents, settings, remembered);
        var links = new List<AirportLink>();
        foreach (var content in contents)
        {
            var found = new Dictionary<AirportCode, AirportLinkReason>();
            var excluded = IsExcluded(content, settings);
            var counted = !excluded && content.UsableAirports().Count > 0;

            if (!excluded)
            {
                foreach (var record in content.UsableAirports())
                {
                    Add(found, record.Code, AirportLinkReason.Record);
                }
            }

            if (content.Source == PackageSource.SimManaged)
            {
                if (!counted)
                {
                    foreach (Match match in OfficialAirportName.Matches(content.Name.Value))
                    {
                        if (AirportCode.TryParse(match.Groups[1].Value, out var code) && known.Contains(code))
                        {
                            Add(found, code, AirportLinkReason.PackageName);
                        }
                    }
                }
            }
            else
            {
                foreach (var candidate in TokenCandidates(content, settings, known).Where(c => c.Linked))
                {
                    Add(found, candidate.Code, AirportLinkReason.Token);
                }
            }

            if (overrides.Removed.TryGetValue(content.Name, out var removed))
            {
                foreach (var code in found.Keys.Where(removed.Contains).ToList())
                {
                    found.Remove(code);
                }
            }

            if (overrides.Manual.TryGetValue(content.Name, out var manual))
            {
                foreach (var code in manual)
                {
                    found[code] = AirportLinkReason.Manual;
                }
            }

            links.AddRange(found.Select(kv => new AirportLink(content, kv.Key, kv.Value)));
        }

        return links
            .OrderBy(l => l.Package.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.Package.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.Code.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>More distinct codes than the threshold, or a pattern match, unless the Package is opted in (C4).</summary>
    public static bool IsExcluded(PackageContent content, ContentSettings settings)
        => !settings.OptedIn.Contains(content.Name)
            && (content.UsableAirports().Select(a => a.Code).Distinct().Count() > settings.AirportThreshold || settings.MatchesExclusion(content.Name));

    public static bool IsExcludedByPattern(PackageContent content, ContentSettings settings)
        => !settings.OptedIn.Contains(content.Name) && settings.MatchesExclusion(content.Name);

    /// <summary>Every ICAO code in any record except those of Packages excluded by pattern (C4), and every remembered record's.</summary>
    public static IReadOnlySet<AirportCode> KnownCodes(IReadOnlyList<PackageContent> contents, ContentSettings settings, IReadOnlyList<AirportRecord>? remembered = null)
    {
        var known = new HashSet<AirportCode>();
        if (remembered is not null)
        {
            known.UnionWith(remembered.Select(r => r.Code));
        }

        foreach (var content in contents)
        {
            if (content.UsableAirports().Count > 0 && !IsExcludedByPattern(content, settings))
            {
                known.UnionWith(content.UsableAirports().Select(a => a.Code));
            }
        }

        return known;
    }

    public static IReadOnlyList<TokenCandidate> TokenCandidates(PackageContent content, ContentSettings settings, IReadOnlySet<AirportCode> known)
    {
        string? packageReason = null;
        if (content.Source == PackageSource.SimManaged)
        {
            packageReason = "sim-managed";
        }
        else if (CategoryRules.Provisional(content.Manifest.ContentType, content.HasBgl) != Category.Scenery)
        {
            packageReason = "not scenery";
        }
        else if (content.IsAirTraffic)
        {
            packageReason = "air traffic";
        }
        else if (!IsExcluded(content, settings) && content.UsableAirports().Count > 0)
        {
            packageReason = "counted record";
        }

        var result = new List<TokenCandidate>();
        Collect("name", content.Name.Value);
        Collect("title", content.Manifest.Title);
        return result;

        void Collect(string source, string? text)
        {
            var first = true;
            foreach (var token in Tokens(text))
            {
                var isFirst = first;
                first = false;
                if (!AirportCode.TryParse(token, out var code) || !known.Contains(code))
                {
                    continue;
                }

                var reason = packageReason
                    ?? (settings.IsStopWord(token) ? "stop word" : token.Length == MinCodeLength && !isFirst ? "not leading" : null);
                result.Add(new TokenCandidate(source, code, reason is null, reason));
            }
        }
    }

    private static IEnumerable<string> Tokens(string? text)
        => Regex.Matches(text ?? "", "[A-Za-z0-9]+").Select(m => m.Value);

    private static void Add(Dictionary<AirportCode, AirportLinkReason> found, AirportCode code, AirportLinkReason reason)
    {
        // Record, PackageName and Token are added in that order and the first wins; Manual overwrites afterwards.
        found.TryAdd(code, reason);
    }
}
