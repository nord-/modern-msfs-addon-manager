namespace AddonManager.Core.Content;

/// <summary>The Packages the sim manages itself (Official folder); their layout is sim-specific.</summary>
public interface ISimManagedPackages
{
    /// <summary>The Package folders, sorted by name.</summary>
    IReadOnlyList<string> PackageFolders();
}
