using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Cards;

/// <summary>
/// The GSX profile Packages (spec 8), next to <see cref="PackageCatalog"/> for folder Packages. A profile is a locked Scenery Package with
/// one ICAO code. Pure: reads <see cref="UserState"/> and <see cref="BoardInput"/> and changes nothing.
/// </summary>
public static class ProfileCatalog
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// A summary for every profile file that is a Package, plus one for every stored profile on a card whose file is gone (Not found, or
    /// Library not found while the GSX folder is missing), sorted by name. A profile that is on a card stays a Package, whether or not its ICAO code is known. A file whose ICAO code has 3 characters that the analysis does not
    /// know is not a Package: it is looked at again on every call, since a later scan can make the ICAO code known (spec 8).
    /// </summary>
    public static IReadOnlyList<PackageSummary> Build(UserState state, BoardInput input)
    {
        var present = new Dictionary<PackageName, GsxProfile>();
        var ignored = new HashSet<PackageName>();
        foreach (var profile in input.Profiles.OrderBy(p => p.Name.Value, Names).ThenBy(p => p.Path, Names))
        {
            // A profile already on a card stays a Package even when its 3-character ICAO code is no longer known.
            if (IsPackage(profile, input.Analysis) || state.CardOf(new PackageKey(PackageKind.GsxProfile, profile.Name)) is not null)
            {
                present.TryAdd(profile.Name, profile);
            }
            else
            {
                ignored.Add(profile.Name);
            }
        }

        var result = present.Values
            .Select(p => Make(p.Name, p.Name.Value, p.Path, Presence.Found, p.Code, p.Disabled, state.Profiles.GetValueOrDefault(p.Name)?.Section))
            .ToList();

        // A stored profile that is no longer a file stays while it is on a card, so the card keeps it (spec 8: deleted means Not found).
        var gone = input.GsxFolderFound ? Presence.NotFound : Presence.LibraryNotFound;
        foreach (var (name, stored) in state.Profiles)
        {
            if (present.ContainsKey(name) || ignored.Contains(name) || stored.Memo is not { } memo || state.CardOf(new PackageKey(PackageKind.GsxProfile, name)) is null)
            {
                continue;
            }

            // Disabled is false here: the memo does not keep whether the file was .off.
            result.Add(Make(name, memo.DisplayName, memo.Path, gone, memo.Codes.FirstOrDefault(), false, stored.Section));
        }

        return [.. result.OrderBy(s => s.Name.Value, Names)];
    }

    /// <summary>The summaries of Packages of both kinds by key; a key with no summary is left out.</summary>
    internal static List<PackageSummary> Resolve(
        IEnumerable<PackageKey> keys,
        IReadOnlyDictionary<PackageName, PackageSummary> folders,
        IReadOnlyList<PackageSummary> profiles)
    {
        var byName = profiles.ToDictionary(p => p.Name);
        return [.. keys
            .Select(k => k.Kind == PackageKind.Folder ? folders.GetValueOrDefault(k.Name) : byName.GetValueOrDefault(k.Name))
            .OfType<PackageSummary>()];
    }

    /// <summary>The facts <see cref="CardPlacer.Place"/> stores for a Found profile, so the board can show it when it is gone.</summary>
    internal static PackageMemo MemoOf(PackageSummary profile)
        => new(profile.Path, PackageSource.GsxFolder, profile.DisplayName, null, null, null, BglPlacement.None, [.. profile.OwnCodes], [], AircraftRole.None, []);

    private static bool IsPackage(GsxProfile profile, ContentAnalysis analysis)
        => profile.Code != default && (profile.Code.Value.Length != 3 || analysis.Airports.ContainsKey(profile.Code));

    private static PackageSummary Make(PackageName name, string displayName, string path, Presence presence, AirportCode code, bool disabled, Category? fixedSection)
    {
        IReadOnlyList<AirportCode> codes = code == default ? [] : [code];
        return new PackageSummary(
            name,
            displayName,
            path,
            PackageSource.GsxFolder,
            presence,
            LockReason.GsxProfile,
            null,
            presence == Presence.Found,
            Category.Scenery,
            fixedSection,
            Category.Scenery,
            null,
            null,
            null,
            BglPlacement.None,
            codes,
            codes,
            [],
            AircraftRole.None,
            null,
            [],
            [],
            [],
            [])
        {
            Kind = PackageKind.GsxProfile,
            Disabled = disabled,
        };
    }
}
