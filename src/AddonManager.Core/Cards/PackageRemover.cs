using AddonManager.Core.Community;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Cards;

/// <summary>
/// Removes a Package from the app's data (spec 6.5 "Remove"): only a Package the board shows as Not found, or one no registered Library
/// holds. Nothing in a Library or Official is touched; a Broken Community link of the same name goes through <see cref="ChangeApplier"/>.
/// </summary>
public sealed class PackageRemover(ChangeApplier applier, ICommunitySource community, ISimProcessMonitor sim, IUserStateStore store)
{
    /// <summary>
    /// Forgets everything stored for the Package and the card it leaves empty, and saves. A Broken link of a folder Package is removed
    /// first; when that fails the Package stays. Throws <see cref="InvalidOperationException"/> when the board does not offer Remove for
    /// it, or while the sim runs (Community is not changed then). <paramref name="board"/> must be built from the current state.
    /// </summary>
    public void Remove(UserState state, Board board, PackageIndex index, PackageKey package)
    {
        // One scope around the link removal and the forgetting: they commit together, and a locked file refuses both.
        using var change = store.BeginChange();
        var shown = board.CardOf(package)?.Packages.FirstOrDefault(p => p.Kind == package.Kind && p.Name == package.Name);
        if (shown is not { Removable: true })
        {
            throw new InvalidOperationException("Only a Package that is Not found or held by no registered Library can be removed.");
        }

        if (sim.IsRunning)
        {
            throw new InvalidOperationException("Remove is unavailable while the sim runs.");
        }

        // Only a folder Package has an entry in Community.
        if (package.Kind == PackageKind.Folder && CommunityClassifier.Classify(community.Read(), index)
                .Any(e => e.Name == package.Name && e.Class == EntryClass.Broken))
        {
            if (applier.RemoveBrokenLink(state, index, package.Name.Value).Deferred)
            {
                throw new InvalidOperationException("Remove is unavailable while the sim runs.");
            }
        }

        state.Forget(package);
        store.Save(state);
    }
}
