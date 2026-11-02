using AddonManager.Core.FirstRun;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Tests.FirstRun;

public class StartupRulesTests
{
    [Fact]
    public void No_libraries_and_no_snapshot_is_first_run()
        => Assert.True(StartupRules.IsFirstRun(new UserState()));

    [Fact]
    public void A_snapshot_without_libraries_is_not_first_run()
    {
        var state = new UserState { Snapshot = [] };

        Assert.False(StartupRules.IsFirstRun(state));
    }

    [Fact]
    public void A_library_without_snapshot_is_not_first_run()
    {
        var state = new UserState();
        state.Libraries.Add(new LibraryRoot(@"C:\Sim\Addons", LibraryRoot.AllSections));

        Assert.False(StartupRules.IsFirstRun(state));
    }

    [Fact]
    public void The_wall_waits_for_the_first_completed_scan()
    {
        var state = new UserState { ContentPassCompleted = false };

        Assert.False(StartupRules.ShowsWallAtDiscovery(state, 3));
    }

    [Fact]
    public void The_wall_waits_when_the_cache_is_empty()
    {
        var state = new UserState { ContentPassCompleted = true };

        Assert.False(StartupRules.ShowsWallAtDiscovery(state, 0));
    }

    [Fact]
    public void A_settled_state_with_a_cache_shows_the_wall_at_discovery()
    {
        var state = new UserState { ContentPassCompleted = true };

        Assert.True(StartupRules.ShowsWallAtDiscovery(state, 3));
    }
}
