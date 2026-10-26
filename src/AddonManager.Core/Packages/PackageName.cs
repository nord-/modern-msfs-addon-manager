namespace AddonManager.Core.Packages;

/// <summary>A Package's identity: its folder name, compared case-insensitively.</summary>
public readonly struct PackageName(string value) : IEquatable<PackageName>
{
    public string Value { get; } = value;

    public bool Equals(PackageName other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is PackageName other && Equals(other);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? "");

    public override string ToString() => Value;

    public static bool operator ==(PackageName left, PackageName right) => left.Equals(right);

    public static bool operator !=(PackageName left, PackageName right) => !left.Equals(right);
}
