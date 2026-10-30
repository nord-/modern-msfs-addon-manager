using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Cards;

/// <summary>Gathers everything known about each Package into a <see cref="PackageSummary"/> (spec 6.1 to 6.4, rules A1 and A6).</summary>
public static class PackageCatalog
{
    /// <summary>The Package under a name, from the index, Fixed and External entries, and what was last seen; a pure function of its arguments.</summary>
    public static IReadOnlyDictionary<PackageName, PackageSummary> Build(UserState state, BoardInput input)
    {
        var context = new Context(state, input);
        var result = new Dictionary<PackageName, PackageSummary>();
        foreach (var name in context.Names().OrderBy(n => n.Value, StringComparer.OrdinalIgnoreCase))
        {
            var summary = context.Summarise(name);
            if (summary is not null)
            {
                result[name] = summary;
            }
        }

        return result;
    }

    /// <summary>The tags of every Library folder above the Packages, counting Packages that are Not found or on an unreachable Library too, so a tag doesn't flicker.</summary>
    public static TagIndex TagIndexOf(UserState state, BoardInput input, IReadOnlyDictionary<PackageName, PackageSummary> packages)
        => TagIndex.Build(
            state.Libraries,
            input.Index.AllCopies.Select(c => c.Path).Concat(packages.Values.Where(s => s.Source == PackageSource.Library).Select(s => s.Path)));

    /// <summary>
    /// Stores a memo for every Found Package with content that was read completely and while quiet (rule R3: not Unsettled, not Failed).
    /// Returns true when any memo was added or differs; the caller saves.
    /// </summary>
    public static bool Remember(UserState state, IReadOnlyDictionary<PackageName, PackageSummary> packages, BoardInput input)
    {
        var changed = false;
        var tagIndex = TagIndexOf(state, input, packages);
        foreach (var (name, s) in packages)
        {
            if (s.Presence != Presence.Found || !s.HasContent || !input.IsSettled(s.Path))
            {
                continue;
            }

            var memo = new PackageMemo(s.Path, s.Source, s.DisplayName, s.ContentType, s.Version, s.ThumbnailPath, s.Bgl, [.. s.OwnCodes], [.. s.Containers], s.Role, [.. s.Airports])
            {
                Tags = s.Source == PackageSource.Library ? [.. tagIndex.TagsOf(s.Path)] : [],
                TypeDesignators = [.. s.TypeDesignators],
            };
            var current = state.Packages.GetValueOrDefault(name);
            if (current is null || !memo.SameAs(current.Memo))
            {
                state.Packages[name] = (current ?? new PackageState(false)) with { Memo = memo };
                changed = true;
            }
        }

        return changed;
    }

    private sealed class Context
    {
        private static readonly uint[] PlacingSections = [0x25, 0x65];

        private readonly UserState state;
        private readonly BoardInput input;
        private readonly Dictionary<string, PackageContent> contentByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<PackageName, ClassifiedEntry> entries = [];
        private readonly HashSet<PackageName> simManaged = [];
        private readonly Dictionary<string, List<AirportCode>> codesByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<AirportCode>> ownCodesByPath = new(StringComparer.OrdinalIgnoreCase);

        public Context(UserState state, BoardInput input)
        {
            this.state = state;
            this.input = input;
            foreach (var content in input.Contents)
            {
                contentByPath.TryAdd(PathNormalizer.Normalize(content.Path), content);
                if (content.Source == PackageSource.SimManaged)
                {
                    simManaged.Add(content.Name);
                }
            }

            foreach (var entry in input.Community)
            {
                entries.TryAdd(entry.Name, entry);
            }

            foreach (var group in AirportCatalog.RealLinks(input.Analysis.AirportLinks, state.Content).SelectMany(g => g)
                         .GroupBy(l => PathNormalizer.Normalize(l.Package.Path), StringComparer.OrdinalIgnoreCase))
            {
                codesByPath[group.Key] = [.. group.Select(l => l.Code).Distinct().OrderBy(c => c.Value, StringComparer.Ordinal)];
            }

            foreach (var group in input.OwnLinks.GroupBy(l => PathNormalizer.Normalize(l.Package.Path), StringComparer.OrdinalIgnoreCase))
            {
                ownCodesByPath[group.Key] = [.. group.Select(l => l.Code).Distinct().OrderBy(c => c.Value, StringComparer.Ordinal)];
            }
        }

        public IEnumerable<PackageName> Names()
        {
            var names = new HashSet<PackageName>(input.Index.Names);
            names.UnionWith(entries.Values.Where(e => e.Class is EntryClass.Fixed or EntryClass.External).Select(e => e.Name));
            // A carded Package keeps its row even when a Sim-managed package shares its name (it is Not found on its card, spec 6.1).
            names.UnionWith(state.Packages.Where(p => p.Value.Memo is not null && (p.Value.CardId is not null || !simManaged.Contains(p.Key))).Select(p => p.Key));
            return names;
        }

        public PackageSummary? Summarise(PackageName name)
        {
            entries.TryGetValue(name, out var entry);
            var package = state.Packages.GetValueOrDefault(name);
            var memo = package?.Memo;
            var copyPaths = input.Index.CopiesOf(name).Select(c => c.Path).Order(StringComparer.OrdinalIgnoreCase).ToList();

            if (entry?.Class is EntryClass.Fixed or EntryClass.External)
            {
                var content = entry.Class == EntryClass.External
                    ? Content(entry.Entry.Target)
                    : input.Contents.FirstOrDefault(c => c.Name == name && c.Source == PackageSource.Community);
                var path = entry.Class == EntryClass.External ? entry.Entry.Target : content?.Path;
                return Make(name, path ?? memo?.Path ?? entry.Entry.Path ?? "", PackageSource.Community, Presence.Found, LockReason.HeldByEntry, entry, content, null, memo, copyPaths);
            }

            var taken = entry?.Class is EntryClass.Ignored or EntryClass.Stray;
            var takenLock = taken ? LockReason.NameTaken : LockReason.None;
            var takenBy = taken ? entry : null;

            // A stored copy on an unreachable Library isn't indexed; it is still the Package's copy, so a reachable duplicate
            // must not lend it its path, Library or content (Review Focus 2: a Library-not-found Package keeps its card).
            var stored = package?.ActiveCopyPath;
            // The same holds for a stored copy in a folder discovery could not read: a duplicate elsewhere must not take it over.
            if (stored is not null && input.Index.IsInaccessible(stored))
            {
                return Make(name, stored, memo?.Source ?? PackageSource.Library, Presence.Unreadable, takenLock, takenBy, null, null, memo, copyPaths);
            }

            if (stored is not null && input.Index.IsUnreachable(stored))
            {
                return Make(name, stored, PackageSource.Library, Presence.LibraryNotFound, takenLock, takenBy, Content(stored), null, memo, copyPaths);
            }

            var copy = PackageFacts.ActiveCopy(state, name, input.Index, input.Community);
            if (copy is not null)
            {
                var libraryNotFound = PackageFacts.IsLibraryNotFound(state, name, input.Index, input.Community)
                              || PackageFacts.IsLibraryNotFound(state, name, input.Index, input.Community, copy.Path);
                return Make(name, copy.Path, PackageSource.Library, libraryNotFound ? Presence.LibraryNotFound : Presence.Found,
                    takenLock, takenBy, Content(copy.Path), copy, memo, copyPaths);
            }

            // Discovery could not read the folder that held the copy (by the stored path or the memo's, as Settlement's Forget checks both):
            // the Package is neither gone nor seen. It stays, Unreadable, and Settlement keeps its choices.
            var unreadable = new[] { stored, memo?.Path }.OfType<string>().FirstOrDefault(input.Index.IsInaccessible);
            if (unreadable is not null)
            {
                return Make(name, unreadable, memo?.Source ?? PackageSource.Library, Presence.Unreadable, takenLock, takenBy, null, null, memo, copyPaths);
            }

            if (memo is null)
            {
                return null;
            }

            if (input.Index.IsUnreachable(memo.Path) || entry?.Class == EntryClass.Offline)
            {
                return Make(name, memo.Path, memo.Source, Presence.LibraryNotFound, takenLock, takenBy, null, null, memo, copyPaths);
            }

            // A Package that is on no card and has no copy has vanished; it gets no summary so Settlement forgets it.
            if (state.CardOf(PackageKey.Folder(name)) is null)
            {
                return null;
            }

            if (LibraryRoots.Innermost(state.Libraries, memo.Path) is not null)
            {
                return Make(name, memo.Path, memo.Source, Presence.NotFound, takenLock, takenBy, null, null, memo, copyPaths);
            }

            // No registered Library holds it: it is still there (off and locked) when the folder exists, or when a run hasn't looked yet
            // (the startup board); else it is gone, since the app can't tell an unplugged drive outside the Libraries from a deleted folder (spec 6.1).
            var present = input.PresentOutsideLibraries is not { } outside || outside.Contains(PathNormalizer.Normalize(memo.Path));
            return present
                ? Make(name, memo.Path, memo.Source, Presence.Found, LockReason.NotInRegisteredLibrary, null, Content(memo.Path), null, memo, copyPaths)
                : Make(name, memo.Path, memo.Source, Presence.NotFound, takenLock, takenBy, null, null, memo, copyPaths);
        }

        private PackageContent? Content(string? path) => path is null ? null : contentByPath.GetValueOrDefault(PathNormalizer.Normalize(path));

        private PackageSummary Make(
            PackageName name,
            string path,
            PackageSource source,
            Presence presence,
            LockReason lockReason,
            ClassifiedEntry? holder,
            PackageContent? content,
            PackageCopy? copy,
            PackageMemo? memo,
            IReadOnlyList<string> copyPaths)
        {
            // A deleted scan cache must only cost a rescan (spec 11): a Package on a card, or one that can't be seen, keeps its memo facts.
            var remembered = content is null && memo is not null && (presence != Presence.Found || state.CardOf(PackageKey.Folder(name)) is not null) ? memo : null;
            var manifest = content?.Manifest ?? copy?.Manifest;
            var title = manifest?.Title;
            var onLibrary = source == PackageSource.Library;

            // The section rules (R4): the fixed section first, then what the content read says, then the manifest (the remembered content type for a Not found Package).
            var manifestSection = CategoryRules.Provisional(manifest?.ContentType ?? remembered?.ContentType, null);
            var fixedSection = state.Packages.GetValueOrDefault(name)?.Section;
            Category? contentCategory = content is null ? null
                : input.Analysis.Categories.TryGetValue(content.Path, out var c) ? c : CategoryRules.Final(content.Manifest.ContentType, content.HasBgl, content.IsAirTraffic);
            var section = fixedSection ?? contentCategory ?? manifestSection;

            // Every copy's Libraries decide eligibility; with no indexed copy, the stored copy's (else the memo's) Libraries do.
            var storedPath = state.Packages.GetValueOrDefault(name)?.ActiveCopyPath;
            IEnumerable<string> libraryPaths = copyPaths.Count > 0 ? copyPaths : [storedPath ?? memo?.Path ?? path];
            IReadOnlyList<Category> librarySections = onLibrary
                ? [.. libraryPaths.SelectMany(p => LibraryRoots.SectionsOf(state.Libraries, p)).Distinct().Order()]
                : [];

            // What the card makes of the remembered ICAO codes is what it makes of a found Package's own: its manual codes are added, its removed ones dropped.
            IReadOnlyList<AirportCode> ownCodes = content is not null ? ownCodesByPath.GetValueOrDefault(PathNormalizer.Normalize(content.Path)) ?? [] : remembered?.Codes ?? [];
            IReadOnlyList<AirportCode> codes = content is not null
                ? codesByPath.GetValueOrDefault(PathNormalizer.Normalize(content.Path)) ?? []
                : remembered is null ? [] : CardAdjusted(name, ownCodes);

            return new PackageSummary(
                name,
                !string.IsNullOrWhiteSpace(title) ? title : remembered?.DisplayName ?? name.Value,
                path,
                source,
                presence,
                lockReason,
                holder,
                content is not null,
                manifestSection,
                fixedSection,
                section,
                manifest?.ContentType ?? remembered?.ContentType,
                manifest?.PackageVersion ?? remembered?.Version,
                content is not null ? content.ThumbnailPath : remembered?.ThumbnailPath,
                content is not null ? BglOf(content) : remembered?.Bgl ?? BglPlacement.None,
                codes,
                ownCodes,
                content is not null ? input.Analysis.Aircraft.ContainersOf(content) : remembered?.Containers ?? [],
                content is not null ? input.Analysis.Aircraft.RoleOf(content) : remembered?.Role ?? AircraftRole.None,
                onLibrary ? LibraryRoots.Innermost(state.Libraries, path) : null,
                librarySections,
                copyPaths,
                content is not null ? RecordsOf([.. codes.Union(ownCodes).OrderBy(c => c.Value, StringComparer.Ordinal)]) : remembered?.Airports ?? [],
                content is not null ? DesignatorsOf(content) : remembered?.TypeDesignators ?? []);
        }

        /// <summary>The Package's own ICAO codes as its Scenery card shows them: removed ones gone, manual ones added (as <see cref="CardLinks.Overrides"/> does for a found Package).</summary>
        private IReadOnlyList<AirportCode> CardAdjusted(PackageName name, IReadOnlyList<AirportCode> own)
        {
            var card = state.CardOf(PackageKey.Folder(name));
            return card is null || card.Section != Category.Scenery
                ? own
                : [.. own.Where(c => !card.RemovedCodes.Contains(c)).Concat(card.Codes).Distinct().OrderBy(c => c.Value, StringComparer.Ordinal)];
        }

        private IReadOnlyList<AirportRecord> RecordsOf(IEnumerable<AirportCode> codes)
            => [.. codes.Where(c => input.Analysis.Airports.TryGetValue(c, out var a) && (!string.IsNullOrWhiteSpace(a.Name) || a.Position is not null))
                .Select(c => new AirportRecord(c, input.Analysis.Airports[c].Name, input.Analysis.Airports[c].Position))];

        private static IReadOnlyList<string> DesignatorsOf(PackageContent content)
            => [.. content.AircraftConfigs.Select(a => a.TypeDesignator).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

        private static BglPlacement BglOf(PackageContent content)
        {
            if (content.UnreadableBglCount > 0 || content.BglSectionTypes.Count == 0)
            {
                return BglPlacement.None;
            }

            return content.BglSectionTypes.Any(PlacingSections.Contains) ? BglPlacement.Places : BglPlacement.PlacesNothing;
        }
    }
}
