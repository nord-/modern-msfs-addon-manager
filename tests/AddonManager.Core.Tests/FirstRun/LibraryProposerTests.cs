using AddonManager.Core.Community;
using AddonManager.Core.FirstRun;
using AddonManager.Core.Tests.Fakes;

namespace AddonManager.Core.Tests.FirstRun;

public class LibraryProposerTests
{
    private static InMemoryFolderTree Tree(string[] packages, string[]? emptyFolders = null) => new(packages, emptyFolders);

    private static CommunityEntryInfo Junction(string target, bool targetExists = true)
        => new(Path.GetFileName(target.TrimEnd('\\')), EntryKind.Junction, target, targetExists, targetExists);

    private static CommunityEntryInfo Symlink(string target) => new(Path.GetFileName(target), EntryKind.Symlink, target, true, true);

    [Fact]
    public void Unlinked_sibling_folders_with_packages_still_merge_up_to_the_library_root()
    {
        var tree = Tree(packages: [@"C:\Sim\Addons\Scenery\Europe\a", @"C:\Sim\Addons\Scenery\Australia\b", @"C:\Sim\Addons\Aircraft\c"],
                        emptyFolders: [@"C:\Sim\Tool"]);

        var result = LibraryProposer.Propose([Junction(@"C:\Sim\Addons\Scenery\Europe\a"), Junction(@"C:\Sim\Addons\Aircraft\c")], tree);

        Assert.Equal([new LibraryProposal(@"C:\Sim\Addons", false)], result);
    }

    [Fact]
    public void Tool_roots_are_proposed_directly()
    {
        const string target = @"C:\Sim\Orbx Library\msfs\ESSA Stockholm Arlanda Airport\Community\orbx-airport-essa-stockholm-arlanda\";

        Assert.Equal([new LibraryProposal(@"C:\Sim\Orbx Library\msfs", true)], LibraryProposer.Propose([Junction(target)], Tree(packages: [target])));
    }

    [Fact]
    public void A_junction_into_the_FSDT_folder_proposes_nothing()
    {
        // FSDT's Addon Manager owns its own junctions; its folder holds a huge backup tree and is never a Library.
        const string target = @"C:\Sim\FSDT Addon Manager\MSFS\fsdreamteam-gsx-world-of-jetways-2020\";
        var tree = Tree(packages: [target, @"C:\Sim\FSDT Addon Manager\MSFS\other"]);

        Assert.Empty(LibraryProposer.Propose([Junction(target)], tree));
    }

    [Fact]
    public void Symlinks_and_missing_targets_are_not_used()
    {
        var entries = new[]
        {
            Symlink(@"C:\Users\user\AppData\Roaming\SayIntentionsAI\SayIntentionsAI\simobjects\SayIntentions-SimObjects-Optional"),
            Junction(@"C:\Sim\Gone\pkg", targetExists: false),
        };

        Assert.Empty(LibraryProposer.Propose(entries, Tree(packages: [entries[0].Target!])));
    }

    [Fact]
    public void Merging_stops_below_a_folder_with_a_package_free_subfolder()
    {
        var tree = Tree(packages: [@"C:\Sim\Addons\Scenery\a", @"C:\Sim\Addons\Aircraft\c", @"C:\Sim\Neofly\addons\n"],
                        emptyFolders: [@"C:\Sim\Tool\logs"]);

        var single = LibraryProposer.Propose([Junction(@"C:\Sim\Addons\Scenery\a")], tree).Single();

        Assert.Equal(@"C:\Sim\Addons", single.Path);
    }

    [Fact]
    public void A_folder_whose_subfolders_are_packages_still_merges_upward()
    {
        // SKIATHOS V1.2 holds three Packages directly; it must not become a nested Library.
        var tree = Tree(packages:
        [
            @"C:\Sim\Addons\Scenery\Europe\SKIATHOS V1.2\lgsk-a", @"C:\Sim\Addons\Scenery\Europe\SKIATHOS V1.2\lgsk-b",
            @"C:\Sim\Addons\Scenery\Europe\SKIATHOS V1.2\lgsk-c", @"C:\Sim\Addons\Aircraft\c",
        ], emptyFolders: [@"C:\Sim\Tool"]);

        var result = LibraryProposer.Propose([Junction(@"C:\Sim\Addons\Scenery\Europe\SKIATHOS V1.2\lgsk-a"), Junction(@"C:\Sim\Addons\Aircraft\c")], tree);

        Assert.Equal([new LibraryProposal(@"C:\Sim\Addons", false)], result);
    }

    [Fact]
    public void Merging_stops_below_a_tool_root_and_the_drive_root()
    {
        var tree = Tree(packages: [@"D:\Addons\a", @"D:\Addons\b"]);

        Assert.Equal([new LibraryProposal(@"D:\Addons", false)], LibraryProposer.Propose([Junction(@"D:\Addons\a")], tree));
    }

    [Fact]
    public void Proposals_inside_another_proposal_are_dropped_and_results_ordered()
    {
        // Merging from x stops at Inner (Outer has an empty subfolder); y starts at Outer, which contains Inner.
        var tree = Tree(packages: [@"C:\Outer\Inner\x", @"C:\Outer\y", @"C:\Sim\Neofly\addons\n"], emptyFolders: [@"C:\Outer\empty"]);

        var result = LibraryProposer.Propose([Junction(@"C:\Outer\y"), Junction(@"C:\Outer\Inner\x"), Junction(@"C:\Sim\Neofly\addons\n")], tree);

        Assert.Equal([new LibraryProposal(@"C:\Outer", false), new LibraryProposal(@"C:\Sim\Neofly\addons", true)], result);
    }

    [Fact]
    public void A_target_directly_under_a_drive_root_proposes_nothing()
    {
        var tree = Tree(packages: [@"D:\a", @"D:\Other\b"]);

        Assert.Equal([new LibraryProposal(@"D:\Other", false)], LibraryProposer.Propose([Junction(@"D:\a"), Junction(@"D:\Other\b")], tree));
    }
}
