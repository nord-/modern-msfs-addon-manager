using AddonManager.Core.Cards;
using AddonManager.Core.Packages;

namespace AddonManager.App.ViewModels;

/// <summary>A dialog row that may offer "Remove": a Package row, or a GSX profile row. <c>Key</c> is null for rows that are not the card's own Packages.</summary>
public interface IRemovableRow
{
    PackageKey? Key { get; }

    string FolderName { get; }

    bool CanRemove { get; }
}

/// <summary>One Package row of the card dialog.</summary>
public sealed class PackageRowViewModel(CardPackage package) : IRemovableRow
{
    public PackageKey Key { get; } = new(package.Kind, package.Name);

    PackageKey? IRemovableRow.Key => Key;

    public string DisplayName { get; } = package.DisplayName;

    public string FolderName { get; } = package.Name.Value;

    public string Path { get; } = package.Path;

    public string? Version { get; } = package.Version;

    /// <summary>The path of the Library that holds the Package, or <c>null</c>.</summary>
    public string? Library { get; } = package.Library?.Path;

    /// <summary>The copy the Package is active from, shown only when the Package has more than one copy.</summary>
    public string? ActiveCopy { get; } = package.Copies.Count > 1 ? package.Path : null;

    /// <summary>Why the Package cannot be switched, or <c>null</c>.</summary>
    public string? Reason { get; } = package.DisabledReason;

    public bool CanRemove { get; } = package.Removable;

    /// <summary>A locked Package (spec 6.3) is dimmed, besides its reason.</summary>
    public bool Dimmed { get; } = package.Lock != LockReason.None;
}
