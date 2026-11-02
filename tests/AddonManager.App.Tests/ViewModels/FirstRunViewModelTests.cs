using AddonManager.App.Session;
using AddonManager.App.ViewModels;
using AddonManager.Core.Community;
using AddonManager.Core.Content;

namespace AddonManager.App.Tests.ViewModels;

public sealed class FirstRunViewModelTests
{
    private readonly FirstRunFakeSession session = new();
    private string? picked;

    private FirstRunViewModel Create(Action<Action>? post = null)
        => new(session, post ?? (a => a()), () => picked);

    private static CommunityEntryInfo Junction(string name, string target)
        => new(name, EntryKind.Junction, target, true, true);

    [Fact]
    public void Adding_a_folder_updates_the_coverage()
    {
        session.Community.Add(Junction("a", @"D:\Addons\a"));
        session.Community.Add(Junction("b", @"E:\Other\b"));
        session.Community.Add(new CommunityEntryInfo("orbx", EntryKind.Junction, @"D:\Addons\x", false, false));
        var vm = Create();
        Assert.Equal("0 of 2 links covered", vm.Coverage);

        picked = @"D:\Addons";
        vm.AddSceneryCommand.Execute(null);

        Assert.Equal("1 of 2 links covered", vm.Coverage);
        Assert.Equal(["orbx"], vm.NotCounted);
        Assert.True(vm.HasNotCounted);
        Assert.Equal([@"D:\Addons"], vm.Scenery);
    }

    [Fact]
    public void Links_under_no_library_are_listed_with_their_target_until_covered()
    {
        session.Community.Add(Junction("a", @"D:\Addons\a"));
        session.Community.Add(Junction("neofly", @"C:\Spel\Neofly\addons\n"));
        var vm = Create();

        Assert.Equal(["a (D:\\Addons\\a)", @"neofly (C:\Spel\Neofly\addons\n)"], vm.NotCovered);
        Assert.True(vm.HasNotCovered);

        picked = @"D:\Addons";
        vm.AddSceneryCommand.Execute(null);

        Assert.Equal([@"neofly (C:\Spel\Neofly\addons\n)"], vm.NotCovered);

        picked = @"C:\Spel\Neofly";
        vm.AddOtherCommand.Execute(null);

        Assert.Empty(vm.NotCovered);
        Assert.False(vm.HasNotCovered);
        Assert.Equal("Not covered (no Library holds them)", FirstRunViewModel.NotCoveredTitle);
    }

    [Fact]
    public void Without_foreign_links_there_is_no_not_counted_list()
    {
        session.Community.Add(Junction("a", @"D:\Addons\a"));
        var vm = Create();

        Assert.Empty(vm.NotCounted);
        Assert.False(vm.HasNotCounted);
    }

    [Fact]
    public async Task The_same_folder_in_two_sections_is_one_library()
    {
        var vm = Create();
        picked = @"D:\Addons\";
        vm.AddSceneryCommand.Execute(null);
        vm.AddAircraftCommand.Execute(null);
        picked = @"D:\Addons";
        vm.AddSceneryCommand.Execute(null);

        Assert.Single(vm.Scenery);
        await vm.StartCommand.ExecuteAsync(null);

        var library = Assert.Single(session.Completed!);
        Assert.Equal([Category.Scenery, Category.Aircraft], library.Sections);
    }

    [Fact]
    public void A_community_folder_is_refused_with_its_reason()
    {
        var vm = Create();
        picked = @"C:\Sim\Packages\Community\x";
        vm.AddOtherCommand.Execute(null);

        Assert.Empty(vm.Other);
        Assert.NotNull(vm.LibraryRefusal);
        Assert.Contains("Community", vm.LibraryRefusal);

        picked = @"D:\Addons";
        vm.AddOtherCommand.Execute(null);
        Assert.Null(vm.LibraryRefusal);
    }

    [Fact]
    public void A_folder_can_be_removed_before_start()
    {
        var vm = Create();
        picked = @"D:\Addons";
        vm.AddAircraftCommand.Execute(null);

        vm.RemoveAircraftCommand.Execute(@"D:\Addons\");

        Assert.Empty(vm.Aircraft);
    }

    [Fact]
    public void Start_needs_a_library()
    {
        var vm = Create();
        Assert.False(vm.StartCommand.CanExecute(null));

        picked = @"D:\Addons";
        vm.AddSceneryCommand.Execute(null);
        Assert.True(vm.StartCommand.CanExecute(null));

        vm.RemoveSceneryCommand.Execute(@"D:\Addons");
        Assert.False(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Start_hands_the_combined_libraries_to_the_session()
    {
        var vm = Create();
        picked = @"D:\S";
        vm.AddSceneryCommand.Execute(null);
        picked = @"D:\A";
        vm.AddAircraftCommand.Execute(null);

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal([@"D:\S", @"D:\A"], session.Completed!.Select(l => l.Path));
    }

    [Fact]
    public async Task Libraries_keep_the_order_their_folder_was_first_added()
    {
        var vm = Create();
        picked = @"D:\A";
        vm.AddAircraftCommand.Execute(null);
        picked = @"D:\S";
        vm.AddSceneryCommand.Execute(null);

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal([@"D:\A", @"D:\S"], session.Completed!.Select(l => l.Path));
    }

    [Fact]
    public void A_picked_root_without_community_shows_the_refusal()
    {
        session.Current = new(SessionPhase.NeedsPackagesRoot, null, null, null);
        session.RootRefusal = "No Community folder here.";
        var vm = Create();
        picked = @"D:\Nope";

        vm.PickPackagesRootCommand.Execute(null);

        Assert.True(vm.NeedsPackagesRoot);
        Assert.Equal("No Community folder here.", vm.RootRefusal);
        Assert.Equal([@"D:\Nope"], session.RootsTried);
    }

    [Fact]
    public void Cancelling_the_folder_picker_changes_nothing()
    {
        var vm = Create();
        picked = null;

        vm.AddSceneryCommand.Execute(null);
        vm.PickPackagesRootCommand.Execute(null);

        Assert.Empty(vm.Scenery);
        Assert.Null(vm.LibraryRefusal);
        Assert.Null(vm.RootRefusal);
        Assert.Empty(session.RootsTried);
    }

    [Fact]
    public void Changes_are_applied_through_post()
    {
        session.Current = new(SessionPhase.NeedsPackagesRoot, null, null, null);
        var queue = new Queue<Action>();
        var vm = Create(queue.Enqueue);

        session.Publish(new SessionView(SessionPhase.FirstRun, null, null, "boom"));
        session.Publish(new SessionView(SessionPhase.FirstRun, null, null, "boom"));

        Assert.True(vm.NeedsPackagesRoot);
        Assert.Null(vm.StartError);
        Assert.Single(queue);

        queue.Dequeue()();

        Assert.False(vm.NeedsPackagesRoot);
        Assert.Equal("boom", vm.StartError);

        session.Publish(new SessionView(SessionPhase.FirstRun, null, null, null));
        Assert.Single(queue);
    }

    [Fact]
    public void Coverage_appears_once_the_root_is_picked()
    {
        session.Current = new(SessionPhase.NeedsPackagesRoot, null, null, null);
        var vm = Create();

        Assert.Null(vm.Coverage);
        Assert.Equal(0, session.ReadCommunityCalls);

        session.Publish(new SessionView(SessionPhase.FirstRun, null, null, null));

        Assert.Equal("0 of 0 links covered", vm.Coverage);
        Assert.Equal(1, session.ReadCommunityCalls);
    }

    [Fact]
    public void A_start_error_is_shown_on_the_first_run_screen()
    {
        var vm = Create();

        session.Publish(new SessionView(SessionPhase.FirstRun, null, null, "Could not save."));

        Assert.Equal("Could not save.", vm.StartError);
    }
}
