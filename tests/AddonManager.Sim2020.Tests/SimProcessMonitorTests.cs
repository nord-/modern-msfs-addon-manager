namespace AddonManager.Sim2020.Tests;

public class SimProcessMonitorTests
{
    [Fact]
    public void Monitor_asks_for_FlightSimulator()
    {
        string? asked = null;

        var running = new SimProcessMonitor(n => { asked = n; return true; }).IsRunning;

        Assert.True(running);
        Assert.Equal("FlightSimulator", asked);
    }

    [Fact]
    public void Default_check_runs_without_throwing() => _ = new SimProcessMonitor().IsRunning;
}
