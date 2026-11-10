using System.Windows.Media;
using AddonManager.App.Session;
using AddonManager.App.ViewModels;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Gsx;
using AddonManager.Core.Packages;
using static AddonManager.App.Tests.ViewModels.Boards;

namespace AddonManager.App.Tests.ViewModels;

public sealed class CardDialogViewModelTests
{
    private const string SimText = "Close MSFS to change add-ons";
    private const string Locked = "userdata.db is locked";

    private static CardPackage With(string name, bool on = true, bool canSwitch = true, string? reason = null, bool removable = false, string[]? copies = null)
        => Package(name) with { On = on, CanSwitch = canSwitch, DisabledReason = reason, Removable = removable, Copies = copies ?? [] };

    private static CardPackage Profile(string name) => Package(name) with { Kind = PackageKind.GsxProfile, Lock = LockReason.GsxProfile };

    private static Card CardOf(long? id, string name, params CardPackage[] packages)
        => Card(id, name, codes: ["EHAM"]) with { Packages = packages };

    private static Board BoardOf(Card card) => Board(card);

    private static DialogServices Services(
        Func<string, string, bool>? confirm = null,
        Func<string?, Task<ImageSource?>>? load = null,
        Func<string?>? pick = null,
        Action<string>? open = null)
        => new(confirm ?? ((_, _) => true), load ?? (_ => Task.FromResult<ImageSource?>(null)), pick ?? (() => null), open ?? (_ => { }));

    private static CardDialogViewModel Dialog(
        FakeSession session,
        Card card,
        Func<string, string, bool>? confirm = null,
        DialogServices? services = null,
        Action<string>? notice = null,
        Action? changeStarting = null)
        => Dialog(session, BoardOf(card), card, confirm, services, notice, changeStarting);

    private static CardDialogViewModel Dialog(
        FakeSession session,
        Board board,
        Card card,
        Func<string, string, bool>? confirm = null,
        DialogServices? services = null,
        Action<string>? notice = null,
        Action? changeStarting = null)
        => new(session, board, CardRef.Of(card), services ?? Services(confirm), notice ?? (_ => { }), changeStarting ?? (() => { }));

    [Fact]
    public void Rows_show_the_package_facts_and_the_reason()
    {
        var dup = With("dup", copies: [@"C:\A\dup", @"C:\B\dup"]) with { Version = "1.2", Library = new LibraryRoot(@"C:\Lib", LibraryRoot.AllSections) };
        var gone = With("gone", on: false, canSwitch: false, reason: "Not found", removable: true);
        var vm = Dialog(new FakeSession(), CardOf(1, "Schiphol", dup, gone));

        Assert.Equal("Schiphol", vm.NameText);
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

        Assert.Null(vm.Message);
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

        Assert.Equal("Stored name", vm.NameText);
        Assert.True(vm.CanEdit);
    }

    [Fact]
    public void Update_returns_false_when_the_card_is_gone()
    {
        var vm = Dialog(new FakeSession(), CardOf(1, "A", With("a")));

        Assert.False(vm.Update(Board()));
    }

    [Fact]
    public void A_card_that_is_not_on_the_board_is_refused()
    {
        var card = CardOf(1, "A", With("a"));

        Assert.Throws<ArgumentException>(() => new CardDialogViewModel(new FakeSession(), Board(), CardRef.Of(card), Services(), _ => { }, () => { }));
    }

    [Fact]
    public async Task Remove_asks_first_and_does_nothing_on_no()
    {
        var session = new FakeSession();
        var asked = new List<(string Caption, string Text)>();
        var vm = Dialog(session, CardOf(1, "A", With("gone", on: false, canSwitch: false, removable: true)), (caption, text) =>
        {
            asked.Add((caption, text));
            return false;
        });

        await vm.RemoveCommand.ExecuteAsync(vm.Rows[0]);

        Assert.Equal([("Remove", "Remove gone and forget it?")], asked);
        Assert.Empty(session.Removed);

        var yes = Dialog(session, CardOf(1, "A", With("gone", on: false, canSwitch: false, removable: true)));
        await yes.RemoveCommand.ExecuteAsync(yes.Rows[0]);
        var removed = Assert.Single(session.Removed);
        Assert.Equal(PackageKey.Folder(new PackageName("gone")), removed.Package);
    }

    [Fact]
    public async Task Remove_does_nothing_for_a_row_without_a_key()
    {
        var session = new FakeSession();
        var asked = 0;
        var vm = Dialog(session, CardOf(1, "A", With("a")), (_, _) =>
        {
            asked++;
            return true;
        });

        await vm.RemoveCommand.ExecuteAsync(new GsxProfileRow("EHAM-other", null, false, null, null, null, true));

        Assert.Equal(0, asked);
        Assert.Empty(session.Removed);
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

    [Fact]
    public async Task Clicking_through_the_name_saves_nothing()
    {
        var session = new FakeSession();
        var vm = Dialog(session, CardOf(1, "Schiphol", With("a")));

        vm.NameFocused = true;
        Assert.Null(await vm.CommitNameAsync());
        await vm.NameLostFocusAsync();

        Assert.Empty(session.Renamed);
        Assert.Equal("Schiphol", vm.NameText);
    }

    [Fact]
    public async Task A_changed_name_is_trimmed_and_saved_and_empty_clears_it()
    {
        var session = new FakeSession();
        var vm = Dialog(session, CardOf(1, "Schiphol", With("a")));

        vm.NameText = "  Home ";
        Assert.Null(await vm.CommitNameAsync());
        vm.NameText = "";
        Assert.Null(await vm.CommitNameAsync());

        Assert.Equal([(new CardRef(1, null), "Home"), (new CardRef(1, null), (string?)null)], session.Renamed);
    }

    [Fact]
    public async Task A_new_board_keeps_the_text_of_a_focused_name_box_and_refreshes_an_unfocused_one()
    {
        var session = new FakeSession();
        var vm = Dialog(session, CardOf(1, "A", With("a")));

        vm.Update(BoardOf(CardOf(1, "A2", With("a"))));
        Assert.Equal("A2", vm.NameText);

        // A focused box keeps its text, also untouched, so clicking through it never pins the newer computed name.
        vm.NameFocused = true;
        vm.Update(BoardOf(CardOf(1, "A3", With("a"))));
        Assert.Equal("A2", vm.NameText);
        await vm.NameLostFocusAsync();
        Assert.Empty(session.Renamed);
        Assert.Equal("A3", vm.NameText);

        vm.NameFocused = true;
        vm.NameText = "typed";
        vm.Update(BoardOf(CardOf(1, "A4", With("a"))));
        Assert.Equal("typed", vm.NameText);
    }

    [Fact]
    public async Task A_name_refused_on_focus_loss_stays_and_is_sent_again_by_the_next_commit()
    {
        var session = new FakeSession { RenameMessage = Locked };
        var vm = Dialog(session, CardOf(1, "A", With("a")));
        vm.NameFocused = true;
        vm.NameText = "Home";

        await vm.NameLostFocusAsync();

        Assert.False(vm.NameFocused);
        Assert.Equal(Locked, vm.Message);
        Assert.Equal("Home", vm.NameText);
        vm.Update(BoardOf(CardOf(1, "A2", With("a"))));
        Assert.Equal("Home", vm.NameText);

        session.RenameMessage = null;
        Assert.Null(await vm.CommitNameAsync());
        Assert.Equal(2, session.Renamed.Count);
    }

    [Fact]
    public async Task Two_commits_at_once_send_one_rename()
    {
        var hold = new TaskCompletionSource<string?>();
        var session = new FakeSession { RenameHold = hold };
        var vm = Dialog(session, CardOf(1, "A", With("a")));
        vm.NameText = "Home";

        var first = vm.CommitNameAsync();
        var second = vm.CommitNameAsync();
        hold.SetResult(Locked);

        Assert.Equal(Locked, await first);
        Assert.Equal(Locked, await second);
        Assert.Single(session.Renamed);
    }

    [Fact]
    public async Task Text_typed_during_a_commit_is_sent_by_the_next_commit()
    {
        var hold = new TaskCompletionSource<string?>();
        var session = new FakeSession { RenameHold = hold };
        var vm = Dialog(session, CardOf(1, "A", With("a")));
        vm.NameText = "Home";

        var first = vm.CommitNameAsync();
        vm.NameText = "Home base";
        var second = vm.CommitNameAsync();
        hold.SetResult(null);
        await first;
        await second;

        Assert.Equal(["Home", "Home base"], session.Renamed.Select(r => r.Name));
    }

    [Fact]
    public async Task After_detach_a_commit_sends_nothing()
    {
        var session = new FakeSession();
        var vm = Dialog(session, CardOf(1, "A", With("a")));
        vm.NameText = "Home";

        vm.Detach();
        await vm.NameLostFocusAsync();

        Assert.Null(await vm.CommitNameAsync());
        Assert.Empty(session.Renamed);
    }

    [Fact]
    public async Task Switching_to_another_card_saves_the_name_and_shows_that_card()
    {
        var session = new FakeSession();
        var a = CardOf(1, "A", With("a"));
        var b = Card(2, "B", packages: ["b1", "b2"]);
        var vm = Dialog(session, Board(a, b), a);
        vm.NameText = "Home";

        await vm.ShowCardCommand.ExecuteAsync(CardRef.Of(b));

        Assert.Equal([(new CardRef(1, null), (string?)"Home")], session.Renamed);
        Assert.Equal("B", vm.NameText);
        Assert.Equal(["b1", "b2"], vm.Rows.Select(r => r.FolderName));
        Assert.Null(await vm.CommitNameAsync());
        Assert.Single(session.Renamed);

        await vm.ToggleCommand.ExecuteAsync(null);
        Assert.Equal([new CardRef(2, null)], session.Switched);
    }

    [Fact]
    public async Task Every_session_command_clears_the_notice_first()
    {
        var session = new FakeSession();
        var clears = new List<int>();
        int Calls() => session.Switched.Count + session.Removed.Count + session.Renamed.Count + session.ImagesSet.Count + session.Splits.Count;
        var services = Services(pick: () => @"D:\Pictures\eham.png");
        var vm = Dialog(session, CardOf(1, "A", With("a"), With("gone", on: false, canSwitch: false, removable: true)), services: services,
            changeStarting: () => clears.Add(Calls()));

        await vm.ToggleCommand.ExecuteAsync(null);
        await vm.RemoveCommand.ExecuteAsync(vm.Rows[1]);
        vm.NameText = "Home";
        await vm.CommitNameAsync();
        await vm.SetImageCommand.ExecuteAsync(null);
        await vm.ChooseImageCommand.ExecuteAsync(null);
        await vm.SplitCommand.ExecuteAsync(vm.Rows[1]);

        Assert.Equal([0, 1, 2, 3, 4, 5], clears);

        // A commit with unchanged text and a declined Remove send nothing, so they keep the notice.
        await vm.CommitNameAsync();
        var declined = Dialog(session, CardOf(1, "A", With("gone", on: false, canSwitch: false, removable: true)), (_, _) => false,
            changeStarting: () => clears.Add(-1));
        await declined.RemoveCommand.ExecuteAsync(declined.Rows[0]);
        Assert.Equal(6, clears.Count);
    }

    [Fact]
    public async Task Code_commands_clear_the_notice_first()
    {
        var session = new FakeSession();
        var clears = new List<int>();
        var vm = Dialog(session, CardOf(1, "A", With("a")), changeStarting: () => clears.Add(session.CodesAdded.Count + session.CodesRemoved.Count));
        session.Airports = Known(("YALR", "Alpha Rock"));

        vm.CodeText = "yalr";
        await vm.AddCodeCommand.ExecuteAsync(null);
        await vm.PickSuggestionCommand.ExecuteAsync(session.Airports.Values.First());
        await vm.RemoveCodeCommand.ExecuteAsync(Code("EHAM"));

        Assert.Equal([0, 1, 2], clears);
    }

    private static IReadOnlyDictionary<AirportCode, Airport> Known(params (string Code, string? Name)[] airports)
        => airports.ToDictionary(a => Code(a.Code), a => new Airport(Code(a.Code), a.Name, null, false, false));

    private static (FakeSession Session, CardDialogViewModel Vm) CodeDialog(params (string Code, string? Name)[] airports)
    {
        var session = new FakeSession { Airports = Known(airports) };
        return (session, Dialog(session, CardOf(1, "A", With("a"))));
    }

    [Fact]
    public void Typing_suggests_names_and_codes_and_selects_the_first()
    {
        var (_, vm) = CodeDialog(("EHAM", "Amsterdam Schiphol"), ("YALR", "Alpha Rock"), ("EHRD", "Rotterdam"));

        vm.CodeText = "ams";

        Assert.Equal(["EHAM"], vm.Suggestions.Select(a => a.Code.Value));
        Assert.Equal("EHAM", vm.SelectedSuggestion?.Code.Value);
        Assert.True(vm.SuggestionsOpen);

        vm.CodeText = "EH";
        Assert.Equal(["EHAM", "EHRD"], vm.Suggestions.Select(a => a.Code.Value));

        vm.CodeText = "zzz";
        Assert.Empty(vm.Suggestions);
        Assert.Null(vm.SelectedSuggestion);
        Assert.False(vm.SuggestionsOpen);
    }

    [Fact]
    public void Up_and_down_move_the_selection_within_the_list()
    {
        var (_, vm) = CodeDialog(("EHAM", null), ("EHRD", null), ("EHBK", null));
        vm.CodeText = "EH";

        vm.MoveSelection(-1);
        Assert.Equal("EHAM", vm.SelectedSuggestion?.Code.Value);
        vm.MoveSelection(1);
        vm.MoveSelection(1);
        vm.MoveSelection(1);
        Assert.Equal("EHRD", vm.SelectedSuggestion?.Code.Value);
    }

    [Fact]
    public async Task Enter_adds_the_selected_suggestion()
    {
        var (session, vm) = CodeDialog(("EHAM", null), ("EHRD", null));
        vm.CodeText = "EH";
        vm.MoveSelection(1);

        await vm.AddCodeCommand.ExecuteAsync(null);

        Assert.Equal("EHRD", session.CodesAdded.Single().Text);
        Assert.Equal("", vm.CodeText);
        Assert.False(vm.SuggestionsOpen);
        Assert.Null(vm.CodeMessage);
    }

    [Fact]
    public async Task Escape_closes_the_list_and_enter_then_adds_the_typed_text()
    {
        var (session, vm) = CodeDialog(("YALR1", "Alpha"));
        vm.CodeText = "YALR";
        Assert.True(vm.SuggestionsOpen);

        Assert.True(vm.CloseSuggestions());
        Assert.False(vm.CloseSuggestions());
        await vm.AddCodeCommand.ExecuteAsync(null);

        Assert.Equal("YALR", session.CodesAdded.Single().Text);
    }

    [Fact]
    public async Task An_invalid_code_shows_the_rule_and_keeps_the_text()
    {
        var (session, vm) = CodeDialog();
        session.AddCodeResult = (new CodeOutcome(CodeResult.Invalid), null);
        vm.CodeText = "A";

        await vm.AddCodeCommand.ExecuteAsync(null);

        Assert.Equal("Use 3–5 letters or digits.", vm.CodeMessage);
        Assert.Equal("A", vm.CodeText);
    }

    [Fact]
    public async Task A_taken_code_names_the_holder_and_keeps_the_text()
    {
        var (session, vm) = CodeDialog();
        session.AddCodeResult = (new CodeOutcome(CodeResult.Taken, 2, "EHAM Schiphol"), null);
        vm.CodeText = "eham";

        await vm.AddCodeCommand.ExecuteAsync(null);

        Assert.Equal("EHAM is already on EHAM Schiphol.", vm.CodeMessage);
        Assert.Equal("eham", vm.CodeText);
    }

    [Fact]
    public async Task Editing_the_code_text_clears_the_refusal()
    {
        var (session, vm) = CodeDialog();
        session.AddCodeResult = (new CodeOutcome(CodeResult.Invalid), null);
        vm.CodeText = "A";
        await vm.AddCodeCommand.ExecuteAsync(null);
        Assert.NotNull(vm.CodeMessage);

        vm.CodeText = "AB";

        Assert.Null(vm.CodeMessage);
    }

    [Fact]
    public async Task A_taken_code_whose_holder_is_not_shown_says_another_card()
    {
        var (session, vm) = CodeDialog();
        session.AddCodeResult = (new CodeOutcome(CodeResult.Taken, 2), null);
        vm.CodeText = "EHAM";

        await vm.AddCodeCommand.ExecuteAsync(null);

        Assert.Equal("EHAM is already on another card.", vm.CodeMessage);
    }

    [Fact]
    public async Task A_session_refusal_shows_the_message()
    {
        var (session, vm) = CodeDialog();
        session.AddCodeResult = (null, "userdata.db is locked");
        vm.CodeText = "EHAM";

        await vm.AddCodeCommand.ExecuteAsync(null);

        Assert.Equal("userdata.db is locked", vm.Message);
        Assert.Equal("EHAM", vm.CodeText);
        Assert.Null(vm.CodeMessage);
    }

    [Fact]
    public async Task Removing_a_chip_calls_the_session()
    {
        var (session, vm) = CodeDialog();

        await vm.RemoveCodeCommand.ExecuteAsync(Code("EHAM"));

        Assert.Equal(Code("EHAM"), session.CodesRemoved.Single().Code);
        Assert.Equal([Code("EHAM")], vm.CodeChips);
    }

    [Fact]
    public void Only_scenery_cards_show_the_code_field()
    {
        Assert.True(Dialog(new FakeSession(), Card(1, "S")).ShowCodes);
        Assert.False(Dialog(new FakeSession(), Card(1, "Plane", section: Category.Aircraft)).ShowCodes);
    }

    [Fact]
    public void Provisional_cards_are_read_only()
    {
        var provisional = Card(null, "pkg-a", packages: ["pkg-a", "pkg-b"]);
        var vm = Dialog(new FakeSession(), provisional);

        Assert.False(vm.AddCodeCommand.CanExecute(null));
        Assert.False(vm.PickSuggestionCommand.CanExecute(null));
        Assert.False(vm.RemoveCodeCommand.CanExecute(Code("EHAM")));
        Assert.False(vm.CanEdit);
        Assert.False(vm.CanSplit);
        Assert.False(vm.SetImageCommand.CanExecute(null));
        Assert.False(vm.ChooseImageCommand.CanExecute(null));
        Assert.False(vm.SplitCommand.CanExecute(vm.Rows[0]));
    }

    [Fact]
    public void Profiles_are_not_package_rows_and_split_needs_two_other_packages()
    {
        var one = Dialog(new FakeSession(), CardOf(1, "A", With("a"), Profile("EHAM-xyz")));
        Assert.Equal(["a"], one.Rows.Select(r => r.FolderName));
        Assert.False(one.CanSplit);
        Assert.False(one.SplitCommand.CanExecute(one.Rows[0]));

        var two = Dialog(new FakeSession(), CardOf(1, "A", With("a"), With("b"), Profile("EHAM-xyz")));
        Assert.Equal(["a", "b"], two.Rows.Select(r => r.FolderName));
        Assert.True(two.CanSplit);
        Assert.True(two.SplitCommand.CanExecute(two.Rows[0]));
    }

    [Fact]
    public async Task Split_calls_the_session_with_the_row()
    {
        var session = new FakeSession { SplitMessage = Locked };
        var vm = Dialog(session, CardOf(1, "A", With("a"), With("b")));

        await vm.SplitCommand.ExecuteAsync(vm.Rows[1]);

        Assert.Equal([(new CardRef(1, null), PackageKey.Folder(new PackageName("b")))], session.Splits);
        Assert.Equal(Locked, vm.Message);
    }

    [Fact]
    public void Locked_rows_are_dimmed()
    {
        var vm = Dialog(new FakeSession(), CardOf(1, "A", With("a"), With("held") with { Lock = LockReason.HeldByEntry }));

        Assert.False(vm.Rows[0].Dimmed);
        Assert.True(vm.Rows[1].Dimmed);
        Assert.Equal(PackageKey.Folder(new PackageName("held")), ((IRemovableRow)vm.Rows[1]).Key);
    }

    [Fact]
    public async Task Image_choices_list_packages_with_thumbnails_and_automatic_clears()
    {
        var session = new FakeSession();
        var image = new DrawingImage();
        var loads = new List<string?>();
        var services = Services(load: path =>
        {
            loads.Add(path);
            return Task.FromResult<ImageSource?>(path == @"C:\Lib\a\thumb.jpg" ? image : null);
        });
        var vm = Dialog(session, CardOf(1, "A", With("a") with { ThumbnailPath = @"C:\Lib\a\thumb.jpg" }, With("b")), services: services);

        var choice = Assert.Single(vm.ImageChoices);
        Assert.Equal("a", choice.DisplayName);
        Assert.Equal(@"C:\Lib\a\thumb.jpg", choice.Path);
        Assert.Same(image, choice.Image);
        Assert.Contains(@"C:\Lib\a\thumb.jpg", loads);

        Assert.True(vm.SetImageCommand.CanExecute(choice.Path));
        await vm.SetImageCommand.ExecuteAsync(choice.Path);
        await vm.SetImageCommand.ExecuteAsync(null);

        Assert.Equal([(new CardRef(1, null), (string?)@"C:\Lib\a\thumb.jpg"), (new CardRef(1, null), null)], session.ImagesSet);
    }

    [Fact]
    public async Task Choose_file_sets_the_picked_path_and_cancel_does_nothing()
    {
        var session = new FakeSession();
        string? picked = null;
        var vm = Dialog(session, CardOf(1, "A", With("a")), services: Services(pick: () => picked));

        await vm.ChooseImageCommand.ExecuteAsync(null);
        Assert.Empty(session.ImagesSet);

        picked = @"D:\Pictures\eham.png";
        await vm.ChooseImageCommand.ExecuteAsync(null);
        Assert.Equal([(new CardRef(1, null), (string?)@"D:\Pictures\eham.png")], session.ImagesSet);
    }

    [Fact]
    public void A_new_board_whose_image_loads_as_null_clears_the_dialog_image()
    {
        var image = new DrawingImage();
        ImageSource? next = image;
        var card = Card(1, "A", tab: CardTab.Landscapes, image: @"C:\Pictures\a.png");
        var vm = Dialog(new FakeSession(), card, services: Services(load: path => Task.FromResult(path == @"C:\Pictures\a.png" ? next : null)));
        Assert.Same(image, vm.Image);
        Assert.Equal(CardTab.Landscapes, vm.Tab);

        next = null;
        vm.Update(BoardOf(card with { }));

        Assert.Null(vm.Image);
    }

    private static Card GsxCard(long id, string name, string code)
        => Card(id, name, codes: [code]) with
        {
            GsxAirports = [new GsxAirport(Code(code), GsxMark.None, [], [], [], [], GsxStatus.SearchLink(Code(code)))],
        };

    [Fact]
    public void A_search_link_that_fails_lights_the_warning_on_that_code()
    {
        var opened = new List<string>();
        var card = GsxCard(1, "Rotterdam", "EHRD");
        var vm = Dialog(new FakeSession(), card, services: Services(open: link =>
        {
            opened.Add(link);
            throw new InvalidOperationException("No browser");
        }));
        var row = Assert.IsType<GsxSearchRow>(Assert.Single(vm.Gsx.Rows));
        Assert.Null(row.Error);

        vm.SearchCommand.Execute(row);

        Assert.Equal([row.Link], opened);
        Assert.Equal("No browser", Assert.IsType<GsxSearchRow>(Assert.Single(vm.Gsx.Rows)).Error);
    }

    [Fact]
    public async Task The_search_error_is_kept_by_a_new_board_and_cleared_by_a_card_switch()
    {
        var a = GsxCard(1, "Rotterdam", "EHRD");
        var b = GsxCard(2, "Groningen", "EHGG");
        var vm = Dialog(new FakeSession(), Board(a, b), a, services: Services(open: _ => throw new InvalidOperationException("No browser")));
        vm.SearchCommand.Execute((GsxSearchRow)vm.Gsx.Rows[0]);

        vm.Update(Board(a with { }, b));
        Assert.Equal("No browser", ((GsxSearchRow)vm.Gsx.Rows[0]).Error);

        await vm.ShowCardCommand.ExecuteAsync(CardRef.Of(b));
        Assert.Null(((GsxSearchRow)vm.Gsx.Rows[0]).Error);

        await vm.ShowCardCommand.ExecuteAsync(CardRef.Of(a));
        Assert.Null(((GsxSearchRow)vm.Gsx.Rows[0]).Error);
    }
}
