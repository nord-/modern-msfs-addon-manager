using AddonManager.Core.Linking;

namespace AddonManager.Core.Tests.Fakes;

internal sealed class FakeSim : ISimProcessMonitor
{
    public bool IsRunning { get; set; }
}
