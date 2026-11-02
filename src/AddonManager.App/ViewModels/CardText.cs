using AddonManager.Core.Cards;
using GsxMarkKind = AddonManager.Core.Gsx.GsxMark;

namespace AddonManager.App.ViewModels;

/// <summary>The fixed copy of the wall's cards (plan 5a, D12).</summary>
public static class CardText
{
    public static string PackageCount(int n) => n == 1 ? "1 package" : $"{n} packages";

    /// <summary>The GSX mark of an Airports card; <c>null</c> when there is nothing to show.</summary>
    public static string? GsxMark(GsxMarkKind mark) => mark switch
    {
        GsxMarkKind.Installed => "GSX",
        GsxMarkKind.Missing => "GSX missing",
        GsxMarkKind.TwoProfiles => "GSX ×2",
        _ => null,
    };

    /// <summary>The warning, then ": " and the Package name, or the ICAO code for two GSX profiles.</summary>
    public static string Warning(CardWarning w) => w.Kind switch
    {
        WarningKind.DuplicatePackage => $"Duplicate Package: {w.Package.Value}",
        WarningKind.DuplicateCopy => $"Duplicate copy: {w.Package.Value}",
        WarningKind.LiveryWithoutAircraft => $"Livery without aircraft: {w.Package.Value}",
        WarningKind.NotFound => $"Not found: {w.Package.Value}",
        _ => $"Two GSX profiles: {w.Code?.Value ?? w.Package.Value}",
    };
}
