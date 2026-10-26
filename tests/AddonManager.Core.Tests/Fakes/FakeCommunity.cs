using AddonManager.Core.Community;

namespace AddonManager.Core.Tests.Fakes;

/// <summary>An in-memory Community folder.</summary>
internal sealed class FakeCommunity : ICommunitySource
{
    private readonly Dictionary<string, CommunityEntryInfo> entries = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CommunityEntryInfo> Read() => entries.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public bool Has(string name) => entries.ContainsKey(name);

    public CommunityEntryInfo? Get(string name) => entries.GetValueOrDefault(name);

    public string? TargetOf(string name) => Get(name)?.Target;

    public void AddJunction(string name, string target, bool targetExists = true)
        => entries[name] = new CommunityEntryInfo(name, EntryKind.Junction, target, targetExists, targetExists);

    public void AddDirectory(string name, bool hasManifest)
        => entries[name] = new CommunityEntryInfo(name, EntryKind.Directory, null, false, hasManifest);

    public void AddFile(string name) => entries[name] = new CommunityEntryInfo(name, EntryKind.File, null, false, false);

    public void Remove(string name) => entries.Remove(name);
}
