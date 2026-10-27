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

        return IsAircraftType(contentType) ? Category.Aircraft : Category.Other;
    }

    /// <summary>The category after the content pass. Air traffic is always Other; aircraft that are no aircraft member are Other.</summary>
    public static Category Final(string? contentType, bool hasBgl, bool isAirTraffic, bool isAircraftMember)
    {
        if (isAirTraffic)
        {
            return Category.Other;
        }

        var category = Provisional(contentType, hasBgl);
        return category == Category.Aircraft && !isAircraftMember ? Category.Other : category;
    }

    /// <summary>True for <c>AIRCRAFT</c> and <c>LIVERY</c>.</summary>
    public static bool IsAircraftType(string? contentType) => IsType(contentType, "AIRCRAFT") || IsType(contentType, "LIVERY");

    private static bool IsType(string? contentType, string expected) => string.Equals(contentType?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
