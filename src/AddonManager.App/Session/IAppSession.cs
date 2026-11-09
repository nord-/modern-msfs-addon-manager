using AddonManager.Core.Community;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Sim2020;

namespace AddonManager.App.Session;

/// <summary>The window's view of the app session. <see cref="Changed"/> is raised on whatever thread finished the work, never under the session lock.</summary>
public interface IAppSession
{
    SessionView Current { get; }

    event Action<SessionView>? Changed;

    UserState State { get; }

    SimPaths? Paths { get; }

    string DatabaseFile { get; }

    /// <summary>Community as it is now, read once; empty before the install is known or when it cannot be read.</summary>
    IReadOnlyList<CommunityEntryInfo> ReadCommunity();

    bool TryUsePackagesRoot(string root, out string? refusal);

    Task CompleteFirstRunAsync(IReadOnlyList<LibraryRoot> libraries);

    Task RescanAsync(bool full);

    /// <summary>Whether MSFS runs right now; false before the install is known.</summary>
    bool IsSimRunning { get; }

    /// <summary>
    /// The card's switch: sets every switchable Package of the card to the opposite of the switch position, and the wall shows it at once.
    /// Returns the message to show, or <c>null</c> on success (also when the click changes nothing); nothing is thrown.
    /// </summary>
    Task<string?> SwitchCardAsync(CardRef card);

    /// <summary>Removes a Not found Package (and the card it leaves empty) from the app's data. Returns the message to show, or <c>null</c> on success.</summary>
    Task<string?> RemoveAsync(CardRef card, PackageKey package);
}
