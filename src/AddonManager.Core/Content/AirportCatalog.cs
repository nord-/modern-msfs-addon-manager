using AddonManager.Core.Packages;

namespace AddonManager.Core.Content;

public sealed record Airport(AirportCode Code, string? Name, GeoPoint? Position, bool HasRealScenery, bool HasBuiltIn);

public sealed record BuiltInReplacement(PackageContent Package, AirportCode Code);

/// <summary>Builds the Airports and finds Packages that replace built-in scenery (spec 6.5, content rules C4 and C7).</summary>
public static class AirportCatalog
{
    private static readonly PairEquality PairComparer = new();

    public static IReadOnlyDictionary<AirportCode, Airport> Build(
        IReadOnlyList<PackageContent> contents,
        IReadOnlyList<AirportLink> links,
        ContentSettings settings,
        IReadOnlyList<AirportRecord>? remembered = null)
    {
        var real = RealLinks(links, settings);
        var linkedPairs = links.Select(l => (l.Package.Path, l.Code)).ToHashSet(PairComparer);

        var records = new List<(AirportRecord Record, PackageContent Package, bool Linked)>();
        foreach (var content in contents)
        {
            if (AirportLinker.IsExcludedByPattern(content, settings))
            {
                continue;
            }

            foreach (var record in content.UsableAirports())
            {
                records.Add((record, content, linkedPairs.Contains((content.Path, record.Code))));
            }
        }

        // One sort plus one lookup; no per-code scans.
        var byCode = records
            .OrderBy(r => r.Linked ? 0 : 1)
            .ThenBy(r => r.Package.Source == PackageSource.SimManaged ? 0 : 1)
            .ThenBy(r => r.Package.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Package.Path, StringComparer.OrdinalIgnoreCase)
            .ToLookup(r => r.Record.Code);

        var codes = new HashSet<AirportCode>(AirportLinker.KnownCodes(contents, settings, remembered));
        codes.UnionWith(links.Select(l => l.Code));

        // A remembered record speaks only for what no content record gives: name and position each fall back to it on their own (spec 11).
        var rememberedByCode = (remembered ?? []).ToLookup(r => r.Code);

        var result = new Dictionary<AirportCode, Airport>();
        foreach (var code in codes)
        {
            string? name = null;
            GeoPoint? position = null;
            foreach (var record in byCode[code].Select(r => r.Record).Concat(rememberedByCode[code]))
            {
                name ??= IsUsableName(record.Name) ? record.Name : null;
                position ??= record.Position;
                if (name is not null && position is not null)
                {
                    break;
                }
            }

            var codeLinks = real[code].ToList();
            result[code] = new Airport(
                code,
                name,
                position,
                codeLinks.Count > 0,
                codeLinks.Any(l => l.Package.Source == PackageSource.SimManaged));
        }

        return result;
    }

    public static IReadOnlyList<BuiltInReplacement> ReplacesBuiltIn(
        IReadOnlyList<PackageContent> contents,
        IReadOnlyList<AirportLink> links,
        IReadOnlyDictionary<AirportCode, Airport> airports,
        ContentSettings settings)
    {
        var realLinks = RealLinks(links, settings).SelectMany(g => g).ToList();
        var found = new Dictionary<(string Path, AirportCode Code), PackageContent>(PairComparer);

        foreach (var link in realLinks)
        {
            if (link.Package.Source != PackageSource.SimManaged && airports.TryGetValue(link.Code, out var airport) && airport.HasBuiltIn)
            {
                found[(link.Package.Path, link.Code)] = link.Package;
            }
        }

        var simCodes = realLinks
            .Where(l => l.Package.Source == PackageSource.SimManaged)
            .ToLookup(l => l.Package.Name.Value, l => l.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var content in contents)
        {
            if (content.Source == PackageSource.SimManaged || AirportLinker.IsExcluded(content, settings))
            {
                continue;
            }

            foreach (var dependency in content.Manifest.Dependencies)
            {
                foreach (var code in simCodes[dependency])
                {
                    found[(content.Path, code)] = content;
                }
            }
        }

        return found
            .Select(kv => new BuiltInReplacement(kv.Value, kv.Key.Code))
            .OrderBy(r => r.Package.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Package.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Code.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Links from Packages that aren't excluded (C4), plus Manual links.</summary>
    public static ILookup<AirportCode, AirportLink> RealLinks(IReadOnlyList<AirportLink> links, ContentSettings settings)
        => links
            .Where(l => l.Reason == AirportLinkReason.Manual || !AirportLinker.IsExcluded(l.Package, settings))
            .ToLookup(l => l.Code);

    private static bool IsUsableName(string? name)
        => !string.IsNullOrWhiteSpace(name) && !name.StartsWith("TT:", StringComparison.OrdinalIgnoreCase);

    private sealed class PairEquality : IEqualityComparer<(string Path, AirportCode Code)>
    {
        public bool Equals((string Path, AirportCode Code) x, (string Path, AirportCode Code) y)
            => x.Code.Equals(y.Code) && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Path, AirportCode Code) p)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(p.Path), p.Code);
    }
}
