namespace AddonManager.Core.Content;

/// <summary>An <c>aircraft.cfg</c> in a Package. <c>Container</c> is the folder holding it; <c>BaseContainer</c> is the last segment of <c>base_container</c>, or null. <c>TypeDesignator</c> is <c>icao_type_designator</c> from the <c>[GENERAL]</c> section, or null.</summary>
public sealed record AircraftConfig(string Container, string? BaseContainer, string? TypeDesignator = null);
