using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure.Tests.Fakes;

/// <summary>Keeps rows by normalised path, like the real cache. Safe to call from a scan thread while a test reads it.</summary>
internal sealed class InMemoryScanCache : IScanCache
{
    private readonly object gate = new();
    private readonly Dictionary<string, PackageContent> rows = new(StringComparer.OrdinalIgnoreCase);
    private int loads;

    /// <summary>How many times <see cref="LoadAll"/> was called.</summary>
    public int LoadCount => Volatile.Read(ref loads);

    public IReadOnlyList<PackageContent> LoadAll()
    {
        Interlocked.Increment(ref loads);
        lock (gate)
            return [.. rows.Values];
    }

    public void Upsert(IReadOnlyList<PackageContent> contents)
    {
        lock (gate)
        {
            foreach (var c in contents)
                rows[PathNormalizer.Normalize(c.Path)] = c;
        }
    }

    public void Remove(IReadOnlyCollection<string> paths)
    {
        lock (gate)
        {
            foreach (var p in paths)
                rows.Remove(PathNormalizer.Normalize(p));
        }
    }
}
