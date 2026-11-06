namespace AddonManager.Core.Linking;

/// <summary>Thrown by every change to Community while the sim runs, before anything is changed or stored.</summary>
public sealed class SimRunningException() : InvalidOperationException(Text)
{
    public const string Text = "Close MSFS to change add-ons";
}
