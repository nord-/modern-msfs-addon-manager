using AddonManager.Core.Packages;

namespace AddonManager.Core.Content;

/// <summary>How a Package relates to the aircraft containers it provides or extends.</summary>
public enum AircraftRole
{
    None,
    Provider,
    Mod,
    Livery,
    Attached,
    AirTraffic,
    LockedBase,
}

/// <summary>Everything that belongs to one aircraft container. <c>Providers</c> are Library Packages, plus any copy that shares a provider's name (Mods when <c>LockedBases</c> is not empty).</summary>
public sealed record ContainerGroup(
    string Container,
    IReadOnlyList<PackageContent> Providers,
    IReadOnlyList<PackageContent> LockedBases,
    IReadOnlyList<PackageContent> Liveries,
    IReadOnlyList<PackageContent> Attached);

/// <summary>Groups aircraft Packages by container (spec 6.6). Computed in two passes, providers first, so scan order never decides.</summary>
public sealed class AircraftIndex
{
    private static readonly IComparer<PackageContent> PackageOrder = Comparer<PackageContent>.Create(static (a, b) =>
    {
        var byName = StringComparer.OrdinalIgnoreCase.Compare(a.Name.Value, b.Name.Value);
        return byName != 0 ? byName : StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path);
    });

    private readonly Dictionary<PackageContent, AircraftRole> roles = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PackageContent, List<string>> containers = new(ReferenceEqualityComparer.Instance);

    private AircraftIndex(IReadOnlyDictionary<string, ContainerGroup> groups) => Groups = groups;

    public IReadOnlyDictionary<string, ContainerGroup> Groups { get; }

    public AircraftRole RoleOf(PackageContent content) => roles.GetValueOrDefault(content);

    /// <summary>The Container of every group the Package is in, sorted case-insensitively.</summary>
    public IReadOnlyList<string> ContainersOf(PackageContent content) => containers.TryGetValue(content, out var list) ? list : [];

    public static AircraftIndex Build(IReadOnlyList<PackageContent> contents)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var providers = new Dictionary<string, List<(PackageContent Package, string Spelling)>>(comparer);
        var liveries = new Dictionary<string, List<(PackageContent Package, string Spelling)>>(comparer);
        var attachable = new List<PackageContent>();
        var roles = new Dictionary<PackageContent, AircraftRole>(ReferenceEqualityComparer.Instance);

        // Pass 1: providers and liveries.
        foreach (var content in contents.Where(IsParticipant))
        {
            if (content.IsAirTraffic)
            {
                roles[content] = AircraftRole.AirTraffic;
                continue;
            }

            var provided = ProvidedContainers(content);
            foreach (var spelling in provided)
            {
                Add(providers, spelling, content, spelling);
            }

            if (provided.Count > 0)
            {
                continue;
            }

            if (content.AircraftConfigs.Count > 0)
            {
                if (content.AircraftConfigs.All(c => c.BaseContainer is not null))
                {
                    // A base that is one of the Package's own containers is a chained variant, not another aircraft.
                    var own = content.AircraftConfigs.Select(c => c.Container).ToHashSet(comparer);
                    foreach (var baseContainer in content.AircraftConfigs.Select(c => c.BaseContainer!).Distinct(comparer).Where(b => !own.Contains(b)))
                    {
                        Add(liveries, baseContainer, content, baseContainer);
                    }
                }
            }
            else if (CategoryRules.IsAircraftType(content.Manifest.ContentType))
            {
                attachable.Add(content);
            }
        }

        // Pass 2: attached packages join groups that pass 1 created.
        var attached = new Dictionary<string, List<PackageContent>>(comparer);
        foreach (var content in attachable)
        {
            var keys = content.AirplaneFolders.Where(f => providers.ContainsKey(f) || liveries.ContainsKey(f)).Distinct(comparer).ToList();
            if (keys.Count == 0)
            {
                var dependencies = new HashSet<string>(content.Manifest.Dependencies, comparer);
                keys = providers.Where(g => g.Value.Any(p => dependencies.Contains(p.Package.Name.Value))).Select(g => g.Key).ToList();
            }

            foreach (var key in keys)
            {
                if (!attached.TryGetValue(key, out var list))
                {
                    attached[key] = list = [];
                }

                list.Add(content);
            }
        }

        var groups = new Dictionary<string, ContainerGroup>(comparer);
        var memberContainers = new Dictionary<PackageContent, List<string>>(ReferenceEqualityComparer.Instance);
        foreach (var key in providers.Keys.Concat(liveries.Keys).Distinct(comparer))
        {
            var provs = Sorted(providers, key);
            // Community copies with a Library provider's name are the same Package (C5, C8), never a locked base of it; a Sim-managed provider always is one.
            var libraryNames = provs.Where(p => p.Package.Source == PackageSource.Library).Select(p => p.Package.Name.Value).ToHashSet(comparer);
            var libraryProviders = provs.Where(p => p.Package.Source == PackageSource.Library
                || (p.Package.Source == PackageSource.Community && libraryNames.Contains(p.Package.Name.Value))).ToList();
            var lockedBases = provs.Except(libraryProviders).ToList();
            var groupLiveries = Sorted(liveries, key);
            var spelling = libraryProviders.Concat(lockedBases).Concat(groupLiveries).First().Spelling;
            var group = new ContainerGroup(
                spelling,
                libraryProviders.Select(p => p.Package).ToList(),
                lockedBases.Select(p => p.Package).ToList(),
                groupLiveries.Select(p => p.Package).ToList(),
                attached.TryGetValue(key, out var a) ? a.OrderBy(p => p, PackageOrder).ToList() : []);
            groups[spelling] = group;

            var providerRole = lockedBases.Count > 0 ? AircraftRole.Mod : AircraftRole.Provider;
            Note(group.Providers, providerRole);
            Note(group.LockedBases, AircraftRole.LockedBase);
            Note(group.Liveries, AircraftRole.Livery);
            Note(group.Attached, AircraftRole.Attached);

            void Note(IEnumerable<PackageContent> packages, AircraftRole role)
            {
                foreach (var package in packages)
                {
                    roles[package] = roles.TryGetValue(package, out var current) ? Stronger(current, role) : role;
                    if (!memberContainers.TryGetValue(package, out var list))
                    {
                        memberContainers[package] = list = [];
                    }

                    if (!list.Contains(spelling, comparer))
                    {
                        list.Add(spelling);
                    }
                }
            }
        }

        var index = new AircraftIndex(groups);
        foreach (var (package, role) in roles)
        {
            index.roles[package] = role;
        }

        foreach (var (package, list) in memberContainers)
        {
            index.containers[package] = list.Order(comparer).ToList();
        }

        return index;
    }

    private static bool IsParticipant(PackageContent c) => CategoryRules.IsAircraftType(c.Manifest.ContentType) || c.Source == PackageSource.SimManaged;

    /// <summary>Precedence for <c>RoleOf</c>: the lowest number wins.</summary>
    private static int Rank(AircraftRole role) => role switch
    {
        AircraftRole.AirTraffic => 0,
        AircraftRole.Mod => 1,
        AircraftRole.Provider => 2,
        AircraftRole.LockedBase => 3,
        AircraftRole.Livery => 4,
        AircraftRole.Attached => 5,
        _ => 6,
    };

    private static AircraftRole Stronger(AircraftRole a, AircraftRole b) => Rank(a) <= Rank(b) ? a : b;

    private static void Add(Dictionary<string, List<(PackageContent Package, string Spelling)>> map, string key, PackageContent package, string spelling)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        if (!list.Any(e => ReferenceEquals(e.Package, package)))
        {
            list.Add((package, spelling));
        }
    }

    private static List<(PackageContent Package, string Spelling)> Sorted(Dictionary<string, List<(PackageContent Package, string Spelling)>> map, string key)
        => map.TryGetValue(key, out var list) ? list.OrderBy(e => e.Package, PackageOrder).ToList() : [];

    /// <summary>The containers a Package provides, one spelling each, in ordinal order.</summary>
    private static List<string> ProvidedContainers(PackageContent content)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var provided = new HashSet<string>(comparer);
        var cfgs = content.AircraftConfigs;
        foreach (var cfg in cfgs.Where(c => c.BaseContainer is null))
        {
            provided.Add(cfg.Container);
        }

        foreach (var folder in content.EncryptedAirplaneFolders)
        {
            provided.Add(folder);
        }

        if (content.Source == PackageSource.SimManaged)
        {
            var withCfg = new HashSet<string>(cfgs.Select(c => c.Container), comparer);
            foreach (var folder in content.AirplaneFolders.Where(f => !withCfg.Contains(f)))
            {
                provided.Add(folder);
            }
        }

        // A variant whose base is provided by the same Package (including its encrypted or cfg-less folders) is part of that Package's own aircraft.
        bool grew;
        do
        {
            grew = false;
            foreach (var cfg in cfgs.Where(c => c.BaseContainer is not null && provided.Contains(c.BaseContainer)))
            {
                grew |= provided.Add(cfg.Container);
            }
        }
        while (grew);

        return provided.Order(StringComparer.Ordinal).ToList();
    }
}
