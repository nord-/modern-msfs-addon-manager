using AddonManager.Core.Community;
using AddonManager.Core.FirstRun;
using AddonManager.Core.Packages;

namespace AddonManager.Core.Tests.Scenarios;

/// <summary>First run against a sanitised copy of Rickard's Community and Library tree (2026-10).</summary>
public class FirstRunScenarioTests
{
    private static readonly LibraryRoot[] Libraries =
    [
        new(@"C:\Sim\Addons"), new(@"C:\Sim\FSDT Addon Manager\MSFS"), new(@"C:\Sim\Neofly\addons"), new(@"C:\Sim\Orbx Library\msfs"),
    ];

    private readonly (IReadOnlyList<CommunityEntryInfo> Community, Fakes.InMemoryFolderTree Tree, IReadOnlyList<PackageCopy> Packages) f
        = FixtureLoader.Load("rickard-2026-10");

    private readonly PackageIndex index;

    public FirstRunScenarioTests() => index = new PackageIndex(f.Packages, Libraries);

    private string? CommunityTarget(string name)
        => f.Community.SingleOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))?.Target;

    [Fact]
    public void Proposes_the_four_real_library_roots()
        => Assert.Equal(
            [
                new LibraryProposal(@"C:\Sim\Addons", false), new LibraryProposal(@"C:\Sim\FSDT Addon Manager\MSFS", true),
                new LibraryProposal(@"C:\Sim\Neofly\addons", true), new LibraryProposal(@"C:\Sim\Orbx Library\msfs", true),
            ],
            LibraryProposer.Propose(f.Community, f.Tree));

    [Fact]
    public void Classifies_the_real_community()
    {
        var counts = CommunityClassifier.Classify(f.Community, index).GroupBy(e => e.Class).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(95, counts[EntryClass.Managed]);
        Assert.Equal(2, counts[EntryClass.External]);
        Assert.Equal(1, counts[EntryClass.Broken]);
        Assert.Equal(16, counts[EntryClass.Fixed]);
        Assert.Equal(3, counts[EntryClass.Ignored]);
        Assert.Equal(1, counts[EntryClass.Stray]);
        Assert.Equal(118, f.Community.Count);
    }

    [Fact]
    public void Finds_the_four_neofly_duplicate_copies_with_the_neofly_copy_active()
    {
        var dupes = index.Names.Where(index.IsDuplicateCopy).Select(n => n.Value).Order(StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal(["bagolu-c152-neoflybannertow", "neofly-neofly-cargo", "neofly-neofly-objects", "neofly-neofly-smokes"], dupes, StringComparer.OrdinalIgnoreCase);
        Assert.All(dupes, d => Assert.StartsWith(@"C:\Sim\Neofly\addons",
            ActiveCopySelector.Select(new(d), index, CommunityTarget(d), null)!.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Fixture_packages_per_root_match_the_export()
    {
        var perRoot = f.Packages.GroupBy(p => p.Library.Path).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(228, perRoot[@"C:\Sim\Addons"]);
        Assert.Equal(12, perRoot[@"C:\Sim\Orbx Library\msfs"]);
        Assert.Equal(4, perRoot[@"C:\Sim\FSDT Addon Manager\MSFS"]);
        Assert.Equal(5, perRoot[@"C:\Sim\Neofly\addons"]);
    }
}
