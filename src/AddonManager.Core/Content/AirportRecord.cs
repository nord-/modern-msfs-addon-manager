namespace AddonManager.Core.Content;

/// <summary>An airport record read from a Package's BGL files. <c>Name</c> is null when the record has no readable name.</summary>
public sealed record AirportRecord(AirportCode Code, string? Name, GeoPoint? Position);
