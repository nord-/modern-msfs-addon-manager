using AddonManager.Core.Content;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Cards;

/// <summary>One profile Package from the GSX folder; .ini and .off of one name are one profile (plan 4 reads them).</summary>
/// <param name="Name">The file name without its extension, which is also the display name (spec 8).</param>
/// <param name="Code">The ICAO code the file name yields.</param>
/// <param name="Path">The file's path.</param>
/// <param name="Disabled">True for a <c>.off</c> file.</param>
public sealed record GsxProfile(PackageName Name, AirportCode Code, string Path, bool Disabled)
{
    /// <summary>The <c>.off</c> file when both the <c>.ini</c> and the <c>.off</c> of one name exist.</summary>
    public string? OffVariantPath { get; init; }
}
