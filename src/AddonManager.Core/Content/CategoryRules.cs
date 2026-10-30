namespace AddonManager.Core.Content;

/// <summary>Derives a Package's <see cref="Category"/> from its <c>content_type</c> and what its content holds.</summary>
public static class CategoryRules
{
    /// <summary>The category for discovery, before content is read. A <c>null</c> <paramref name="hasBgl"/> means unknown and keeps Scenery.</summary>
    public static Category Provisional(string? contentType, bool? hasBgl)
    {
        if (IsType(contentType, "SCENERY"))
        {
            return hasBgl == false ? Category.Other : Category.Scenery;
        }

        return IsAircraftType(contentType) || IsInstruments(contentType) ? Category.Aircraft : Category.Other;
    }

    /// <summary>The category after the content pass. Air traffic is always Other.</summary>
    public static Category Final(string? contentType, bool hasBgl, bool isAirTraffic)
        => isAirTraffic ? Category.Other : Provisional(contentType, hasBgl);

    /// <summary>True for <c>INSTRUMENTS</c>: shown under Aircraft, but never part of Livery or attachment matching.</summary>
    public static bool IsInstruments(string? contentType) => IsType(contentType, "INSTRUMENTS");

    /// <summary>True for <c>AIRCRAFT</c> and <c>LIVERY</c>.</summary>
    public static bool IsAircraftType(string? contentType) => IsType(contentType, "AIRCRAFT") || IsType(contentType, "LIVERY");

    private static bool IsType(string? contentType, string expected) => string.Equals(contentType?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
