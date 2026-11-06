using AddonManager.Core.Linking;

namespace AddonManager.Core.Tests.Fakes;

internal sealed class FakeSim : ISimProcessMonitor
{
    private bool running;
    private int reads;

    /// <summary>Set to make the sim report running from the n-th read on (a sim that starts between two checks).</summary>
    public int? RunningFromRead { get; set; }

    public bool IsRunning
    {
        get
        {
            reads++;
            return running || (RunningFromRead is { } from && reads >= from);
        }
        set => running = value;
    }
}
