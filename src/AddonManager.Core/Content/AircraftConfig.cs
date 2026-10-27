namespace AddonManager.Core.Content;

/// <summary>An <c>aircraft.cfg</c> in a Package. <c>Container</c> is the folder holding it; <c>BaseContainer</c> is the last segment of <c>base_container</c>, or null.</summary>
public sealed record AircraftConfig(string Container, string? BaseContainer);
