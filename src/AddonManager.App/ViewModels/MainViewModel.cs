using System.Collections.ObjectModel;
using System.Windows.Media;
using AddonManager.App.Session;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AddonManager.App.ViewModels;

/// <summary>The left menu. Not called <c>MenuItem</c>, which clashes with the WPF and WPF-UI control of that name.</summary>
public enum MenuSection { Scenery, Aircraft, Other, Settings }

/// <summary>The Libraries registered for one section, as Settings lists them.</summary>
public sealed record SettingsSection(string Title, IReadOnlyList<string> Folders)
{
    /// <summary>UI Automation falls back to this for the entry's name, instead of the record's member dump.</summary>
    public override string ToString() => Title;
}

/// <summary>
/// The window: which screen shows, the menu, the tabs, the wall and the banner. It only shows what the session publishes; every
/// update reads <c>session.Current</c> on the UI thread, so views raised out of order still end on the newest.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public const string RetryHintText = "The app tries again when Community or a Library changes, or when you press F5.";

    private readonly IAppSession session;
    private readonly Action<Action> post;
    private readonly string gsxFolder;
    private readonly DialogServices services;
    private readonly Dictionary<MenuSection, CardTab> rememberedTabs = [];
    private Board? lastBoard;
    private IReadOnlyList<string> sessionLines = [];
    private string? notice;
    private int queued;
    private bool applying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWall), nameof(ShowSettings))]
    private MenuSection selectedMenu = MenuSection.Scenery;

    [ObservableProperty]
    private TabViewModel? selectedTab;

    [ObservableProperty]
    private IReadOnlyList<string> bannerLines = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWall), nameof(ShowScanning), nameof(ShowFirstRun), nameof(ShowError), nameof(ShowMenu), nameof(ShowSettings), nameof(ShowRetryHint))]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand))]
    private SessionPhase phase = SessionPhase.Starting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRetryHint))]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand))]
    private bool canRetry;

    [ObservableProperty]
    private string? errorText;

    [ObservableProperty]
    private IReadOnlyList<SettingsSection> settingsLibraries = [];

    [ObservableProperty]
    private string? packagesRoot;

    [ObservableProperty]
    private string databaseFile = "";

    /// <summary>The open card dialog, or <c>null</c>.</summary>
    [ObservableProperty]
    private CardDialogViewModel? cardDialog;

    public MainViewModel(IAppSession session, Action<Action> post, string gsxFolder, DialogServices services)
    {
        this.session = session;
        this.post = post;
        this.gsxFolder = gsxFolder;
        this.services = services;
        session.Changed += OnSessionChanged;
        Apply(session.Current);
    }

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    public ObservableCollection<CardViewModel> Cards { get; } = [];

    public string RetryHint => RetryHintText;

    public bool ShowWall => Phase == SessionPhase.Ready && SelectedMenu != MenuSection.Settings;

    public bool ShowSettings => Phase == SessionPhase.Ready && SelectedMenu == MenuSection.Settings;

    public bool ShowScanning => Phase is SessionPhase.Starting or SessionPhase.Scanning;

    public bool ShowFirstRun => Phase is SessionPhase.FirstRun or SessionPhase.NeedsPackagesRoot;

    public bool ShowError => Phase == SessionPhase.Failed;

    /// <summary>The overlays cover the content area outside Ready, so the menu (and Settings) is only there in Ready.</summary>
    public bool ShowMenu => Phase == SessionPhase.Ready;

    public bool ShowRetryHint => Phase == SessionPhase.Failed && CanRetry;

    private bool CanRescan => Phase == SessionPhase.Ready || (Phase == SessionPhase.Failed && CanRetry);

    [RelayCommand]
    private void OpenCard(CardViewModel? card)
    {
        // A card opens only on the current board.
        if (card is null || lastBoard is null || card.Ref.In(lastBoard) is null)
            return;
        CardDialog = new CardDialogViewModel(session, lastBoard, card.Ref, services, ShowNotice, ClearNotice);
    }

    /// <summary>✕ and a click outside: saves the name first, then closes. A refused save still closes and goes to the banner.</summary>
    [RelayCommand]
    private async Task CloseCard()
    {
        if (CardDialog is not { } dialog)
            return;
        var refused = await dialog.CommitNameAsync();
        dialog.Detach();
        // Esc or another card may have replaced the dialog while the name was saved.
        if (ReferenceEquals(CardDialog, dialog))
            CardDialog = null;
        if (refused is not null)
            ShowNotice(refused);
    }

    /// <summary>Esc: drops the name not yet saved and closes.</summary>
    [RelayCommand]
    private void DiscardCard()
    {
        if (CardDialog is not { } dialog)
            return;
        dialog.DiscardName();
        dialog.Detach();
        CardDialog = null;
    }

    /// <summary>Shows a refused user change as the last banner line, until the next user change starts or a new board is shown.</summary>
    public void ShowNotice(string message)
    {
        notice = message;
        UpdateBanner();
    }

    public void ClearNotice()
    {
        notice = null;
        UpdateBanner();
    }

    private void UpdateBanner() => BannerLines = notice is null ? sessionLines : [.. sessionLines, notice];

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private Task Rescan() => session.RescanAsync(full: true);

    partial void OnSelectedMenuChanged(MenuSection value)
    {
        if (!applying)
            ApplySelection();
    }

    partial void OnSelectedTabChanged(TabViewModel? value)
    {
        if (applying || value is null)
            return;
        rememberedTabs[SelectedMenu] = value.Tab;
        foreach (var tab in Tabs)
            tab.IsSelected = ReferenceEquals(tab, value);
        RebuildCards();
    }

    /// <summary>May run on any thread and out of order: it only asks for one update, which reads <c>Current</c> later.</summary>
    private void OnSessionChanged(SessionView _)
    {
        if (Interlocked.Exchange(ref queued, 1) == 0)
            post(Update);
    }

    private void Update()
    {
        // Cleared before Current is read, so an event raised while this update runs posts another one.
        Interlocked.Exchange(ref queued, 0);
        Apply(session.Current);
    }

    private void Apply(SessionView view)
    {
        var wasReady = Phase == SessionPhase.Ready;
        var board = view.Board;
        var boardChanged = board is not null && !ReferenceEquals(board, lastBoard);
        if (boardChanged)
        {
            lastBoard = board;
            notice = null;
        }

        CanRetry = view.CanRetry;
        ErrorText = view.Phase == SessionPhase.Ready ? null : view.Error;
        sessionLines = BannerText.Lines(board?.Banner, view.Progress, view.Error, gsxFolder, view.LinksWaiting);
        UpdateBanner();
        Phase = view.Phase;
        if (boardChanged && CardDialog is { } dialog && !dialog.Update(lastBoard!))
        {
            // The card is gone: its unsaved name is dropped, so the unloading name box must not send it.
            dialog.Detach();
            CardDialog = null;
        }

        if (view.Phase == SessionPhase.Ready && (boardChanged || !wasReady))
            RefreshSettings();
        if (boardChanged)
            ApplySelection();
    }

    private void RefreshSettings()
    {
        // Libraries never change after first run in 5a, and the phase is Ready, so reading them without the session lock is safe.
        var libraries = session.State.Libraries;
        SettingsLibraries =
        [
            .. new[] { (Category.Scenery, "Scenery"), (Category.Aircraft, "Aircraft"), (Category.Other, "Other") }
                .Select(s => new SettingsSection(s.Item2, [.. libraries.Where(l => l.IsRegisteredFor(s.Item1)).Select(l => l.Path)])),
        ];
        PackagesRoot = session.Paths?.PackagesPath;
        DatabaseFile = session.DatabaseFile;
    }

    private static Category? CategoryOf(MenuSection menu) => menu switch
    {
        MenuSection.Scenery => Category.Scenery,
        MenuSection.Aircraft => Category.Aircraft,
        MenuSection.Other => Category.Other,
        _ => null,
    };

    /// <summary>Rebuilds the tabs for the selected section (the remembered tab, else the first) and then the wall.</summary>
    private void ApplySelection()
    {
        var section = CategoryOf(SelectedMenu);
        var tabs = section is { } s && SelectedMenu != MenuSection.Other ? CardTabs.Of(s) : [];

        applying = true;
        try
        {
            if (!Tabs.Select(t => t.Tab).SequenceEqual(tabs))
            {
                Tabs.Clear();
                foreach (var tab in tabs)
                    Tabs.Add(new TabViewModel(tab));
            }

            foreach (var tab in Tabs)
                tab.Header = $"{tab.Tab} ({lastBoard?.On(section!.Value, tab.Tab).Count() ?? 0})";

            TabViewModel? selected = null;
            if (tabs.Count > 0)
            {
                var wanted = rememberedTabs.TryGetValue(SelectedMenu, out var remembered) && tabs.Contains(remembered) ? remembered : tabs[0];
                rememberedTabs[SelectedMenu] = wanted;
                selected = Tabs.First(t => t.Tab == wanted);
            }

            foreach (var tab in Tabs)
                tab.IsSelected = ReferenceEquals(tab, selected);
            SelectedTab = selected;
        }
        finally
        {
            applying = false;
        }

        RebuildCards();
    }

    /// <summary>Brings <see cref="Cards"/> to the selected section and tab in place: kept cards are refreshed, new ones inserted, gone ones removed.</summary>
    private void RebuildCards()
    {
        var section = CategoryOf(SelectedMenu);
        if (lastBoard is null || section is null)
        {
            Cards.Clear();
            return;
        }

        var tab = SelectedMenu == MenuSection.Other ? CardTab.Other : SelectedTab?.Tab ?? CardTab.Other;
        var cards = lastBoard.On(section.Value, tab).ToList();
        var subtitles = CardTitles.Subtitles(cards);
        var showGsx = tab == CardTab.Airports;
        var wanted = cards.Select(CardViewModel.KeyOf).ToHashSet(StringComparer.Ordinal);

        for (var i = Cards.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Cards[i].Key))
                Cards.RemoveAt(i);
        }

        var kept = Cards.ToDictionary(c => c.Key, StringComparer.Ordinal);
        for (var i = 0; i < cards.Count; i++)
        {
            var key = CardViewModel.KeyOf(cards[i]);
            if (kept.TryGetValue(key, out var vm))
            {
                var oldPath = vm.ImagePath;
                vm.Update(cards[i], subtitles[i], showGsx);
                if (vm.ImagePath != oldPath)
                    vm.Image = null;
                var at = ReferenceEquals(Cards[i], vm) ? i : Cards.IndexOf(vm);
                if (at != i)
                    Cards.Move(at, i);
            }
            else
            {
                vm = new CardViewModel(cards[i], subtitles[i], showGsx);
                Cards.Insert(i, vm);
                kept[key] = vm;
            }

            // Every board asks again: a cache hit is cheap, and a file that is gone clears the image.
            if (vm.ImagePath is { } path)
                _ = LoadImageAsync(vm, path);
        }
    }

    private async Task LoadImageAsync(CardViewModel vm, string path)
    {
        ImageSource? result;
        try
        {
            result = await services.LoadImage(path).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A thumbnail that cannot be loaded leaves the card without an image; the next board tries again.
            result = null;
        }

        post(() =>
        {
            if (vm.ImagePath == path)
                vm.Image = result;
        });
    }
}
