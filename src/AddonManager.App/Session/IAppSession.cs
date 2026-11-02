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
}
