using AddonManager.App.Session;
using AddonManager.App.ViewModels;
using AddonManager.Core.Cards;
using AddonManager.Core.Packages;
using static AddonManager.App.Tests.ViewModels.Boards;

namespace AddonManager.App.Tests.ViewModels;

public sealed class CardDialogViewModelTests
{
    private const string SimText = "Close MSFS to change add-ons";

    private static CardPackage With(string name, bool on = true, bool canSwitch = true, string? reason = null, bool removable = false, string[]? copies = null)
        => Package(name) with { On = on, CanSwitch = canSwitch, DisabledReason = reason, Removable = removable, Copies = copies ?? [] };

    private static Card CardOf(long? id, string name, params CardPackage[] packages)
        => Card(id, name, codes: ["EHAM"]) with { Packages = packages };

    private static Board BoardOf(Card card) => Board(card);

    private static CardDialogViewModel Dialog(FakeSession session, Card card, Func<string, bool>? confirm = null)
        => new(session, BoardOf(card), CardRef.Of(card), confirm ?? (_ => true));

    [Fact]
    public void Rows_show_the_package_facts_and_the_reason()
    {
        var dup = With("dup", copies: [@"C:\A\dup", @"C:\B\dup"]) with { Version = "1.2", Library = new LibraryRoot(@"C:\Lib", LibraryRoot.AllSections) };
        var gone = With("gone", on: false, canSwitch: false, reason: "Not found", removable: true);
        var vm = Dialog(new FakeSession(), CardOf(1, "Schiphol", dup, gone));

        Assert.Equal("Schiphol", vm.Title);
        Assert.Equal("EHAM", vm.Codes);
        var first = vm.Rows[0];
        Assert.Equal(@"C:\Lib\dup", first.Path);
        Assert.Equal("dup", first.FolderName);
        Assert.Equal("1.2", first.Version);
        Assert.Equal(@"C:\Lib", first.Library);
        Assert.Equal(@"C:\Lib\dup", first.ActiveCopy);
        Assert.Null(first.Reason);
        Assert.False(first.CanRemove);
        var second = vm.Rows[1];
        Assert.Null(second.ActiveCopy);
        Assert.Null(second.Library);
        Assert.Equal("Not found", second.Reason);
        Assert.True(second.CanRemove);
    }

    [Fact]
    public void A_card_without_codes_has_null_codes()
    {
        var card = Card(1, "X") with { Codes = [] };

        Assert.Null(Dialog(new FakeSession(), card).Codes);
    }

    [Fact]
    public void The_switch_shows_the_switchable_packages_only()
    {
        var vm = Dialog(new FakeSession(), CardOf(1, "Mixed", With("a"), With("locked", on: false, canSwitch: false), With("gone", on: false, canSwitch: false)));

        Assert.True(vm.SwitchOn);
        Assert.True(vm.CanToggle);
        Assert.Null(vm.SwitchReason);
    }

    [Fact]
    public void A_card_without_switchable_packages_disables_the_switch()
    {
        var vm = Dialog(new FakeSession(), CardOf(1, "Locked", With("a", on: false, canSwitch: false)));

        Assert.False(vm.SwitchOn);
        Assert.False(vm.CanToggle);
        Assert.Equal("No package on this card can be switched", vm.SwitchReason);
    }

    [Fact]
    public void While_the_sim_runs_the_sim_text_wins()
    {
        var vm = Dialog(new FakeSession { IsSimRunning = true }, CardOf(1, "Locked", With("a", on: false, canSwitch: false)));

        Assert.False(vm.CanToggle);
        Assert.Equal(SimText, vm.SwitchReason);
    }

    [Fact]
    public async Task A_click_refused_by_the_sim_keeps_the_switch_disabled()
    {
        var session = new FakeSession { SwitchMessage = SimText };
        var vm = Dialog(session, CardOf(1, "A", With("a")));

        await vm.ToggleCommand.ExecuteAsync(null);
        vm.Update(BoardOf(CardOf(1, "A", With("a", on: false))));

        Assert.Equal(SimText, vm.Message);
        Assert.Equal(SimText, vm.SwitchReason);
        Assert.False(vm.CanToggle);
        Assert.False(vm.ToggleCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_message_clears_at_the_next_command()
    {
        var session = new FakeSession { SwitchMessage = "Could not change a: locked" };
        var vm = Dialog(session, CardOf(1, "A", With("a"), With("gone", on: false, canSwitch: false, removable: true)));
        await vm.ToggleCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Message);

        string? seen = "unset";
        session.OnSwitch = _ => seen = vm.Message;
        session.SwitchMessage = null;
        await vm.ToggleCommand.ExecuteAsync(null);
        Assert.Null(seen);
        Assert.Null(vm.Message);

        session.SwitchMessage = "Could not change a: locked";
        await vm.ToggleCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Message);
        await vm.RemoveCommand.ExecuteAsync(vm.Rows[1]);
        Assert.Null(vm.Message);
    }

    [Fact]
    public async Task A_failed_switch_shows_its_message()
    {
        var session = new FakeSession { SwitchMessage = "Could not change a: locked" };
        var vm = Dialog(session, CardOf(1, "A", With("a")));

        await vm.ToggleCommand.ExecuteAsync(null);

        Assert.Equal("Could not change a: locked", vm.Message);
        Assert.True(vm.CanToggle);
        Assert.Null(vm.SwitchReason);
        Assert.Equal([new CardRef(1, null)], session.Switched);
    }

    [Theory]
    [InlineData(SimText)]
    [InlineData("Could not change a: locked")]
    public async Task A_refused_click_raises_SwitchOn_so_the_switch_flips_back(string message)
    {
        var session = new FakeSession { SwitchMessage = message };
        var vm = Dialog(session, CardOf(1, "A", With("a")));
        var raised = 0;
        vm.PropertyChanged += (_, e) => raised += e.PropertyName == nameof(CardDialogViewModel.SwitchOn) ? 1 : 0;

        await vm.ToggleCommand.ExecuteAsync(null);

        Assert.True(raised >= 1);
        Assert.True(vm.SwitchOn);
    }

    [Fact]
    public async Task A_successful_click_raises_SwitchOn_too()
    {
        var vm = Dialog(new FakeSession(), CardOf(1, "A", With("a")));
        var raised = 0;
        vm.PropertyChanged += (_, e) => raised += e.PropertyName == nameof(CardDialogViewModel.SwitchOn) ? 1 : 0;

        await vm.ToggleCommand.ExecuteAsync(null);

        Assert.True(raised >= 1);
    }

    [Fact]
    public void The_dialog_follows_a_provisional_card_into_its_stored_card()
    {
        var provisional = Card(null, "pkg-a", packages: ["pkg-a"]);
        var vm = Dialog(new FakeSession(), provisional);

        var stored = Card(7, "Stored name", packages: ["pkg-a"]);
        Assert.True(vm.Update(BoardOf(stored)));

        Assert.Equal("Stored name", vm.Title);
    }

    [Fact]
    public void Update_returns_false_when_the_card_is_gone()
    {
        var vm = Dialog(new FakeSession(), CardOf(1, "A", With("a")));
        var closed = 0;
        vm.Closed += () => closed++;

        Assert.False(vm.Update(Board()));
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task Remove_asks_first_and_does_nothing_on_no()
    {
        var session = new FakeSession();
        var asked = new List<string>();
        var vm = Dialog(session, CardOf(1, "A", With("gone", on: false, canSwitch: false, removable: true)), text => { asked.Add(text); return false; });

        await vm.RemoveCommand.ExecuteAsync(vm.Rows[0]);

        Assert.Equal(["Remove gone and forget it?"], asked);
        Assert.Empty(session.Removed);

        var yes = Dialog(session, CardOf(1, "A", With("gone", on: false, canSwitch: false, removable: true)));
        await yes.RemoveCommand.ExecuteAsync(yes.Rows[0]);
        var removed = Assert.Single(session.Removed);
        Assert.Equal(PackageKey.Folder(new PackageName("gone")), removed.Package);
    }

    [Fact]
    public async Task A_failed_remove_shows_its_message()
    {
        var session = new FakeSession { RemoveMessage = "Could not remove gone: in use" };
        var vm = Dialog(session, CardOf(1, "A", With("gone", on: false, canSwitch: false, removable: true)));

        await vm.RemoveCommand.ExecuteAsync(vm.Rows[0]);

        Assert.Equal("Could not remove gone: in use", vm.Message);
        Assert.True(vm.RemoveCommand.CanExecute(vm.Rows[0]));
    }

    [Fact]
    public async Task Remove_is_offered_only_on_removable_rows_and_not_while_the_sim_runs()
    {
        var card = CardOf(1, "A", With("ok"), With("gone", on: false, canSwitch: false, removable: true));
        var vm = Dialog(new FakeSession(), card);
        Assert.False(vm.RemoveCommand.CanExecute(vm.Rows[0]));
        Assert.True(vm.RemoveCommand.CanExecute(vm.Rows[1]));

        var running = Dialog(new FakeSession { IsSimRunning = true }, card);
        Assert.All(running.Rows, r => Assert.False(running.RemoveCommand.CanExecute(r)));

        var refused = Dialog(new FakeSession { RemoveMessage = SimText }, card);
        await refused.RemoveCommand.ExecuteAsync(refused.Rows[1]);
        Assert.All(refused.Rows, r => Assert.False(refused.RemoveCommand.CanExecute(r)));
    }
}
