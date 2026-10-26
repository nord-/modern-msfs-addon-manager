namespace AddonManager.Core.Packages;

/// <summary>A registered Library. Equality and hashing use the normalised path only, case-insensitively.</summary>
public sealed record LibraryRoot(string Path, bool ManagedElsewhere = false)
{
    private string Key => PathNormalizer.Normalize(Path);

    public bool Equals(LibraryRoot? other)
        => other is not null && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Key);
}
