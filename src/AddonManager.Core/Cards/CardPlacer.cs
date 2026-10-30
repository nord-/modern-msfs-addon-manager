using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Cards;

/// <summary>
/// Gives Packages that are on no card a card of their own (spec 6.5). Placement only adds cards: it never moves, merges or edits
/// an existing one (rule R5). New Scenery Packages with ICAO codes are grouped by their set of codes and join an existing card when one fits.
/// New aircraft Packages are grouped by Container and join the oldest Aircraft card that provides one of them, else the oldest that only repaints it.
/// </summary>
public static class CardPlacer
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Makes a card in its section for every candidate that is on no card, in name order. <paramref name="packages"/> holds every
    /// summary; only <paramref name="candidates"/> are placed, and <see cref="Settlement"/> decides which those are. Returns true when <c>state</c> changed; the caller saves.
    /// <paramref name="profiles"/> are the GSX profile Packages from <see cref="ProfileCatalog"/>: every Found one gets a stored state and memo, and
    /// those on no card are placed together with the new folder Packages, as Scenery Packages with one ICAO code each.
    /// </summary>
    public static bool Place(
        UserState state,
        IReadOnlyDictionary<PackageName, PackageSummary> packages,
        IReadOnlySet<PackageName> candidates,
        ContentAnalysis analysis,
        DateTimeOffset now,
        IReadOnlyList<PackageSummary>? profiles = null)
    {
        var found = (profiles ?? []).Where(p => p.Presence == Presence.Found).ToList();
        var placed = StoreProfiles(state, found);
        var unplaced = candidates
            .Where(packages.ContainsKey)
            .Select(n => packages[n])
            .Where(s => state.CardOf(PackageKey.Folder(s.Name)) is null)
            .Concat(found.Where(p => state.CardOf(p.Key) is null))
            .OrderBy(s => s.Name.Value, Names)
            .ThenBy(s => s.Kind)
            .ThenBy(s => s.Path, Names)
            .ToList();

        // Rule R5: only cards from before this call can be joined.
        var existing = state.Cards.Where(c => c.Section == Category.Scenery).ToList();
        var aircraftCards = ExistingAircraftCards(state, packages);
        var aircraftGroups = AircraftGroups(unplaced.Where(s => s.Section == Category.Aircraft && IsAircraft(s)).ToList(), analysis.Aircraft);
        var aircraftDone = new HashSet<PackageName>();
        var groups = unplaced
            .Where(s => s.Section == Category.Scenery && s.Codes.Count > 0)
            .GroupBy(s => string.Join(",", s.Codes.Select(c => c.Value).Order(StringComparer.Ordinal)))
            .ToDictionary(g => g.Key, g => g.ToList());
        var done = new HashSet<string>();

        foreach (var summary in unplaced)
        {
            if (summary.Section == Category.Scenery && summary.Codes.Count > 0)
            {
                var key = string.Join(",", summary.Codes.Select(c => c.Value).Order(StringComparer.Ordinal));
                if (done.Add(key))
                {
                    var members = groups[key];
                    var card = JoinableCard(existing, summary.Codes) ?? state.NewCard(
                        Category.Scenery,
                        CardTabs.Initial(Category.Scenery, members, summary.Codes.Count, analysis.Aircraft),
                        summary.Codes,
                        now);
                    foreach (var member in members)
                    {
                        state.SetCard(member.Key, card.Id);
                    }

                    placed = true;
                }

                continue;
            }

            if (aircraftGroups.TryGetValue(summary.Name, out var group))
            {
                if (aircraftDone.Add(group.Members[0].Name))
                {
                    var card = JoinableAircraftCard(aircraftCards, group.Containers)
                        ?? state.NewCard(Category.Aircraft, CardTabs.Initial(Category.Aircraft, group.Members, 0, analysis.Aircraft), [], now);
                    foreach (var member in group.Members)
                    {
                        state.SetCard(PackageKey.Folder(member.Name), card.Id);
                    }

                    placed = true;
                }

                continue;
            }

            var section = summary.Section;
            var tab = CardTabs.Initial(section, [summary], 0, analysis.Aircraft);
            var own = state.NewCard(section, tab, [], now);
            state.SetCard(summary.Key, own.Id);
            placed = true;
        }

        return placed;
    }

    /// <summary>Gives every Found profile a stored state with its memo, and Scenery as its section on first placement (rule R4). Returns true when anything changed.</summary>
    private static bool StoreProfiles(UserState state, List<PackageSummary> found)
    {
        var changed = false;
        foreach (var profile in found)
        {
            var memo = ProfileCatalog.MemoOf(profile);
            var current = state.Profiles.GetValueOrDefault(profile.Name);
            if (current is not null && memo.SameAs(current.Memo) && current.Section is not null)
            {
                continue;
            }

            state.Profiles[profile.Name] = (current ?? new PackageState(false)) with { Memo = memo, Section = current?.Section ?? Category.Scenery };
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// The oldest existing card that fits a set of ICAO codes, by the first tier that has one: exactly these codes; exactly these codes
    /// counting removed ones; has all of them; covers all of them counting removed ones (spec 6.5).
    /// </summary>
    private static CardRecord? JoinableCard(IReadOnlyList<CardRecord> existing, IReadOnlyList<AirportCode> set)
    {
        static bool Equal(IReadOnlyList<AirportCode> have, IReadOnlyList<AirportCode> want)
            => have.ToHashSet().SetEquals(want);

        static bool Covers(IReadOnlyList<AirportCode> have, IReadOnlyList<AirportCode> want)
            => want.All(have.Contains);

        return existing.Where(c => Equal(c.Codes, set)).MinBy(c => c.Id)
            ?? existing.Where(c => Equal(c.MatchCodes, set)).MinBy(c => c.Id)
            ?? existing.Where(c => Covers(c.Codes, set)).MinBy(c => c.Id)
            ?? existing.Where(c => Covers(c.MatchCodes, set)).MinBy(c => c.Id);
    }

    private static bool IsAircraft(PackageSummary s)
        => s.Containers.Count > 0 && s.Role is not (AircraftRole.None or AircraftRole.AirTraffic) && !CategoryRules.IsInstruments(s.ContentType);

    private sealed record AircraftCard(CardRecord Card, HashSet<string> Provided, HashSet<string> Repainted);

    private static bool IsProvider(PackageSummary s) => s.Role is AircraftRole.Provider or AircraftRole.Mod or AircraftRole.LockedBase;

    /// <summary>
    /// Every existing Aircraft card with the Containers its Packages provide and the ones they only repaint or attach to, from the summaries
    /// (the memo's for a Not found Package), taken before any card is made.
    /// </summary>
    private static List<AircraftCard> ExistingAircraftCards(UserState state, IReadOnlyDictionary<PackageName, PackageSummary> packages)
    {
        var cards = new List<AircraftCard>();
        foreach (var card in state.Cards.Where(c => c.Section == Category.Aircraft))
        {
            var members = state.PackagesOn(card.Id).Where(k => k.Kind == PackageKind.Folder && packages.ContainsKey(k.Name)).Select(k => packages[k.Name]).ToList();
            cards.Add(new AircraftCard(
                card,
                members.Where(IsProvider).SelectMany(m => m.Containers).ToHashSet(Names),
                members.Where(m => !IsProvider(m)).SelectMany(m => m.Containers).ToHashSet(Names)));
        }

        return cards;
    }

    /// <summary>
    /// The oldest existing card that provides one of the Containers; failing that the oldest that only repaints or attaches to one, so a
    /// Livery that covers several aircraft never pulls a Package into the wrong card (spec 6.6).
    /// </summary>
    private static CardRecord? JoinableAircraftCard(IReadOnlyList<AircraftCard> cards, HashSet<string> containers)
        => cards.Where(c => c.Provided.Overlaps(containers)).Select(c => c.Card).MinBy(c => c.Id)
           ?? cards.Where(c => c.Repainted.Overlaps(containers)).Select(c => c.Card).MinBy(c => c.Id);

    private sealed record AircraftGroup(List<PackageSummary> Members, HashSet<string> Containers);

    /// <summary>
    /// Groups new aircraft Packages among themselves, each Package name mapping to its group. Providers (Provider, Mod, LockedBase) are
    /// united by shared Containers. A Livery or Attached Package joins the group of its first Container by name that has a provider or a
    /// locked base, any source; with none it stands alone with its own Containers. A group's Containers are its providers' and the
    /// Container a Livery or Attached Package chose, so a Livery for two aircraft never merges them.
    /// </summary>
    private static Dictionary<PackageName, AircraftGroup> AircraftGroups(List<PackageSummary> aircraft, AircraftIndex index)
    {
        var parent = new Dictionary<string, string>(Names);

        string Find(string c)
        {
            parent.TryAdd(c, c);
            while (!Names.Equals(parent[c], c))
            {
                parent[c] = parent[parent[c]];
                c = parent[c];
            }

            return c;
        }

        void Union(string a, string b)
        {
            var (ra, rb) = (Find(a), Find(b));
            if (!Names.Equals(ra, rb))
            {
                parent[rb] = ra;
            }
        }

        bool Provided(string c) => index.Groups.FirstOrDefault(g => Names.Equals(g.Key, c)).Value is { } g && (g.Providers.Count > 0 || g.LockedBases.Count > 0);

        foreach (var p in aircraft.Where(IsProvider))
        {
            var list = p.Containers.Order(Names).ToList();
            list.ForEach(c => Union(list[0], c));
        }

        var groups = new Dictionary<string, AircraftGroup>(Names);
        var result = new Dictionary<PackageName, AircraftGroup>();
        foreach (var p in aircraft)
        {
            // A non-provider chooses its first Container by name that is provided; null when it stands alone.
            var chosen = IsProvider(p) ? p.Containers.Order(Names).First() : p.Containers.Order(Names).FirstOrDefault(Provided);
            AircraftGroup group;
            if (chosen is null)
            {
                group = new AircraftGroup([], [.. p.Containers]);
            }
            else
            {
                var root = Find(chosen);
                if (!groups.TryGetValue(root, out var known))
                {
                    groups[root] = known = new AircraftGroup([], new HashSet<string>(Names));
                }

                group = known;
                group.Containers.UnionWith(IsProvider(p) ? p.Containers : [chosen]);
            }

            group.Members.Add(p);

            result[p.Name] = group;
        }

        return result;
    }
}
