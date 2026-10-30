using AddonManager.Core.Cards;
using AddonManager.Core.Community;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.State;

namespace AddonManager.Core.Tests.Scenarios;

/// <summary>
/// Rickard's sanitised real-machine content (spec 13) as a board input: Library contents become copies of the given Libraries,
/// Community contents become a Fixed folder or an External junction, Sim-managed contents stay contents. No Managed links.
/// </summary>
internal static class RickardWorld
{
    private const string CommunityMarker = @"\Packages\Community\";

    /// <summary>Both Libraries of the machine, registered for all three sections.</summary>
    public static LibraryRoot[] AllSectionLibraries()
        =>
        [
            new(@"C:\Sim\Addons", LibraryRoot.AllSections),
            new(@"C:\Sim\Orbx Library\msfs", LibraryRoot.AllSections),
        ];

    /// <summary>
    /// The state with <paramref name="libraries"/> registered (both Libraries for all three sections by default) and the scan input,
    /// analysed with the state's cards: ready for <c>Settlement.Apply</c>. After settling, build the board from <c>input.Reanalyze(state)</c>.
    /// </summary>
    public static (UserState State, BoardInput Input) Build(IReadOnlyList<LibraryRoot>? libraries = null)
    {
        libraries ??= AllSectionLibraries();
        var state = new UserState();
        state.Libraries.AddRange(libraries);

        var contents = ContentFixtureLoader.Load("rickard-content");
        var copies = new List<PackageCopy>();
        var entries = new List<CommunityEntryInfo>();

        foreach (var content in contents)
        {
            switch (content.Source)
            {
                case PackageSource.Library:
                    var library = libraries.FirstOrDefault(l => PathNormalizer.IsUnder(content.Path, l.Path));
                    if (library is not null)
                    {
                        copies.Add(new PackageCopy(content.Name, content.Path, library, content.Manifest));
                    }

                    break;

                case PackageSource.Community when content.Path.Contains(CommunityMarker, StringComparison.OrdinalIgnoreCase):
                    entries.Add(new CommunityEntryInfo(content.Name.Value, EntryKind.Directory, null, false, true));
                    break;

                case PackageSource.Community:
                    entries.Add(new CommunityEntryInfo(content.Name.Value, EntryKind.Junction, content.Path, true, true));
                    break;
            }
        }

        // Library contents outside every given Library are not part of this machine.
        var inLibraries = contents
            .Where(c => c.Source != PackageSource.Library || libraries.Any(l => PathNormalizer.IsUnder(c.Path, l.Path)))
            .ToList();

        var index = new PackageIndex(copies, state.Libraries, []);
        var community = CommunityClassifier.Classify(entries, index);
        var input = new BoardInput(index, community, inLibraries, ContentAnalyzer.Analyze(inLibraries, state.Content), true);
        return (state, input.WithGsx(ContentFixtureLoader.LoadGsx("rickard-content")).Reanalyze(state));
    }
}
