using AddonManager.Core.Cards;
using AddonManager.Core.Packages;

namespace AddonManager.App.ViewModels;

/// <summary>One Package row of the card dialog.</summary>
public sealed class PackageRowViewModel(CardPackage package)
{
    public PackageKey Key { get; } = new(package.Kind, package.Name);

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
}
