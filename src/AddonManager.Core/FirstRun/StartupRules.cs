using AddonManager.Core.State;

namespace AddonManager.Core.FirstRun;

/// <summary>What the app does at startup, decided from the stored state (decisions D3 and D13).</summary>
public static class StartupRules
{
    /// <summary>First run: no Library is registered and there is no Community snapshot yet.</summary>
    public static bool IsFirstRun(UserState state) => state.Libraries.Count == 0 && state.Snapshot is null;

    /// <summary>
    /// The wall is built from the cached contents at discovery only when a completed scan has been settled before and the cache is not
    /// empty; otherwise "Scanning…" stays until the run completes.
    /// </summary>
    public static bool ShowsWallAtDiscovery(UserState state, int cachedCount) => state.ContentPassCompleted && cachedCount > 0;
}
