using System.Collections.Concurrent;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Infrastructure.Tests.Fakes;

/// <summary>
/// A content reader over temp folders: a Package exists while its <c>manifest.json</c> does, and is identified by its folder name.
/// <see cref="Gate"/> holds every <see cref="ReadMarker"/> call until the test releases it, so a test can keep a run in progress.
/// </summary>
internal sealed class GatedContentReader : IPackageContentReader
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, int> hasBglCalls = new(StringComparer.OrdinalIgnoreCase);
    private Exception? markerFailure;
    private int markerCalls;
    private int readCalls;
    private int active;
    private int maxActive;

    /// <summary>Awaited (blocking) at the start of every <see cref="ReadMarker"/>; null means no gate.</summary>
    public SemaphoreSlim? Gate { get; set; }

    /// <summary>What <see cref="HasBgl"/> returns per folder name; a missing name returns null (no <c>layout.json</c>).</summary>
    public Dictionary<string, bool?> HasBglResult { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Thrown by <see cref="HasBgl"/> per folder name.</summary>
    public Dictionary<string, Exception> HasBglThrows { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The airport records <see cref="Read"/> returns per folder name.</summary>
    public Dictionary<string, AirportRecord[]> Airports { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int MarkerCalls => Volatile.Read(ref markerCalls);

    public int ReadCalls => Volatile.Read(ref readCalls);

    /// <summary>The most <see cref="ReadMarker"/> calls that were in progress at the same moment.</summary>
    public int MaxConcurrentMarkerCalls => Volatile.Read(ref maxActive);

    /// <summary>The next <see cref="ReadMarker"/> call throws this, once, after passing the gate.</summary>
    public void ThrowOnFirstMarker(Exception exception) => markerFailure = exception;

    public int HasBglCallsFor(string packagePathOrName) => hasBglCalls.GetValueOrDefault(NameOf(packagePathOrName));

    public ChangeMarker? ReadMarker(string packagePath)
    {
        Interlocked.Increment(ref markerCalls);
        var now = Interlocked.Increment(ref active);
        InterlockedMax(ref maxActive, now);
        try
        {
            if (Gate is { } gate && !gate.Wait(GateTimeout))
                throw new TimeoutException("The test never released the gate.");
            if (Interlocked.Exchange(ref markerFailure, null) is { } failure)
                throw failure;

            var manifest = new FileInfo(Path.Combine(packagePath, "manifest.json"));
            return manifest.Exists ? new ChangeMarker(manifest.LastWriteTimeUtc.Ticks, manifest.Length, null, null) : null;
        }
        finally
        {
            Interlocked.Decrement(ref active);
        }
    }

    public bool? HasBgl(string packagePath)
    {
        var name = NameOf(packagePath);
        hasBglCalls.AddOrUpdate(name, 1, (_, n) => n + 1);
        if (HasBglThrows.TryGetValue(name, out var failure))
            throw failure;
        return HasBglResult.GetValueOrDefault(name);
    }

    public PackageContent? Read(ScanTarget target, ChangeMarker marker)
    {
        Interlocked.Increment(ref readCalls);
        var name = NameOf(target.Path);
        var airports = Airports.TryGetValue(name, out var records) ? records : [];
        var hasBgl = HasBglResult.GetValueOrDefault(name) ?? true;
        return new PackageContent(
            target.Name, target.Path, target.Source, marker, target.Manifest,
            hasBgl, [], 0, airports, false, [], [], [], null);
    }

    private static string NameOf(string pathOrName)
        => pathOrName.Contains('\\') || pathOrName.Contains('/') ? Path.GetFileName(PathNormalizer.Normalize(pathOrName)) : pathOrName;

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}
