using AddonManager.Core.Packages;

namespace AddonManager.Core.State;

/// <summary>Everything the link engine stores for the user (spec 11).</summary>
public sealed class UserState
{
    public List<LibraryRoot> Libraries { get; set; } = [];

    /// <summary>Choices per Package, kept also while a Package can't be found (spec 6.1).</summary>
    public Dictionary<PackageName, PackageState> Packages { get; set; } = [];

    /// <summary>The last accepted Community snapshot; <c>null</c> means never reconciled (first run).</summary>
    public List<SnapshotEntry>? Snapshot { get; set; }

    /// <summary>Packages whose link changes wait for the sim to exit (spec 6.8).</summary>
    public HashSet<PackageName> Pending { get; set; } = [];

    public bool IsOn(PackageName name) => Packages.TryGetValue(name, out var s) && s.On;

    /// <summary>A per-Package override wins; otherwise the active copy's Library decides (spec 6.3).</summary>
    public bool IsLocked(PackageCopy activeCopy)
        => Packages.GetValueOrDefault(activeCopy.Name)?.ManagedElsewhere ?? activeCopy.Library.ManagedElsewhere;

    /// <summary>Stores a choice, keeping the other fields; <paramref name="activeCopyPath"/> replaces the stored copy when given.</summary>
    public void SetOn(PackageName name, bool on, string? activeCopyPath = null)
    {
        var current = Packages.GetValueOrDefault(name) ?? new PackageState(false);
        Packages[name] = current with { On = on, ActiveCopyPath = activeCopyPath ?? current.ActiveCopyPath };
    }

    public void Forget(PackageName name)
    {
        Packages.Remove(name);
        Pending.Remove(name);
    }
}
