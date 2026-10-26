using System.Diagnostics;
using AddonManager.Core.Linking;

namespace AddonManager.Sim2020;

/// <summary>Detects a running MSFS 2020 (<c>FlightSimulator.exe</c>).</summary>
/// <param name="isProcessRunning">Test seam; defaults to <see cref="Process.GetProcessesByName(string)"/>.</param>
public sealed class SimProcessMonitor(Func<string, bool>? isProcessRunning = null) : ISimProcessMonitor
{
    private const string ProcessName = "FlightSimulator";

    private readonly Func<string, bool> isProcessRunning = isProcessRunning ?? IsAnyRunning;

    public bool IsRunning => isProcessRunning(ProcessName);

    private static bool IsAnyRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var p in processes)
            p.Dispose();
        return processes.Length > 0;
    }
}
