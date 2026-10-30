using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using static AddonManager.Core.Tests.Content.ContentBuilder;

namespace AddonManager.Core.Tests.Cards;

/// <summary>
/// A small machine for tests: Libraries with copies and content, a Community folder, and the board input built from them.
/// Never name a member <c>Cards</c>: the namespace of that name shadows it in test code.
/// </summary>
internal sealed class World
{
    private readonly List<PackageCopy> copies = [];
    private readonly List<PackageContent> contents = [];
    private readonly List<CommunityEntryInfo> entries = [];
    private readonly List<LibraryRoot> unreachable = [];
    private readonly List<string> inaccessible = [];
    private readonly List<string> presentOutside = [];
    private readonly List<GsxProfile> profiles = [];

    /// <summary>The time every test card is created at.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    public UserState State { get; } = new();

    /// <summary>
    /// Settles a completed scan, as the app does: remembers facts, fixes sections and places the Packages that qualify.
    /// Returns whether <see cref="Settlement.Apply"/> changed anything.
    /// </summary>
    public bool Place() => Settlement.Apply(State, Input(), Now);

    /// <summary>A card on <paramref name="section"/> and <paramref name="tab"/> holding the named Packages, which must be on no card yet.</summary>
    public CardRecord Group(Category section, CardTab tab, params string[] names)
    {
        var card = State.NewCard(section, tab, [], Now);
        foreach (var name in names)
        {
            State.SetCard(PackageKey.Folder(new PackageName(name)), card.Id);
        }

        return card;
    }

    /// <summary>Registers a Library for the given sections; with none, for all three.</summary>
    public LibraryRoot Library(string path, params Category[] sections)
    {
        var library = new LibraryRoot(path, sections.Length == 0 ? LibraryRoot.AllSections : sections);
        State.Libraries.Add(library);
        return library;
    }

    /// <summary>Re-paths <paramref name="content"/> into a Library (the first one by default), adds the copy and the content, and returns the re-pathed content.</summary>
    public PackageContent Add(PackageContent content, string? folder = null, LibraryRoot? library = null)
    {
        library ??= State.Libraries[0];
        var path = Path.Combine(library.Path, folder ?? content.Name.Value);
        var placed = content with { Path = path, Source = PackageSource.Library };
        copies.Add(new PackageCopy(content.Name, path, library, content.Manifest));
        contents.Add(placed);
        return placed;
    }

    /// <summary>A Managed junction named after the Package, pointing at its first copy.</summary>
    public void Linked(string name)
    {
        var copy = copies.First(c => c.Name == new PackageName(name));
        entries.Add(new CommunityEntryInfo(name, EntryKind.Junction, copy.Path, true, true));
    }

    /// <summary>A junction named <paramref name="name"/> to any target; it is classified like a real one.</summary>
    public void LinkTo(string name, string target) => entries.Add(new CommunityEntryInfo(name, EntryKind.Junction, target, true, true));

    public void External(PackageContent content, string target)
    {
        contents.Add(content with { Path = target, Source = PackageSource.Community });
        entries.Add(new CommunityEntryInfo(content.Name.Value, EntryKind.Junction, target, true, true));
    }

    public void Fixed(PackageContent content)
    {
        contents.Add(content with { Path = @"C:\Sim\Community\" + content.Name.Value, Source = PackageSource.Community });
        entries.Add(new CommunityEntryInfo(content.Name.Value, EntryKind.Directory, null, false, true));
    }

    public void SimManaged(PackageContent content)
        => contents.Add(content with { Path = @"C:\Sim\Official\" + content.Name.Value, Source = PackageSource.SimManaged });

    public void Ignored(string name)
        => entries.Add(new CommunityEntryInfo(name, EntryKind.Directory, null, false, false));

    public void Stray(string name)
        => entries.Add(new CommunityEntryInfo(name, EntryKind.File, null, false, false));

    /// <summary>The Package's folder can't be read: its copy stays, but the scan produced no content for it.</summary>
    public void WithoutContent(string name) => contents.RemoveAll(c => c.Name == new PackageName(name));

    /// <summary>A later scan read the Package again: the content replaces the earlier one at the same copy path.</summary>
    public void Reread(PackageContent content)
    {
        var copy = copies.First(c => c.Name == content.Name);
        contents.RemoveAll(c => c.Name == content.Name);
        contents.Add(content with { Path = copy.Path, Source = PackageSource.Library });
    }

    public void Unreachable(LibraryRoot library) => unreachable.Add(library);

    /// <summary>Discovery could not read the folder: it goes to the index's inaccessible folders.</summary>
    public void Inaccessible(string folder) => inaccessible.Add(folder);

    /// <summary>The Community entry of a Package disappears (another tool removed the link); its target, if any, stays on disk.</summary>
    public void RemoveEntry(string name) => entries.RemoveAll(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A folder outside every registered Library still exists on disk, as a completed run found when it looked for the Packages last seen there.</summary>
    public void Present(string path) => presentOutside.Add(path);

    /// <summary>Whether a run has completed and listed what exists outside the Libraries; false gives the startup board's unknown (<c>null</c>).</summary>
    public bool OutsideKnown { get; set; } = true;

    /// <summary>Whether the GSX folder exists; false gives <c>BoardInput.GsxFolderFound</c> false.</summary>
    public bool GsxFolderFound { get; set; } = true;

    /// <summary>A GSX profile file in the GSX folder; <paramref name="disabled"/> makes it a <c>.off</c> file.</summary>
    public void Profile(string name, string code, bool disabled = false)
        => profiles.Add(new GsxProfile(new PackageName(name), Code(code), $@"C:\GSX\{name}.{(disabled ? "off" : "ini")}", disabled));

    /// <summary>A profile file whose name yields no ICAO code.</summary>
    public void ProfileWithoutCode(string name)
        => profiles.Add(new GsxProfile(new PackageName(name), default, $@"C:\GSX\{name}.ini", false));

    /// <summary>The profile file was deleted.</summary>
    public void WithoutProfile(string name) => profiles.RemoveAll(p => p.Name == new PackageName(name));

    /// <summary>The Package vanished: its copies, content and Community entry are gone.</summary>
    public void Without(string name)
    {
        var package = new PackageName(name);
        copies.RemoveAll(c => c.Name == package);
        contents.RemoveAll(c => c.Name == package);

        // A vanished link target leaves a link whose target is gone (Broken); only a Fixed folder disappears with its Package.
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (!e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (e.IsLink)
            {
                entries[i] = e with { TargetExists = false, HasManifest = false };
            }
            else if (e.Kind == EntryKind.Directory && e.HasManifest)
            {
                entries.RemoveAt(i--);
            }
        }
    }

    /// <summary>
    /// Analyses the contents with the current <c>State.Content</c> and cards (<c>Reanalyze</c>). Copies under an unreachable Library are not indexed, but
    /// their cached content stays, as in a real scan.
    /// </summary>
    public BoardInput Input(bool scanComplete = true)
    {
        bool Hidden(string path) => unreachable.Any(l => PathNormalizer.IsUnder(path, l.Path));

        // A Library that is no longer registered is not scanned, so its copies are not indexed.
        bool Registered(PackageCopy c) => State.Libraries.Any(l => PathNormalizer.AreEqual(l.Path, c.Library.Path));

        var index = new PackageIndex(copies.Where(c => !Hidden(c.Path) && Registered(c)), State.Libraries, unreachable, inaccessible);
        var community = CommunityClassifier.Classify(entries, index);

        // A completed run lists what exists outside the Libraries: the Community contents and the folders named with Present. Not known before any run.
        var outside = OutsideKnown && scanComplete
            ? contents.Where(c => c.Source == PackageSource.Community).Select(c => c.Path).Concat(presentOutside)
                .Select(p => PathNormalizer.Normalize(p)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        return new BoardInput(index, community, [.. contents], ContentAnalyzer.Analyze(contents, State.Content), scanComplete) { PresentOutsideLibraries = outside, Profiles = [.. profiles], GsxFolderFound = GsxFolderFound }.Reanalyze(State);
    }
}
