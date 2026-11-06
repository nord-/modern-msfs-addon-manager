using AddonManager.App.Session;
using AddonManager.App.ViewModels;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Core.Sync;
using static AddonManager.App.Tests.ViewModels.Boards;

namespace AddonManager.App.Tests.ViewModels;

public sealed class SyncDialogViewModelTests
{
    private static PackageName Name(string value) => new(value);

    private static SyncRow Row(string package, SyncReason reason = SyncReason.LinkAppeared, string? target = null, bool restorable = true)
        => new(Name(package), reason, target) { Restorable = restorable };

    private static (SyncDialogViewModel Vm, FakeSession Session) Create()
    {
        var session = new FakeSession();
        return (new SyncDialogViewModel(session), session);
    }

    private static Board WithNotShown(Board board, params string[] names)
        => board with { NotShown = [.. names.Select(n => new NotShownPackage(Name(n), n, null, Category.Scenery, false))] };

    [Fact]
    public void Rows_are_grouped_by_card_with_not_shown_last()
    {
        var (vm, _) = Create();
        var board = WithNotShown(Board(Card(1, "First"), Card(null, "Provisional"), Card(2, "Same", packages: ["s1"]), Card(3, "Same", packages: ["s2"])), "hidden");

        vm.Replace(new SyncPrompt(
            [Row("hidden"), Row("orphan"), Row("s2"), Row("Provisional"), Row("s1"), Row("First")],
            board));

        Assert.Equal(["First", "Provisional", "Same", "Same", "No card yet", "Not shown"], vm.Groups.Select(g => g.Title));
        Assert.Equal(["s1"], vm.Groups[2].Rows.Select(r => r.Row.Package.Value));
        Assert.Equal(["s2"], vm.Groups[3].Rows.Select(r => r.Row.Package.Value));
        Assert.Equal("orphan", vm.Groups[4].Rows[0].DisplayName);
    }

    [Fact]
    public void Apply_sends_choices_keyed_by_package_and_reason()
    {
        var (vm, session) = Create();
        vm.Replace(new SyncPrompt([Row("a"), Row("b", SyncReason.OffButLinked)], Board(Card(1, "a"), Card(2, "b"))));
        vm.Groups[1].Rows[0].Choice = SyncChoice.RestoreApp;

        vm.ApplyCommand.Execute(null);

        var sent = Assert.Single(session.Resolved);
        Assert.Equal(SyncChoice.KeepCommunity, sent[new SyncKey(Name("a"), SyncReason.LinkAppeared)]);
        Assert.Equal(SyncChoice.RestoreApp, sent[new SyncKey(Name("b"), SyncReason.OffButLinked)]);
    }

    [Fact]
    public void Every_row_offers_keep_or_restore()
    {
        var (vm, _) = Create();
        var rows = Enum.GetValues<SyncReason>().Select(r => Row("p" + (int)r, r, @"D:\New")).ToList();
        vm.Replace(new SyncPrompt(rows, Board()));

        var all = vm.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Equal(6, all.Count);
        Assert.All(all, r =>
        {
            Assert.Equal(SyncChoice.KeepCommunity, r.Choice);
            Assert.True(r.CanRestore);
        });
        Assert.Equal(@"Off in the app, but linked; moved to D:\New", all.Single(r => r.Row.Reason == SyncReason.Moved).Change);
        Assert.Equal(@"Link now points to D:\New", all.Single(r => r.Row.Reason == SyncReason.LinkRetargeted).Change);
        Assert.Equal("Linked outside the app", all.Single(r => r.Row.Reason == SyncReason.LinkAppeared).Change);
        Assert.Equal("Link removed outside the app", all.Single(r => r.Row.Reason == SyncReason.LinkDisappeared).Change);
        Assert.Equal("On in the app, but not linked", all.Single(r => r.Row.Reason == SyncReason.OnButNotLinked).Change);
        Assert.Equal("Off in the app, but linked", all.Single(r => r.Row.Reason == SyncReason.OffButLinked).Change);
    }

    [Fact]
    public void A_row_that_cannot_be_restored_offers_only_keep()
    {
        var (vm, _) = Create();
        vm.Replace(new SyncPrompt([Row("a", restorable: false)], Board()));

        Assert.False(vm.Groups[0].Rows[0].CanRestore);
    }

    [Fact]
    public void SetAll_skips_rows_that_cannot_be_restored()
    {
        var (vm, _) = Create();
        vm.Replace(new SyncPrompt([Row("a"), Row("b", restorable: false)], Board()));

        vm.SetAll(SyncChoice.RestoreApp);

        var rows = vm.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Equal(SyncChoice.RestoreApp, rows.Single(r => r.Row.Package.Value == "a").Choice);
        Assert.Equal(SyncChoice.KeepCommunity, rows.Single(r => r.Row.Package.Value == "b").Choice);

        vm.SetAll(SyncChoice.KeepCommunity);
        Assert.All(rows, r => Assert.Equal(SyncChoice.KeepCommunity, r.Choice));
    }

    [Fact]
    public void New_rows_keep_the_choices_of_rows_still_listed()
    {
        var (vm, _) = Create();
        var board = Board(Card(1, "a"), Card(2, "b"), Card(3, "c"));
        vm.Replace(new SyncPrompt([Row("a"), Row("b"), Row("c")], board));
        vm.SetAll(SyncChoice.RestoreApp);

        vm.Replace(new SyncPrompt([Row("a"), Row("b", SyncReason.OffButLinked), Row("d")], board));

        var rows = vm.Groups.SelectMany(g => g.Rows).ToDictionary(r => r.Row.Package.Value);
        Assert.Equal(SyncChoice.RestoreApp, rows["a"].Choice);
        Assert.Equal(SyncChoice.KeepCommunity, rows["b"].Choice);
        Assert.Equal(SyncChoice.KeepCommunity, rows["d"].Choice);
        Assert.False(rows.ContainsKey("c"));
    }

    [Fact]
    public void Apply_sends_every_choice_and_shows_a_refusal()
    {
        var (vm, session) = Create();
        session.ResolveMessage = "Close MSFS to change add-ons";
        vm.Replace(new SyncPrompt([Row("a"), Row("b")], Board()));

        vm.ApplyCommand.Execute(null);

        Assert.Equal(2, Assert.Single(session.Resolved).Count);
        Assert.Equal("Close MSFS to change add-ons", vm.Message);
    }

    [Fact]
    public void A_row_shows_the_folder_name_beside_the_display_name()
    {
        var (vm, _) = Create();
        vm.Replace(new SyncPrompt([Row("pkg-a")], Board(Card(1, "Nice name", packages: ["pkg-a"]))));

        var row = vm.Groups[0].Rows[0];
        Assert.Equal("pkg-a", row.FolderName);
    }

    [Fact]
    public async Task Apply_clears_the_previous_message_when_it_starts()
    {
        var (vm, session) = Create();
        session.ResolveMessage = "refused";
        vm.Replace(new SyncPrompt([Row("a")], Board()));
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Equal("refused", vm.Message);

        session.ResolveMessage = null;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Null(vm.Message);
    }
}
