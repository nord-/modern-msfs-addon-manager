using AddonManager.Core.Community;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using AddonManager.Core.State;
using static AddonManager.Core.Tests.TestData;

namespace AddonManager.Core.Tests.Linking;

public class LinkPlannerTests
{
    private const string CommunityPath = @"C:\MSFS\Community";
    private static readonly LibraryRoot Unplugged = new(@"E:\Addons", LibraryRoot.AllSections);
    private static readonly LibraryRoot Fsdt = new(@"C:\Sim\FSDT Addon Manager\MSFS", LibraryRoot.AllSections);

    private static readonly PackageName X = new("x");

    private readonly PackageCopy x = Copy("x"), xNeofly = Copy("x", lib: Neofly), locked = Copy("lk", lib: Fsdt);
    private readonly PackageIndex index;
    private readonly UserState state = new();
    private readonly List<CommunityEntryInfo> community = [];

    public LinkPlannerTests() => index = new PackageIndex([x, xNeofly, locked], [SimAddons, Neofly, Fsdt, Unplugged], [Unplugged]);

    private static CommunityEntryInfo J(string name, string target, bool exists = true) => new(name, EntryKind.Junction, target, exists, exists);

    private LinkPlan Plan(Dictionary<PackageName, DesiredLink> desired)
        => LinkPlanner.Plan(desired, state, index, CommunityClassifier.Classify(community, index), CommunityPath);

    private LinkPlan Plan(PackageName name, bool on) => Plan(new() { [name] = new DesiredLink(on) });

    [Fact]
    public void On_without_entry_creates_and_off_without_entry_is_noop()
    {
        Assert.Equal([new LinkOp(LinkOpKind.Create, X, @"C:\MSFS\Community\x", x.Path)], Plan(X, true).Ops);

        var off = Plan(X, false);
        Assert.Empty(off.Ops);
        Assert.Equal([X], off.NoOp);
    }

    [Fact]
    public void On_when_already_linked_to_the_active_copy_is_noop_and_off_removes()
    {
        community.Add(J("x", x.Path));

        Assert.Equal([X], Plan(X, true).NoOp);
        Assert.Equal([new LinkOp(LinkOpKind.Remove, X, @"C:\MSFS\Community\x", null)], Plan(X, false).Ops);
    }

    [Fact]
    public void Managed_entry_pointing_at_another_copy_is_repointed()
    {
        community.Add(J("x", xNeofly.Path));
        state.SetOn(X, true, x.Path);

        var op = Plan(X, true).Ops.Single();

        Assert.Equal(LinkOpKind.Repoint, op.Kind);
        Assert.Equal(x.Path, op.TargetPath);
    }

    [Fact]
    public void Stale_entry_is_repointed_when_on_and_removed_when_off()
    {
        community.Add(J("x", @"C:\Sim\Addons\Old\x", exists: false));

        Assert.Equal(LinkOpKind.Repoint, Plan(X, true).Ops.Single().Kind);
        Assert.Equal(LinkOpKind.Remove, Plan(X, false).Ops.Single().Kind);
    }

    [Theory]
    [InlineData(EntryClass.Fixed)]
    [InlineData(EntryClass.External)]
    [InlineData(EntryClass.Ignored)]
    [InlineData(EntryClass.Stray)]
    public void Name_held_by_a_foreign_entry_is_NameTaken_on_and_NoOp_off(EntryClass heldBy)
    {
        community.Add(heldBy switch
        {
            EntryClass.Fixed => new CommunityEntryInfo("x", EntryKind.Directory, null, false, true),
            EntryClass.External => J("x", @"C:\Other\x"),
            EntryClass.Ignored => new CommunityEntryInfo("x", EntryKind.Directory, null, false, false),
            _ => new CommunityEntryInfo("x", EntryKind.File, null, false, false),
        });

        var on = Plan(X, true);
        Assert.Empty(on.Ops);
        Assert.Equal(new Rejection(X, RejectReason.NameTaken, heldBy), on.Rejections.Single());

        var off = Plan(X, false);
        Assert.Empty(off.Ops);
        Assert.Equal([X], off.NoOp);
    }

    [Fact]
    public void Copy_override_is_used_without_touching_state()
    {
        var plan = Plan(new() { [X] = new DesiredLink(true, xNeofly.Path) });

        Assert.Equal(xNeofly.Path, plan.Ops.Single().TargetPath);
        Assert.Null(state.Packages.GetValueOrDefault(X)?.ActiveCopyPath);
    }

    [Fact]
    public void A_package_in_a_library_another_tool_also_links_from_can_be_switched()
    {
        var lk = new PackageName("lk");
        community.Add(J("lk", locked.Path));

        var plan = Plan(lk, false);

        Assert.Equal([new LinkOp(LinkOpKind.Remove, lk, @"C:\MSFS\Community\lk", null)], plan.Ops);
        Assert.Empty(plan.Rejections);
    }

    [Fact]
    public void A_copy_override_for_a_name_held_by_an_external_entry_is_rejected_also_when_off()
    {
        community.Add(J("x", @"C:\Elsewhere\x"));

        var rejection = Plan(new() { [X] = new DesiredLink(false, xNeofly.Path) }).Rejections.Single();

        Assert.Equal(new Rejection(X, RejectReason.NameTaken, EntryClass.External), rejection);
    }

    [Fact]
    public void Offline_packages_are_rejected()
    {
        state.SetOn(X, true, @"E:\Addons\x");
        Assert.Equal(RejectReason.Offline, Plan(X, false).Rejections.Single().Reason);

        state.Forget(PackageKey.Folder(X));
        community.Add(J("x", @"E:\Addons\x", exists: false));
        Assert.Equal(RejectReason.Offline, Plan(X, true).Rejections.Single().Reason);
    }

    [Fact]
    public void On_without_a_copy_is_NotInLibrary()
        => Assert.Equal(RejectReason.NotInLibrary, Plan(new PackageName("missing"), true).Rejections.Single().Reason);

    [Fact]
    public void Entry_names_match_case_insensitively()
    {
        community.Add(J("X", x.Path));

        Assert.Equal([X], Plan(new PackageName("X"), true).NoOp);
        Assert.Equal(@"C:\MSFS\Community\x", Plan(X, false).Ops.Single().LinkPath);
    }

    [Fact]
    public void Offline_wins_over_a_reachable_duplicate_so_a_pending_change_waits_for_the_library()
    {
        // The stored copy is on the unplugged drive; a reachable duplicate in another Library must not be linked instead.
        var lk = new PackageName("lk");
        state.SetOn(lk, true, @"E:\Addons\lk");

        Assert.Equal(RejectReason.Offline, Plan(lk, true).Rejections.Single().Reason);
    }

    [Fact]
    public void A_copy_override_that_is_not_a_copy_of_the_package_is_rejected_on_or_off()
    {
        Assert.Equal(RejectReason.NotInLibrary, Plan(new() { [X] = new DesiredLink(true, locked.Path) }).Rejections.Single().Reason);
        Assert.Equal(RejectReason.NotInLibrary, Plan(new() { [X] = new DesiredLink(false, locked.Path) }).Rejections.Single().Reason);
        Assert.Equal(RejectReason.NotInLibrary, Plan(new() { [X] = new DesiredLink(false, @"C:\Sim\Addons\nowhere\x") }).Rejections.Single().Reason);
    }
}
