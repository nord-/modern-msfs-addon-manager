using System.Windows.Media;
using AddonManager.App.Session;
using AddonManager.App.ViewModels;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using AddonManager.Sim2020;
using static AddonManager.App.Tests.ViewModels.Boards;

namespace AddonManager.App.Tests.ViewModels;

public sealed class MainViewModelTests
{
    private readonly FakeSession session = new();
    private readonly List<string?> loads = [];
    private Func<string?, Task<ImageSource?>> loader;

    public MainViewModelTests() => loader = _ => Task.FromResult<ImageSource?>(null);

    private MainViewModel Create(Action<Action>? post = null)
        => new(session, post ?? (a => a()), @"C:\GSX", path =>
        {
            loads.Add(path);
            return loader(path);
        });

    private static string[] Names(MainViewModel vm) => [.. vm.Cards.Select(c => c.Name)];

    private void Show(SessionView view)
    {
        session.Current = view;
        session.Raise(view);
    }

    [Fact]
    public void Selecting_a_section_lists_its_tabs_with_counts()
    {
        var vm = Create();
        Show(Ready(Board(
            Card(1, "A", codes: ["ESSA"]), Card(2, "B", codes: ["ESSB"]), Card(3, "C", tab: CardTab.Landscapes),
            Card(4, "Jet", Category.Aircraft, CardTab.Instruments))));

        Assert.Equal(["Airports (2)", "Landscapes (1)", "Assets (0)"], vm.Tabs.Select(t => t.Header));
        Assert.Equal(["Airports (2)", "Landscapes (1)", "Assets (0)"], vm.Tabs.Select(t => t.ToString()));
        Assert.Equal(CardTab.Airports, vm.SelectedTab?.Tab);
        Assert.True(vm.SelectedTab!.IsSelected);

        vm.SelectedMenu = MenuSection.Aircraft;

        Assert.Equal(["Aircraft (0)", "Instruments (1)", "Other (0)"], vm.Tabs.Select(t => t.Header));
        Assert.Equal(CardTab.Aircraft, vm.SelectedTab?.Tab);
    }

    [Fact]
    public void The_wall_shows_the_selected_tab_in_board_order()
    {
        var vm = Create();
        Show(Ready(Board(Card(1, "Zulu", codes: ["ESSA"]), Card(2, "Alpha", codes: ["ESSB"]), Card(3, "Land", tab: CardTab.Landscapes))));

        Assert.Equal(["Zulu", "Alpha"], Names(vm));

        vm.SelectedTab = vm.Tabs[1];

        Assert.Equal(["Land"], Names(vm));
        Assert.Null(vm.Cards[0].Gsx);
    }

    [Fact]
    public void Clashing_titles_get_subtitles()
    {
        var vm = Create();
        Show(Ready(Board(
            Card(1, "Arlanda", codes: ["ESSA"], packages: ["pkg-one"]), Card(2, "Arlanda", codes: ["ESSA"], packages: ["pkg-two"]),
            Card(3, "Bromma", codes: ["ESSB"]))));

        Assert.Equal(["pkg-one", "pkg-two", null], vm.Cards.Select(c => c.Subtitle));
    }

    [Fact]
    public void The_other_section_has_no_tabs()
    {
        var vm = Create();
        Show(Ready(Board(Card(1, "Tool", Category.Other, CardTab.Other), Card(2, "Jet", Category.Aircraft, CardTab.Other))));

        vm.SelectedMenu = MenuSection.Other;

        Assert.Empty(vm.Tabs);
        Assert.Null(vm.SelectedTab);
        Assert.Equal(["Tool"], Names(vm));
    }

    [Fact]
    public void Scanning_hides_the_wall_until_ready()
    {
        var vm = Create();
        Show(new SessionView(SessionPhase.Scanning, null, new ScanProgress(1, 4, null), null));

        Assert.True(vm.ShowScanning);
        Assert.False(vm.ShowWall);
        Assert.Equal(["Scanning… 1 of 4"], vm.BannerLines);

        Show(Ready(Board(Card(1, "A", codes: ["ESSA"]))));

        Assert.False(vm.ShowScanning);
        Assert.True(vm.ShowWall);
        Assert.Equal(["A"], Names(vm));
    }

    [Fact]
    public void A_failure_shows_the_error_screen()
    {
        var vm = Create();
        Show(new SessionView(SessionPhase.Failed, null, null, "Community is gone"));

        Assert.True(vm.ShowError);
        Assert.Equal("Community is gone", vm.ErrorText);
        Assert.False(vm.ShowWall);
        Assert.False(vm.ShowFirstRun);
    }

    [Fact]
    public void The_first_run_screens_cover_a_first_run_error()
    {
        var vm = Create();

        Show(new SessionView(SessionPhase.NeedsPackagesRoot, null, null, null));
        Assert.True(vm.ShowFirstRun);

        Show(new SessionView(SessionPhase.FirstRun, null, null, "No Library"));
        Assert.True(vm.ShowFirstRun);
        Assert.False(vm.ShowError);
        Assert.Equal("No Library", vm.ErrorText);
    }

    [Fact]
    public void F5_asks_for_a_full_scan_only_when_ready()
    {
        var vm = Create();
        Show(new SessionView(SessionPhase.Scanning, null, null, null));
        Assert.False(vm.RescanCommand.CanExecute(null));

        Show(Ready(Board()));
        Assert.True(vm.RescanCommand.CanExecute(null));

        vm.RescanCommand.Execute(null);

        Assert.Equal([true], session.Rescans);
    }

    [Fact]
    public void F5_retries_after_a_run_failure()
    {
        var vm = Create();

        Show(new SessionView(SessionPhase.Failed, null, null, "Boom"));
        Assert.False(vm.RescanCommand.CanExecute(null));
        Assert.False(vm.ShowRetryHint);

        Show(new SessionView(SessionPhase.Failed, null, null, "Boom", CanRetry: true));
        Assert.True(vm.RescanCommand.CanExecute(null));
        Assert.True(vm.ShowRetryHint);
        Assert.Equal("The app tries again when Community or a Library changes, or when you press F5.", vm.RetryHint);
    }

    [Fact]
    public void A_new_board_keeps_the_selected_section_and_tab()
    {
        var vm = Create();
        Show(Ready(Board(Card(1, "Jet", Category.Aircraft, CardTab.Instruments))));
        vm.SelectedMenu = MenuSection.Aircraft;
        vm.SelectedTab = vm.Tabs[1];

        Show(Ready(Board(Card(1, "Jet", Category.Aircraft, CardTab.Instruments), Card(2, "Jet2", Category.Aircraft, CardTab.Instruments))));

        Assert.Equal(MenuSection.Aircraft, vm.SelectedMenu);
        Assert.Equal(CardTab.Instruments, vm.SelectedTab?.Tab);
        Assert.Equal(["Jet", "Jet2"], Names(vm));
    }

    [Fact]
    public void An_older_view_raised_late_does_not_win()
    {
        var vm = Create();
        var older = Ready(Board(Card(1, "Old", codes: ["ESSA"])));
        var newer = Ready(Board(Card(2, "New", codes: ["ESSB"])));

        session.Current = newer;
        session.Raise(newer);
        session.Raise(older);

        Assert.Equal(["New"], Names(vm));
    }

    [Fact]
    public void A_progress_only_view_does_not_rebuild_the_wall()
    {
        var vm = Create();
        var board = Board(Card(1, "A", codes: ["ESSA"], image: "a.png"));
        Show(Ready(board));
        var before = vm.Cards.ToArray();
        var loadsBefore = loads.Count;

        Show(Ready(board, new ScanProgress(5, 10, null)));

        Assert.Equal(before, vm.Cards);
        Assert.Same(before[0], vm.Cards[0]);
        Assert.Equal(loadsBefore, loads.Count);
        Assert.Equal(["Scanning… 5 of 10"], vm.BannerLines);
    }

    [Fact]
    public void A_new_board_keeps_unchanged_cards()
    {
        var vm = Create();
        var a = Card(1, "A", codes: ["ESSA"]);
        var b = Card(2, "B", codes: ["ESSB"]);
        var c = Card(3, "C", codes: ["ESSC"]);
        Show(Ready(Board(a, b, c)));
        var before = vm.Cards.ToArray();

        Show(Ready(Board(a, b with { State = CardState.Off }, c)));

        Assert.Same(before[0], vm.Cards[0]);
        Assert.Same(before[1], vm.Cards[1]);
        Assert.Same(before[2], vm.Cards[2]);
        Assert.Equal(CardState.Off, vm.Cards[1].Light);
    }

    [Fact]
    public void Cards_are_inserted_moved_and_removed_in_place()
    {
        var vm = Create();
        var a = Card(1, "A", codes: ["ESSA"]);
        var b = Card(2, "B", codes: ["ESSB"]);
        var c = Card(null, "C", codes: ["ESSC"]);
        Show(Ready(Board(a, b, c)));
        var before = vm.Cards.ToArray();

        var d = Card(4, "D", codes: ["ESSD"]);
        Show(Ready(Board(c, d, a)));

        Assert.Equal(["C", "D", "A"], Names(vm));
        Assert.Same(before[2], vm.Cards[0]);
        Assert.Same(before[0], vm.Cards[2]);
    }

    [Fact]
    public void Updates_are_coalesced()
    {
        var queue = new Queue<Action>();
        var vm = Create(queue.Enqueue);
        session.Current = Ready(Board(Card(1, "A", codes: ["ESSA"])));
        session.Reads = 0;

        for (var i = 0; i < 1000; i++)
            session.Raise(session.Current);

        Assert.Single(queue);
        session.Reads = 0;
        queue.Dequeue()();

        Assert.Equal(1, session.Reads);
        Assert.Equal(["A"], Names(vm));
    }

    [Fact]
    public void An_event_during_an_update_is_not_lost()
    {
        var queue = new Queue<Action>();
        var vm = Create(queue.Enqueue);
        var v1 = Ready(Board(Card(1, "One", codes: ["ESSA"])));
        var v2 = Ready(Board(Card(2, "Two", codes: ["ESSB"])));
        session.Current = v1;
        session.Raise(v1);
        var armed = true;
        session.OnRead = () =>
        {
            if (!armed)
                return;
            armed = false;
            session.Current = v2;
            session.Raise(v2);
        };

        var updates = 0;
        while (queue.Count > 0)
        {
            queue.Dequeue()();
            updates++;
        }

        Assert.Equal(2, updates);
        Assert.Equal(["Two"], Names(vm));
    }

    [Fact]
    public void A_missing_thumbnail_is_tried_again_on_the_next_board()
    {
        var vm = Create();
        var a = Card(1, "A", codes: ["ESSA"], image: "a.png");
        var board = Board(a);
        Show(Ready(board));
        var kept = vm.Cards[0];
        Assert.Null(kept.Image);

        var image = new DrawingImage();
        loader = _ => Task.FromResult<ImageSource?>(image);
        Show(Ready(board with { }));

        Assert.Same(kept, vm.Cards[0]);
        Assert.Same(image, kept.Image);
        Assert.Equal(["a.png", "a.png"], loads);
    }

    [Fact]
    public void A_loaded_thumbnail_is_not_loaded_again()
    {
        var image = new DrawingImage();
        loader = _ => Task.FromResult<ImageSource?>(image);
        var vm = Create();
        var a = Card(1, "A", codes: ["ESSA"], image: "a.png");
        Show(Ready(Board(a)));

        Show(Ready(Board(a)));

        Assert.Equal(["a.png"], loads);
        Assert.Same(image, vm.Cards[0].Image);
    }

    [Fact]
    public async Task A_thumbnail_for_an_old_path_is_dropped()
    {
        var pending = new Dictionary<string, TaskCompletionSource<ImageSource?>>();
        loader = path => (pending[path!] = new()).Task;
        var vm = Create();
        Show(Ready(Board(Card(1, "A", codes: ["ESSA"], image: "old.png"))));
        Show(Ready(Board(Card(1, "A", codes: ["ESSA"], image: "new.png"))));
        var image = new DrawingImage();

        // The loader's continuation does not run on the test thread, so wait for the result to land.
        pending["new.png"].SetResult(image);
        Assert.True(SpinWait.SpinUntil(() => vm.Cards[0].Image is not null, TimeSpan.FromSeconds(5)));
        pending["old.png"].SetResult(new DrawingImage());
        await Task.Delay(100);

        Assert.Same(image, vm.Cards[0].Image);
    }

    [Fact]
    public void The_menu_is_hidden_outside_ready()
    {
        var vm = Create();
        Assert.False(vm.ShowMenu);

        foreach (var phase in new[] { SessionPhase.Starting, SessionPhase.NeedsPackagesRoot, SessionPhase.FirstRun, SessionPhase.Scanning, SessionPhase.Failed })
        {
            Show(new SessionView(phase, null, null, null));
            Assert.False(vm.ShowMenu);
        }

        Show(Ready(Board()));
        Assert.True(vm.ShowMenu);
    }

    [Fact]
    public void Each_section_remembers_its_tab()
    {
        var vm = Create();
        Show(Ready(Board()));

        vm.SelectedTab = vm.Tabs[1];
        vm.SelectedMenu = MenuSection.Aircraft;
        Assert.Equal(CardTab.Aircraft, vm.SelectedTab?.Tab);
        vm.SelectedTab = vm.Tabs[1];
        vm.SelectedMenu = MenuSection.Scenery;

        Assert.Equal(CardTab.Landscapes, vm.SelectedTab?.Tab);
        vm.SelectedMenu = MenuSection.Aircraft;
        Assert.Equal(CardTab.Instruments, vm.SelectedTab?.Tab);
    }

    [Fact]
    public void Settings_lists_the_libraries_per_section_and_the_paths()
    {
        session.State.Libraries.Add(new LibraryRoot(@"D:\Both", [Category.Scenery, Category.Aircraft]));
        session.State.Libraries.Add(new LibraryRoot(@"D:\Scenery", [Category.Scenery]));
        session.Paths = new SimPaths(null, @"D:\Packages", @"D:\Packages\Community", null);
        var vm = Create();
        Show(Ready(Board()));

        vm.SelectedMenu = MenuSection.Settings;

        Assert.True(vm.ShowSettings);
        Assert.False(vm.ShowWall);
        Assert.Empty(vm.Tabs);
        Assert.Equal(["Scenery", "Aircraft", "Other"], vm.SettingsLibraries.Select(s => s.Title));
        Assert.Equal(["Scenery", "Aircraft", "Other"], vm.SettingsLibraries.Select(s => s.ToString()));
        Assert.Equal([@"D:\Both", @"D:\Scenery"], vm.SettingsLibraries[0].Folders);
        Assert.Equal([@"D:\Both"], vm.SettingsLibraries[1].Folders);
        Assert.Empty(vm.SettingsLibraries[2].Folders);
        Assert.Equal(@"D:\Packages", vm.PackagesRoot);
        Assert.Equal(@"C:\Data\userdata.db", vm.DatabaseFile);
    }
}
