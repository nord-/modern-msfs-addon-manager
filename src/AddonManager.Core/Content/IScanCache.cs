namespace AddonManager.Core.Content;

/// <summary>Disposable store of scanned content, keyed by normalised Package path. Deleting it only costs a rescan.</summary>
public interface IScanCache
{
    IReadOnlyList<PackageContent> LoadAll();

    void Upsert(IReadOnlyList<PackageContent> contents);

    void Remove(IReadOnlyCollection<string> paths);
}
