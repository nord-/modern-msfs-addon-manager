using AddonManager.Core.Community;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using AddonManager.Core.Sync;
using static AddonManager.Core.Tests.TestData;

namespace AddonManager.Core.Tests.Sync;

public class CommunityFollowerTests
{
    private static readonly PackageName A = new("a"), B = new("b"), Unknown = new("unknown");

    private readonly PackageCopy a = Copy("a"), b = Copy("b");
    private readonly PackageIndex index;
    private readonly UserState state = new();

    public CommunityFollowerTests() => index = new PackageIndex([a, b], [SimAddons], []);

    private static CommunityEntryInfo J(string name, string target, bool exists = true) => new(name, EntryKind.Junction, target, exists, exists);

    private bool Follow(params CommunityEntryInfo[] current) => CommunityFollower.Follow(state, CommunityClassifier.Classify(current, index), index);

    [Fact]
    public void A_linked_package_is_on_and_an_unlinked_one_off()
    {
        var link = J(a.Name.Value, a.Path);
        var stray = new CommunityEntryInfo("notes.txt", EntryKind.File, null, false, false);

        Assert.True(Follow(link, stray));

        Assert.True(state.IsOn(A));
        Assert.False(state.IsOn(B));
        Assert.Equal(["a", "notes.txt"], state.Snapshot!.Select(s => s.Name));
    }

    [Fact]
    public void A_package_unlinked_by_another_tool_turns_off_and_the_snapshot_follows()
    {
        Follow(J(a.Name.Value, a.Path));

        var changed = Follow();

        Assert.True(changed);
        Assert.False(state.IsOn(A));
        Assert.Empty(state.Snapshot!);
    }

    [Fact]
    public void A_stale_link_counts_as_on()
    {
        Follow(J("a", @"C:\Sim\Old\a", exists: false));

        Assert.True(state.IsOn(A));
    }

    [Fact]
    public void Following_an_unchanged_community_reports_no_change()
    {
        var link = J(a.Name.Value, a.Path);
        Follow(link);

        Assert.False(Follow(link));
    }

    [Fact]
    public void A_package_that_is_not_indexed_keeps_its_choice()
    {
        state.SetOn(Unknown, true);

        Follow();

        Assert.True(state.IsOn(Unknown));
    }
}
