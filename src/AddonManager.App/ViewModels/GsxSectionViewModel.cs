using AddonManager.App.Session;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;

namespace AddonManager.App.ViewModels;

/// <summary>
/// One GSX profile row of the card dialog (spec 6). <c>Key</c> is set only for the card's own profile Packages; <c>OnCard</c> names the card
/// an active profile sits on when it is not this card; <c>TwoProfilesTip</c> is set when more than one profile is active for its ICAO code.
/// </summary>
public sealed record GsxProfileRow(string Name, PackageKey? Key, bool Dimmed, string? Label, OtherCard? OnCard, string? TwoProfilesTip, bool CanRemove)
    : IRemovableRow
{
    string IRemovableRow.FolderName => Name;
}

/// <summary>An ICAO code with no active profile: an icon-only search link with <c>Tip</c> as its tooltip, and the error of the last failed open.</summary>
public sealed record GsxSearchRow(AirportCode Code, string Link, string Tip, string? Error);

/// <summary>The GSX section of the card dialog (spec 6), display only. <c>Rows</c> holds <see cref="GsxProfileRow"/> and <see cref="GsxSearchRow"/>.</summary>
public sealed record GsxSectionViewModel(IReadOnlyList<object> Rows)
{
    private const string NotFoundLabel = "Not found";
    private const string ShippedLabel = "shipped with scenery";

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    public bool IsVisible => Rows.Count > 0;

    public static GsxSectionViewModel Build(Card card, Board board, IReadOnlyDictionary<AirportCode, string> searchErrors)
    {
        var self = CardRef.Of(card);
        var profiles = card.Packages.Where(p => p.Kind == PackageKind.GsxProfile).ToList();
        var rows = new List<object>();

        foreach (var code in card.Codes)
        {
            var gsx = card.GsxAirports.FirstOrDefault(a => a.Code == code);
            var tip = TwoProfilesTip(gsx);
            var group = profiles.Where(p => p.Codes.Contains(code)).Select(p => OwnRow(p, gsx, tip)).ToList();

            foreach (var name in gsx?.Active ?? [])
            {
                if (board.CardOf(new PackageKey(PackageKind.GsxProfile, name)) is { } other && CardRef.Of(other) != self)
                {
                    var package = other.Packages.First(p => p.Kind == PackageKind.GsxProfile && p.Name == name);
                    group.Add(new GsxProfileRow(package.DisplayName, null, false, null, new OtherCard(other.Name, CardRef.Of(other)), tip, false));
                }
            }

            foreach (var shipped in gsx?.Shipped ?? [])
            {
                var name = Path.GetFileNameWithoutExtension(shipped.IniPath);
                if (!shipped.InGsxFolder || !group.Any(r => Names.Equals(r.Name, name)))
                {
                    group.Add(new GsxProfileRow(name, null, true, ShippedLabel, null, null, false));
                }
            }

            rows.AddRange(group);
            if (gsx is { Active.Count: 0 })
            {
                rows.Add(new GsxSearchRow(code, gsx.SearchLink, $"Search flightsim.to for {code.Value} GSX profiles", searchErrors.GetValueOrDefault(code)));
            }
        }

        // Profiles of codes the card lacks: the two-profiles warning holds on any card (spec 6).
        var anyCard = board.Cards.SelectMany(c => c.GsxAirports).ToList();
        foreach (var profile in profiles.Where(p => !p.Codes.Any(card.Codes.Contains)))
        {
            var gsx = profile.Codes
                .Select(code => anyCard.FirstOrDefault(a => a.Code == code))
                .FirstOrDefault(a => a is not null && a.Active.Contains(profile.Name));
            rows.Add(OwnRow(profile, gsx, TwoProfilesTip(gsx)));
        }

        return new GsxSectionViewModel(rows);
    }

    private static string? TwoProfilesTip(GsxAirport? gsx) => gsx is { Active.Count: > 1 } ? $"Two GSX profiles for {gsx.Code.Value}" : null;

    /// <summary>
    /// A profile Package of the card. Found and enabled: warned when it is one of two active profiles. Disabled, or not seen: dimmed; only
    /// Not found is labelled and keeps its Remove button.
    /// </summary>
    private static GsxProfileRow OwnRow(CardPackage profile, GsxAirport? gsx, string? tip)
    {
        var active = profile.Presence == Presence.Found && !profile.Disabled;
        var notFound = profile.Presence == Presence.NotFound;
        return new GsxProfileRow(
            profile.DisplayName,
            new PackageKey(profile.Kind, profile.Name),
            !active,
            notFound ? NotFoundLabel : null,
            null,
            active && gsx is not null && gsx.Active.Contains(profile.Name) ? tip : null,
            notFound && profile.Removable);
    }
}
