using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.Fakes;

/// <summary>
/// Reads nothing from disk. Packages are identified by the last segment of their path. Every package starts at marker version 0;
/// <see cref="Touch"/> raises it. A read stamps the content's <c>ThumbnailPath</c> with "read-N" so a test can tell a fresh read from a reused row.
/// </summary>
internal sealed class FakeContentReader : IPackageContentReader
{
    private readonly Dictionary<string, long> versions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> vanished = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> vanishOnRead = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> readFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> markerFailures = new(StringComparer.OrdinalIgnoreCase);
    private int reads;

    /// <summary>The <c>UnreadableBglCount</c> of every package that is read from now on.</summary>
    public int UnreadableBgl { get; set; }

    /// <summary>Package names in the order <see cref="Read"/> was called.</summary>
    public List<string> ReadOrder { get; } = [];

    /// <summary>Runs at the start of every <see cref="Read"/>, with the number of reads so far including this one.</summary>
    public Action<int>? OnRead { get; set; }

    public void Touch(string name) => versions[name] = versions.GetValueOrDefault(name) + 1;

    /// <summary>The package is gone: its marker is null.</summary>
    public void Vanish(string name) => vanished.Add(name);

    /// <summary>The marker is there but the folder is gone by the time it is read.</summary>
    public void VanishOnRead(string name) => vanishOnRead.Add(name);

    public void ThrowOn(string name, Exception exception) => readFailures[name] = exception;

    public void ThrowOnMarker(string name, Exception exception) => markerFailures[name] = exception;

    public ChangeMarker? ReadMarker(string packagePath)
    {
        var name = NameOf(packagePath);
        if (markerFailures.TryGetValue(name, out var failure))
            throw failure;
        return vanished.Contains(name) ? null : new ChangeMarker(versions.GetValueOrDefault(name), 0, null, null);
    }

    public bool? HasBgl(string packagePath) => null;

    public PackageContent? Read(ScanTarget target, ChangeMarker marker)
    {
        var name = NameOf(target.Path);
        reads++;
        ReadOrder.Add(name);
        OnRead?.Invoke(reads);
        if (readFailures.TryGetValue(name, out var failure))
            throw failure;
        if (vanishOnRead.Contains(name))
            return null;
        return new PackageContent(
            target.Name, target.Path, target.Source, marker, target.Manifest,
            false, [], UnreadableBgl, [], false, [], [], [], $"read-{reads}");
    }

    private static string NameOf(string path) => Path.GetFileName(PathNormalizer.Normalize(path));
}
