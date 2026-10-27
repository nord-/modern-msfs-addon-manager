using AddonManager.Core.Packages;

namespace AddonManager.Core.Content;

public sealed record ScanProgress(int Done, int Total, string? Current);

public sealed record ContentScanResult(IReadOnlyList<PackageContent> Contents, int Read, int Reused, int Failed);

/// <summary>Scans Packages incrementally: a Package whose change marker is unchanged keeps its cached content (spec 7.3, content rule C2).</summary>
public sealed class ContentScanner(IPackageContentReader reader, IScanCache cache)
{
    private const int BatchSize = 50;

    /// <param name="targets">Must have distinct normalised paths.</param>
    /// <param name="cached">What the caller loaded with <c>cache.LoadAll()</c>; the scanner only writes to the cache.</param>
    /// <param name="keepUnder">Roots that are unreachable right now (offline Libraries): cached content under them is kept and returned.</param>
    public ContentScanResult Scan(
        IReadOnlyList<ScanTarget> targets,
        IReadOnlyList<PackageContent> cached,
        IReadOnlyList<string> keepUnder,
        bool full,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var cachedByPath = new Dictionary<string, PackageContent>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cached)
            cachedByPath[PathNormalizer.Normalize(c.Path)] = c;

        var contents = new List<PackageContent>();
        var batch = new List<PackageContent>();
        var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int read = 0, reused = 0, failed = 0;

        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = targets[i];
                var path = PathNormalizer.Normalize(target.Path);
                targetPaths.Add(path);
                cachedByPath.TryGetValue(path, out var old);

                var (kind, content) = ScanOne(target, path, old, full);
                switch (kind)
                {
                    case Outcome.Reused:
                        reused++;
                        contents.Add(content!);
                        // Startup analyses the cached rows, so adopted facts are stored; unchanged rows are not rewritten.
                        if (!SameAdoptedFacts(old!, content!))
                        {
                            batch.Add(content!);
                            if (batch.Count >= BatchSize)
                                Flush(batch);
                        }
                        break;
                    case Outcome.Read:
                        read++;
                        contents.Add(content!);
                        batch.Add(content!);
                        if (batch.Count >= BatchSize)
                            Flush(batch);
                        break;
                    case Outcome.Vanished:
                        failed++;
                        if (old is not null)
                            cache.Remove([old.Path]);
                        break;
                    case Outcome.Unreadable:
                        failed++;
                        if (old is not null)
                            contents.Add(Adopt(old, target, path));
                        break;
                }

                progress?.Report(new ScanProgress(i + 1, targets.Count, target.Name.Value));
            }
        }
        finally
        {
            // Also runs on cancellation, so the work done so far survives.
            Flush(batch);
        }

        var roots = keepUnder.Select(r => PathNormalizer.Normalize(r)).ToList();
        var stale = new List<string>();
        foreach (var (path, row) in cachedByPath)
        {
            if (targetPaths.Contains(path))
                continue;
            if (roots.Any(r => PathNormalizer.IsUnder(path, r)))
                contents.Add(row with { Path = path });
            else
                stale.Add(row.Path);
        }

        if (stale.Count > 0)
            cache.Remove(stale);

        return new ContentScanResult(contents, read, reused, failed);
    }

    private (Outcome Kind, PackageContent? Content) ScanOne(ScanTarget target, string path, PackageContent? old, bool full)
    {
        try
        {
            var marker = reader.ReadMarker(target.Path);
            if (marker is null)
                return (Outcome.Vanished, null);
            // A row with unreadable BGLs may have been read while the Package was still being written (a missing file counts as unreadable)
            // and the marker only covers the manifest and layout, so such a row is read again.
            if (!full && old is not null && old.Marker == marker && old.UnreadableBglCount == 0)
                return (Outcome.Reused, Adopt(old, target, path));

            var content = reader.Read(target, marker);
            if (content is null)
                return (Outcome.Vanished, null);
            return (Outcome.Read, content.Path == path ? content : content with { Path = path });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (Outcome.Unreadable, null);
        }
    }

    // The source, name and manifest can change without the marker, e.g. when a Library is registered or removed.
    private static PackageContent Adopt(PackageContent old, ScanTarget target, string path)
        => old with { Name = target.Name, Path = path, Source = target.Source, Manifest = target.Manifest };

    // Record equality would compare Dependencies by reference and rewrite every reused row.
    private static bool SameAdoptedFacts(PackageContent old, PackageContent adopted)
        => string.Equals(old.Name.Value, adopted.Name.Value, StringComparison.Ordinal)
            && old.Source == adopted.Source
            && SameManifest(old.Manifest, adopted.Manifest);

    private static bool SameManifest(Manifest a, Manifest b)
        => a.Title == b.Title
            && a.Creator == b.Creator
            && a.ContentType == b.ContentType
            && a.PackageVersion == b.PackageVersion
            && a.Dependencies.SequenceEqual(b.Dependencies, StringComparer.Ordinal);

    private void Flush(List<PackageContent> batch)
    {
        if (batch.Count == 0)
            return;
        // Clear first: a failed Upsert must not be retried by the finally block, which would hide the original exception.
        var rows = batch.ToArray();
        batch.Clear();
        cache.Upsert(rows);
    }

    private enum Outcome { Reused, Read, Vanished, Unreadable }
}
