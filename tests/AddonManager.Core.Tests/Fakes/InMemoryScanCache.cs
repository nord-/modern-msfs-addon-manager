using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.Fakes;

/// <summary>Keeps rows by normalised path, like the real cache, and records how the scanner wrote them.</summary>
internal sealed class InMemoryScanCache : IScanCache
{
    private readonly Dictionary<string, PackageContent> rows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The size of every <see cref="Upsert"/> call.</summary>
    public List<int> UpsertSizes { get; } = [];

    /// <summary>Thrown by every <see cref="Upsert"/> call, after the call is recorded.</summary>
    public Exception? UpsertFailure { get; set; }

    public IReadOnlyList<PackageContent> LoadAll() => [.. rows.Values];

    public void Upsert(IReadOnlyList<PackageContent> contents)
    {
        UpsertSizes.Add(contents.Count);
        if (UpsertFailure is not null)
            throw UpsertFailure;
        foreach (var c in contents)
            rows[PathNormalizer.Normalize(c.Path)] = c;
    }

    public void Remove(IReadOnlyCollection<string> paths)
    {
        foreach (var p in paths)
            rows.Remove(PathNormalizer.Normalize(p));
    }
}
