namespace AddonManager.Core.Packages;

/// <summary>What a Package is: a folder in a Library or Community, or a GSX profile file.</summary>
public enum PackageKind
{
    Folder,
    GsxProfile,
}

/// <summary>A Package's identity across kinds: its kind and its name, the name compared case-insensitively (rule R1).</summary>
public readonly record struct PackageKey(PackageKind Kind, PackageName Name)
{
    public static PackageKey Folder(PackageName name) => new(PackageKind.Folder, name);

    public override string ToString() => Kind == PackageKind.Folder ? Name.Value : $"{Kind}:{Name.Value}";
}
