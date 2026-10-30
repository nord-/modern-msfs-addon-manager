using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Cards;

/// <summary>
/// What runs after every completed scan (spec 6.1, 6.4, 6.5): remembers what was seen, fixes each Package's menu section once,
/// forgets Packages that vanished without ever being on a card, and places the Packages that qualify for a card.
/// </summary>
public static class Settlement
{
    /// <summary>
    /// Settles a completed scan into <paramref name="state"/>. Returns true when anything changed; the caller saves.
    /// Throws <see cref="ArgumentException"/> for an input whose scan is not complete (rule R3). Plan 5's caller wraps this call and its
    /// <c>Save</c> in one <see cref="IUserStateStore.BeginChange"/> scope, so a locked file refuses the settle before it changes anything.
    /// </summary>
    public static bool Apply(UserState state, BoardInput input, DateTimeOffset now)
    {
        if (!input.ScanComplete)
        {
            throw new ArgumentException("Settlement needs the contents of a completed scan.", nameof(input));
        }

        if (!input.Reanalyzed)
        {
            // Without Reanalyze the memo's own ICAO codes would be stored empty.
            throw new ArgumentException("Settlement needs an input built by BoardInput.Reanalyze(state).", nameof(input));
        }

        var summaries = PackageCatalog.Build(state, input);
        var changed = PackageCatalog.Remember(state, summaries, input);
        changed |= FixSections(state, input, summaries);
        changed |= Forget(state, input, summaries);

        // The sections and the forgotten Packages changed the summaries, so placement sees the current ones.
        summaries = PackageCatalog.Build(state, input);
        var candidates = summaries.Values
            .Where(s => IsCandidate(state, input, s))
            .Select(s => s.Name)
            .ToHashSet();
        var profiles = ProfileCatalog.Build(state, input);
        changed |= ForgetProfiles(state, input, profiles);
        changed |= CardPlacer.Place(state, summaries, candidates, input.Analysis, now, profiles);

        if (!state.ContentPassCompleted)
        {
            state.ContentPassCompleted = true;
            changed = true;
        }

        return changed;
    }

    /// <summary>Rule R4: a settled read of a Found Package with content fixes its section, once.</summary>
    private static bool FixSections(UserState state, BoardInput input, IReadOnlyDictionary<PackageName, PackageSummary> summaries)
    {
        var changed = false;
        foreach (var (name, s) in summaries)
        {
            if (s.Presence != Presence.Found || !s.HasContent || !input.IsSettled(s.Path) || s.FixedSection is not null)
            {
                continue;
            }

            state.Packages[name] = (state.Packages.GetValueOrDefault(name) ?? new PackageState(false)) with { Section = s.Section };
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Forgets every Package that is on no card and has no summary, unless its stored copy or memo sits on an unreachable Library or
    /// in a folder discovery could not read: those may come back.
    /// </summary>
    private static bool Forget(UserState state, BoardInput input, IReadOnlyDictionary<PackageName, PackageSummary> summaries)
    {
        bool MayReturn(string? path)
            => path is not null && (input.Index.IsUnreachable(path) || input.Index.IsInaccessible(path));

        var changed = false;
        foreach (var (name, package) in state.Packages.ToList())
        {
            var key = PackageKey.Folder(name);
            if (state.CardOf(key) is not null || summaries.ContainsKey(name) || MayReturn(package.ActiveCopyPath) || MayReturn(package.Memo?.Path))
            {
                continue;
            }

            state.Forget(key);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Forgets every stored profile that is on no card and has no summary (its file is gone, or no longer a Package). While the GSX folder is
    /// missing nothing is forgotten: the profiles may come back (spec 8).
    /// </summary>
    private static bool ForgetProfiles(UserState state, BoardInput input, IReadOnlyList<PackageSummary> profiles)
    {
        if (!input.GsxFolderFound)
        {
            return false;
        }

        var shown = profiles.Select(p => p.Name).ToHashSet();
        var changed = false;
        foreach (var name in state.Profiles.Keys.ToList())
        {
            var key = new PackageKey(PackageKind.GsxProfile, name);
            if (state.CardOf(key) is null && !shown.Contains(name))
            {
                state.Forget(key);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Spec 6.1, 6.5: a card comes only from a scan that read the Package without errors, once its section is fixed and a Library registered for it holds it.</summary>
    private static bool IsCandidate(UserState state, BoardInput input, PackageSummary s)
        => s.Presence == Presence.Found
           && s.Source != PackageSource.SimManaged
           && s.HasContent
           && input.IsSettled(s.Path)
           && s.FixedSection is { } fixedSection
           && s.IsEligible(fixedSection)
           && state.CardOf(PackageKey.Folder(s.Name)) is null;
}
