using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Cards;

/// <summary>Builds the card wall as a pure function of <see cref="UserState"/> and <see cref="BoardInput"/>; it stores nothing.</summary>
public static class BoardBuilder
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    public static Board Build(UserState state, BoardInput input)
    {
        var summaries = PackageCatalog.Build(state, input);
        var profiles = ProfileCatalog.Build(state, input);
        var facts = new Facts(state, input, summaries, profiles);

        // Not found and Library-not-found Packages count too, so a folder's tag doesn't flicker while one of its Packages is on an unplugged drive.
        var tagIndex = PackageCatalog.TagIndexOf(state, input, summaries);

        var cards = new List<Card>();
        var notShown = new List<NotShownPackage>();

        foreach (var record in state.Cards)
        {
            var packages = PackagesOf(state.PackagesOn(record.Id), summaries, profiles);
            if (packages.Count > 0)
            {
                cards.Add(MakeCard(record, packages, CardLayout.Compute(record, packages, input.Analysis, record.Section), tagIndex, facts));
            }
        }

        foreach (var summary in summaries.Values.Where(s => state.CardOf(PackageKey.Folder(s.Name)) is null))
        {
            // Spec 6.4, 6.5: no Library registered for its section holds it, so it is Not shown; else it waits as a provisional card in its manifest section.
            var section = summary.FixedSection ?? summary.ManifestSection;
            if (summary.IsNotShown)
            {
                notShown.Add(new NotShownPackage(summary.Name, summary.DisplayName, summary.Library, section, summary.FixedSection is not null));
            }
            else
            {
                cards.Add(MakeCard(null, [summary], CardLayout.Compute(null, [summary], input.Analysis, section), tagIndex, facts));
            }
        }

        // A profile on no card waits as a provisional card on Scenery → Landscapes (spec 6.5); it is never Not shown.
        foreach (var profile in profiles.Where(p => p.Presence == Presence.Found && state.CardOf(p.Key) is null))
        {
            cards.Add(MakeCard(null, [profile], CardLayout.Compute(null, [profile], input.Analysis, Category.Scenery), tagIndex, facts));
        }

        var banner = MakeBanner(state, input, summaries, cards.Count(c => c.Id is null));
        return new Board(WithBuiltIns(Ordered(cards), banner, facts), [.. notShown.OrderBy(n => n.Name.Value, Names)], banner)
        {
            GsxNotRecognised = ProfileCatalog.NotRecognised(state, input),
        };
    }

    private static List<PackageSummary> PackagesOf(IEnumerable<PackageKey> keys, IReadOnlyDictionary<PackageName, PackageSummary> summaries, IReadOnlyList<PackageSummary> profiles)
        => [.. ProfileCatalog.Resolve(keys, summaries, profiles)
            .OrderBy(p => p.Name.Value, Names)
            .ThenBy(p => p.Kind)
            .ThenBy(p => p.Path, Names)];

    private static Card MakeCard(CardRecord? record, List<PackageSummary> packages, CardLayout layout, TagIndex tagIndex, Facts facts)
    {
        var cardPackages = packages.Select(p => new CardPackage(
            p.Kind,
            p.Name,
            p.DisplayName,
            p.Path,
            p.Version,
            p.Library,
            p.Source,
            p.Presence,
            p.Lock,
            p.Holder,
            p.Role,
            p.Copies,
            p.Codes,
            facts.FolderTagsOf(p, tagIndex),
            facts.IsOn(p),
            p.Presence == Presence.Found && p.Lock == LockReason.None,
            DisabledReason(p, facts),
            facts.Replaces(p),
            p.Presence == Presence.NotFound || p.Lock == LockReason.NotInRegisteredLibrary) { Disabled = p.Disabled }).ToList();

        // Type designators are the aircraft's own tags (spec 6.4): they join the folder tags on Aircraft cards only.
        IReadOnlyList<string> designators = layout.Section == Category.Aircraft
            ? [.. packages.SelectMany(p => p.TypeDesignators).Distinct(Names).Order(Names)]
            : [];
        var gsxAirports = facts.GsxAirportsOf(layout.Codes, packages);
        return new Card(
            record?.Id,
            record is null ? packages[0].Key : null,
            layout.Section,
            layout.Tab,
            layout.Name,
            layout.ImagePath,
            layout.Codes,
            layout.RemovedCodes,
            layout.AirportName,
            cardPackages,
            [.. cardPackages.SelectMany(p => p.Tags).Concat(designators).Distinct(Names).Order(Names)],
            designators,
            StateOf(cardPackages),
            packages.Any(p => p.Kind == PackageKind.Folder && facts.State.Pending.Contains(p.Name)),
            [],
            [.. facts.WarningsOf(packages), .. TwoGsxProfileWarnings(gsxAirports)])
        {
            Gsx = gsxAirports.Count > 0 ? gsxAirports[0].Status : GsxMark.None,
            GsxAirports = gsxAirports,
        };
    }

    /// <summary>Plan 4, D10: one warning per ICAO code of the card whose status is Two profiles, also when the profiles sit on another card.</summary>
    private static IEnumerable<CardWarning> TwoGsxProfileWarnings(IReadOnlyList<GsxAirport> airports)
        => airports
            .Where(a => a.Status == GsxMark.TwoProfiles)
            .OrderBy(a => a.Code.Value, StringComparer.Ordinal)
            .Select(a => new CardWarning(WarningKind.TwoGsxProfiles, a.Active[0]) { Code = a.Code });

    /// <summary>
    /// Only Packages that lie in a registered Library count (spec 6.3), with their stored choice; a name-taken Package counts as off.
    /// Locked Packages, Packages no registered Library holds, and a Not found Fixed or External Package don't. A card of none is Locked.
    /// </summary>
    private static CardState StateOf(List<CardPackage> packages)
    {
        var counted = packages.Where(p => (p.Lock is LockReason.None or LockReason.NameTaken) && p.Library is not null).ToList();
        return counted.Count == 0 ? CardState.Locked
            : counted.All(p => p.On) ? CardState.On
            : counted.Any(p => p.On) ? CardState.Mixed
            : CardState.Off;
    }

    private static string? DisabledReason(PackageSummary p, Facts facts)
    {
        var place = p.Library?.Path ?? (p.Source == PackageSource.Community ? "Community" : p.Source == PackageSource.GsxFolder ? "The GSX folder" : p.Path);
        return p.Presence switch
        {
            Presence.NotFound => $"Not found — {p.Path} is gone",
            Presence.LibraryNotFound => $"Library not found — {place} can't be reached",
            Presence.Unreadable => $"Unreadable — {facts.UnreadableFolder(p)} can't be read",
            _ => p.Lock switch
            {
                LockReason.NameTaken => $"Name taken by {p.Holder?.Class} entry '{p.Holder?.Entry.Name ?? p.Name.Value}' in Community",
                LockReason.NotInRegisteredLibrary => "Not in a registered Library",
                LockReason.GsxProfile => p.Disabled ? "Disabled — GSX profile is switched off (.off)" : "Locked — GSX profile",
                LockReason.HeldByEntry when facts.LeftItsLibrary(p) => "Not in a registered Library",
                LockReason.HeldByEntry when p.Holder?.Class == EntryClass.External => $"Locked — External link to {p.Holder.Entry.Target}",
                LockReason.HeldByEntry => $"Locked — {p.Holder?.Class} entry in Community",
                _ => null,
            },
        };
    }

    private static Banner MakeBanner(UserState state, BoardInput input, IReadOnlyDictionary<PackageName, PackageSummary> summaries, int provisionalCards)
    {
        List<ClassifiedEntry> Of(EntryClass entryClass)
            => [.. input.Community.Where(e => e.Class == entryClass).OrderBy(e => e.Entry.Name, Names)];

        return new Banner(
            Of(EntryClass.Broken),
            Of(EntryClass.Stray),
            state.Pending.Count,
            provisionalCards,
            [.. summaries.Values
                .Where(s => s.Presence == Presence.Unreadable
                            || (input.ScanComplete && s.Presence == Presence.Found && s.Lock != LockReason.NotInRegisteredLibrary && !s.HasContent))
                .Select(s => s.Name)
                .OrderBy(n => n.Value, Names)],
            [.. input.Index.Unreachable.OrderBy(l => l.Path, Names)])
        {
            // Plan 4, D3: only a machine that has stored profiles on cards can have lost the folder.
            GsxFolderMissing = !input.GsxFolderFound && state.Profiles.Any(p => p.Value.CardId is not null),
        };
    }

    /// <summary>
    /// Attaches Sim-managed content to the cards (spec 6.4): an official Airport to every Scenery card with its ICAO code; an aircraft
    /// base, livery or add-on to every Aircraft card whose Packages provide or repaint its container. No card is ever created for them.
    /// </summary>
    private static List<Card> WithBuiltIns(List<Card> cards, Banner banner, Facts facts)
    {
        var parts = cards.Select(_ => new List<BuiltInPart>()).ToList();
        var board = new Board(cards, [], banner);

        var airports = facts.SimManagedLinks().GroupBy(l => (l.Package.Path.ToUpperInvariant(), l.Code)).Select(g => g.First());
        foreach (var link in airports)
        {
            foreach (var target in board.CardsAt(link.Code).Where(c => c.Section == Category.Scenery))
            {
                parts[cards.FindIndex(c => ReferenceEquals(c, target))].Add(Part(link.Package, link.Code, null, AircraftRole.None));
            }
        }

        foreach (var group in facts.Input.Analysis.Aircraft.Groups.Values)
        {
            var holders = cards.Select((c, i) => (Card: c, Index: i))
                .Where(h => h.Card.Section == Category.Aircraft && h.Card.Packages.Any(p => ProvidesOrRepaints(p, group.Container, facts)));
            foreach (var holder in holders)
            {
                parts[holder.Index].AddRange(Members(group, group.LockedBases, AircraftRole.LockedBase));
                parts[holder.Index].AddRange(Members(group, group.Liveries, AircraftRole.Livery));
                parts[holder.Index].AddRange(Members(group, group.Attached, AircraftRole.Attached));
            }
        }

        return [.. cards.Select((c, i) => c with
        {
            BuiltIns = [.. parts[i].DistinctBy(p => (p.Name, p.Code, p.Container?.ToUpperInvariant()))
                .OrderBy(p => p.Name.Value, Names).ThenBy(p => p.Container ?? "", Names).ThenBy(p => p.Code?.Value ?? "", StringComparer.Ordinal)],
        })];

        static IEnumerable<BuiltInPart> Members(ContainerGroup group, IEnumerable<PackageContent> contents, AircraftRole role)
            => contents.Where(c => c.Source == PackageSource.SimManaged).Select(c => Part(c, null, group.Container, role));

        static BuiltInPart Part(PackageContent content, AirportCode? code, string? container, AircraftRole role)
            => new(content.Name, CardLayout.TitleOrName(content), code, container, role);
    }

    /// <summary>A Package makes a built-in aircraft visible as a Provider, Mod, locked base, Livery or attached Package (spec 6.4).</summary>
    private static bool ProvidesOrRepaints(CardPackage package, string container, Facts facts)
        => package.Role is AircraftRole.Provider or AircraftRole.Mod or AircraftRole.LockedBase or AircraftRole.Livery or AircraftRole.Attached
           && facts.ContainersOf(package).Contains(container, Names);

    /// <summary>Section, tab, then the stored cards, then the provisional ones by Package name, then name; on Airports the cards with ICAO codes come first, by first ICAO code. Ties go by card id, then Package name, never by input order.</summary>
    private static List<Card> Ordered(IEnumerable<Card> cards)
        => [.. cards
            .OrderBy(c => c.Section)
            .ThenBy(c => c.Tab)
            .ThenBy(c => c.Id is null ? 1 : 0)
            .ThenBy(c => c.ProvisionalPackage?.Name.Value ?? "", Names)
            .ThenBy(c => c.Tab == CardTab.Airports && c.Codes.Count > 0 ? 0 : 1)
            .ThenBy(c => c.Tab == CardTab.Airports && c.Codes.Count > 0 ? c.Codes[0].Value : "", StringComparer.Ordinal)
            .ThenBy(c => c.Name, Names)
            .ThenBy(c => c.Id ?? long.MaxValue)
            .ThenBy(c => c.ProvisionalPackage?.Name.Value ?? "", Names)
            .ThenBy(c => c.ProvisionalPackage?.Kind ?? PackageKind.Folder)];

    /// <summary>What card state, replacement and warnings are read from, gathered once per build.</summary>
    private sealed class Facts
    {
        private readonly IReadOnlyDictionary<PackageName, PackageSummary> summaries;
        private readonly HashSet<PackageName> simManaged;
        private readonly HashSet<PackageName> communityNames;
        private readonly ILookup<string, AirportCode> replaces;
        private readonly IReadOnlyList<PackageSummary> gsxProfiles;
        private readonly Dictionary<string, PackageContent> contentsByPath = new(Names);

        public Facts(UserState state, BoardInput input, IReadOnlyDictionary<PackageName, PackageSummary> summaries, IReadOnlyList<PackageSummary> gsxProfiles)
        {
            State = state;
            Input = input;
            this.summaries = summaries;
            this.gsxProfiles = gsxProfiles;
            foreach (var content in input.Contents)
            {
                contentsByPath.TryAdd(PathNormalizer.Normalize(content.Path), content);
            }

            simManaged = input.Contents.Where(c => c.Source == PackageSource.SimManaged).Select(c => c.Name).ToHashSet();
            communityNames = input.Community.Select(e => e.Name).ToHashSet();
            replaces = input.Analysis.ReplacesBuiltIn.ToLookup(r => PathNormalizer.Normalize(r.Package.Path), r => r.Code, Names);
        }

        public UserState State { get; }

        public BoardInput Input { get; }

        /// <summary>A Package held by an entry is always loaded; one whose name is taken, or that no registered Library holds, is off; else its stored choice.</summary>
        public bool IsOn(PackageSummary p) => p.Lock switch
        {
            LockReason.HeldByEntry => true,
            LockReason.NameTaken or LockReason.NotInRegisteredLibrary or LockReason.GsxProfile => false,
            _ => State.IsOn(p.Name),
        };

        /// <summary>The folder discovery could not read that holds the Package.</summary>
        public string UnreadableFolder(PackageSummary p) => Input.Index.Inaccessible.FirstOrDefault(f => PathNormalizer.IsUnder(p.Path, f)) ?? p.Path;

        /// <summary>
        /// An External entry holds a Package that was in a Library no registered Library holds any more: its link target is its stored copy or
        /// the path its memo remembers from the Library, and lies under none (spec 6.1). A folder the user never registered stays plainly External.
        /// </summary>
        public bool LeftItsLibrary(PackageSummary p)
        {
            var package = State.Packages.GetValueOrDefault(p.Name);
            var wasInLibrary = (package?.ActiveCopyPath is { } stored && PathNormalizer.AreEqual(stored, p.Path))
                               || (package?.Memo is { Source: PackageSource.Library } memo && PathNormalizer.AreEqual(memo.Path, p.Path));
            return p.Holder?.Class == EntryClass.External && wasInLibrary && LibraryRoots.Innermost(State.Libraries, p.Path) is null;
        }

        /// <summary>The Containers of a card's Package, from its summary (the memo's for a Not found Package).</summary>
        public IReadOnlyList<string> ContainersOf(CardPackage p) => p.Kind == PackageKind.Folder && summaries.TryGetValue(p.Name, out var s) ? s.Containers : [];

        /// <summary>The tags of a Package's Library folders: read now when it is found, else the memo's (spec 6.4), so a Package that can't be seen keeps them.</summary>
        public IReadOnlyList<string> FolderTagsOf(PackageSummary p, TagIndex tagIndex)
        {
            if (p.Source != PackageSource.Library)
            {
                return [];
            }

            return p.Presence != Presence.Found && State.Packages.GetValueOrDefault(p.Name)?.Memo is { } memo ? [.. memo.Tags] : tagIndex.TagsOf(p.Path);
        }

        public IReadOnlyList<AirportCode> Replaces(PackageSummary p)
            => p.Path.Length == 0 ? [] : [.. replaces[PathNormalizer.Normalize(p.Path)].Distinct().OrderBy(c => c.Value, StringComparer.Ordinal)];

        public IEnumerable<AirportLink> SimManagedLinks()
            => AirportCatalog.RealLinks(Input.Analysis.AirportLinks, State.Content).SelectMany(g => g).Where(l => l.Package.Source == PackageSource.SimManaged);

        /// <summary>The GSX facts of each ICAO code of a card, in the card's order; none for a card without ICAO codes.</summary>
        public IReadOnlyList<GsxAirport> GsxAirportsOf(IReadOnlyList<AirportCode> codes, IReadOnlyList<PackageSummary> packages)
        {
            // Spec 8: a GSX folder that can't be read changes nothing, so no card claims a status the folder can't back up; the banner says why.
            if (codes.Count == 0 || !Input.GsxFolderFound)
            {
                return [];
            }

            var shipped = ShippedProfilesOf(codes, packages);
            return [.. codes.Select(code => GsxStatus.For(code, gsxProfiles, Input.GsxScripts, shipped))];
        }

        /// <summary>
        /// The profiles the card's Found folder Packages ship under the card's ICAO codes (plan 4, D4). A Package that is not Found, or whose
        /// content is not known, has no readable files, so it ships nothing.
        /// </summary>
        private List<ShippedProfile> ShippedProfilesOf(IReadOnlyList<AirportCode> codes, IReadOnlyList<PackageSummary> packages)
        {
            var result = new List<ShippedProfile>();
            foreach (var package in packages.Where(p => p.Kind == PackageKind.Folder && p.Presence == Presence.Found))
            {
                if (!contentsByPath.TryGetValue(PathNormalizer.Normalize(package.Path), out var content))
                {
                    continue;
                }

                var files = content.GsxFiles.Select(f => (Path: f, Parsed: GsxFileName.TryParse(Path.GetFileName(f), out var kind, out var code), Kind: kind, Code: code)).Where(f => f.Parsed).ToList();
                foreach (var ini in files.Where(f => f.Kind == GsxFileKind.Profile && codes.Contains(f.Code)))
                {
                    var folder = Path.GetDirectoryName(ini.Path) ?? "";
                    var stem = Path.GetFileNameWithoutExtension(ini.Path);
                    var scripts = files
                        .Where(f => f.Kind == GsxFileKind.Script && f.Code == ini.Code && Names.Equals(Path.GetDirectoryName(f.Path) ?? "", folder))
                        .Select(f => Path.Combine(content.Path, f.Path));
                    var inFolder = Input.GsxFileNames.Contains(stem + ".ini") || Input.GsxFileNames.Contains(stem + ".off");
                    result.Add(new ShippedProfile(ini.Code, package.Name, Path.Combine(content.Path, ini.Path), [.. scripts], inFolder));
                }
            }

            return result;
        }

        public IReadOnlyList<CardWarning> WarningsOf(IEnumerable<PackageSummary> packages)
        {
            var warnings = new List<CardWarning>();
            foreach (var p in packages)
            {
                // A profile is not a folder: only its own Not found warns.
                if (p.Kind == PackageKind.GsxProfile)
                {
                    if (p.Presence == Presence.NotFound)
                    {
                        warnings.Add(new CardWarning(WarningKind.NotFound, p.Name));
                    }

                    continue;
                }

                if (communityNames.Contains(p.Name) && simManaged.Contains(p.Name))
                {
                    warnings.Add(new CardWarning(WarningKind.DuplicatePackage, p.Name));
                }

                if (Input.Index.IsDuplicateCopy(p.Name))
                {
                    warnings.Add(new CardWarning(WarningKind.DuplicateCopy, p.Name));
                }

                if (p.Presence == Presence.NotFound)
                {
                    warnings.Add(new CardWarning(WarningKind.NotFound, p.Name));
                }

                if (LiveryWithoutAircraft(p))
                {
                    warnings.Add(new CardWarning(WarningKind.LiveryWithoutAircraft, p.Name));
                }
            }

            return [.. warnings.OrderBy(w => w.Package.Value, Names).ThenBy(w => w.Kind)];
        }

        /// <summary>
        /// An On livery warns when none of the containers it extends has a provider that is on or a base that is always loaded
        /// (spec 6.7); one such container is enough to clear the warning.
        /// </summary>
        private bool LiveryWithoutAircraft(PackageSummary p)
        {
            if (p.Role != AircraftRole.Livery || p.Containers.Count == 0 || !IsOn(p))
            {
                return false;
            }

            return p.Containers.All(container =>
            {
                var group = Input.Analysis.Aircraft.Groups.FirstOrDefault(g => Names.Equals(g.Key, container)).Value;
                return group is null
                       || (group.LockedBases.Count == 0 && !group.Providers.Any(c => summaries.TryGetValue(c.Name, out var s) && IsOn(s)));
            });
        }
    }
}
