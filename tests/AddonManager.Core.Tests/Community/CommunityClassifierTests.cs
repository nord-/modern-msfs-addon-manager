using AddonManager.Core.Community;
using AddonManager.Core.Packages;
using static AddonManager.Core.Tests.TestData;

namespace AddonManager.Core.Tests.Community;

public class CommunityClassifierTests
{
    private static readonly LibraryRoot Unplugged = new(@"E:\Addons", LibraryRoot.AllSections);

    private static CommunityEntryInfo Link(string name, string target, bool exists = true, bool manifest = true)
        => new(name, EntryKind.Junction, target, exists, exists && manifest);

    private static IReadOnlyList<ClassifiedEntry> Classify(IReadOnlyList<CommunityEntryInfo> entries, IReadOnlyList<PackageCopy> copies)
        => CommunityClassifier.Classify(entries, new PackageIndex(copies, [SimAddons, Neofly, Unplugged], [Unplugged]));

    [Theory]
    [InlineData(EntryKind.File, false, EntryClass.Stray)]
    [InlineData(EntryKind.Directory, true, EntryClass.Fixed)]
    [InlineData(EntryKind.Directory, false, EntryClass.Ignored)]
    public void Classifies_non_links(EntryKind kind, bool manifest, EntryClass expected)
        => Assert.Equal(expected, Classify([new CommunityEntryInfo("x", kind, null, false, manifest)], []).Single().Class);

    [Fact]
    public void Link_into_a_library_package_is_Managed_regardless_of_case_and_trailing_backslash()
    {
        var copy = Copy("Asobo_C172sp_AS1000", @"C:\Sim\Addons\Aircraft\Asobo_C172sp_AS1000");
        var e = Link("asobo_c172sp_as1000", @"c:\sim\addons\aircraft\ASOBO_C172SP_AS1000\");

        var result = Classify([e], [copy]).Single();

        Assert.Equal(EntryClass.Managed, result.Class);
        Assert.Equal(copy, result.Package);
    }

    [Fact]
    public void Missing_target_with_known_name_is_Stale_and_points_to_the_found_copy()
    {
        var newCopy = Copy("lgsk-airport", @"C:\Sim\Addons\Scenery\Europe\lgsk-airport");
        var e = Link("lgsk-airport", @"C:\Sim\Addons\Scenery\lgsk-airport", exists: false);

        var result = Classify([e], [newCopy]).Single();

        Assert.Equal(EntryClass.Stale, result.Class);
        Assert.Equal(newCopy, result.Package);
    }

    [Fact]
    public void Missing_target_under_unreachable_library_is_Offline_not_Broken()
    {
        var e = Link("pkg", @"E:\Addons\pkg", exists: false);

        Assert.Equal(EntryClass.Offline, Classify([e], []).Single().Class);
    }

    [Fact]
    public void Link_to_a_library_package_under_another_name_is_External()
    {
        var copyX = Copy("x");
        var e = Link("zzz-x", copyX.Path);

        Assert.Equal(EntryClass.External, Classify([e], [copyX]).Single().Class);
    }

    [Fact]
    public void Link_with_a_volume_guid_target_is_never_Broken()
    {
        var e = Link("pkg", @"\\?\Volume{0b1c2d3e-0000-0000-0000-000000000000}\pkg", exists: false);

        Assert.Equal(EntryClass.External, Classify([e], []).Single().Class);
    }

    [Fact]
    public void Missing_target_with_unknown_name_is_Broken()
        => Assert.Equal(EntryClass.Broken, Classify([Link("gone", @"C:\Sim\Old\gone", exists: false)], []).Single().Class);

    [Fact]
    public void Link_outside_libraries_to_a_package_is_External()
        => Assert.Equal(EntryClass.External,
            Classify([Link("sayintentions", @"C:\Users\user\AppData\Roaming\SayIntentionsAI\sayintentions")], []).Single().Class);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_link_into_a_folder_discovery_could_not_read_is_offline(bool targetExists)
    {
        var index = new PackageIndex([], [SimAddons], null, [@"C:\Sim\Addons\Locked"]);
        var e = Link("x", @"C:\Sim\Addons\Locked\x", targetExists);

        Assert.Equal(EntryClass.Offline, CommunityClassifier.Classify([e], index).Single().Class);
    }

    [Fact]
    public void Link_to_a_folder_without_manifest_is_Ignored()
        => Assert.Equal(EntryClass.Ignored, Classify([Link("x", @"C:\Sim\Addons\x", manifest: false)], []).Single().Class);
}
