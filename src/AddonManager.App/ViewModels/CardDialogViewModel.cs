using System.Collections.ObjectModel;
using AddonManager.App.Session;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Linking;
using AddonManager.Core.Packages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AddonManager.App.ViewModels;

/// <summary>
/// The card dialog: the header (name, image; <c>CardDialogViewModel.Header.cs</c>), the card's Package rows with their switch, Remove and Split,
/// and the info and GSX parts. The view binds <c>IsChecked</c> OneWay to <see cref="SwitchOn"/> and lets the click run <see cref="ToggleCommand"/>;
/// the command always raises <c>PropertyChanged(SwitchOn)</c> when it completes, so a toggle that changed nothing snaps back.
/// Every command that sends a change to the session calls <c>changeStarting</c> right before it sends, which clears the banner's notice.
/// </summary>
public sealed partial class CardDialogViewModel : ObservableObject
{
    private const string NothingToSwitch = "No package on this card can be switched";

    private readonly IAppSession session;
    private readonly DialogServices services;
    private readonly Action<string> notice;
    private readonly Action changeStarting;
    private readonly Dictionary<AirportCode, string> searchErrors = [];
    private Board board = null!;
    private CardRef cardRef;
    private Card card = null!;
    private bool simRefused;

    [ObservableProperty]
    private string? message;

    [ObservableProperty]
    private CardInfoViewModel info = null!;

    [ObservableProperty]
    private GsxSectionViewModel gsx = null!;

    /// <param name="notice">Shows a message as a banner line (a name save refused on a card switch).</param>
    /// <param name="changeStarting">Runs right before each change is sent to the session.</param>
    public CardDialogViewModel(IAppSession session, Board board, CardRef card, DialogServices services, Action<string> notice, Action changeStarting)
    {
        this.session = session;
        this.services = services;
        this.notice = notice;
        this.changeStarting = changeStarting;
        cardRef = card;
        simRefused = session.IsSimRunning;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => CanToggle);
        RemoveCommand = new AsyncRelayCommand<IRemovableRow>(RemoveAsync, row => row is { CanRemove: true } && !simRefused);
        SplitCommand = new AsyncRelayCommand<PackageRowViewModel>(SplitAsync, row => row is not null && CanSplit);
        SetImageCommand = new AsyncRelayCommand<string?>(SetImageAsync, _ => CanEdit);
        ChooseImageCommand = new AsyncRelayCommand(ChooseImageAsync, () => CanEdit);
        AddCodeCommand = new AsyncRelayCommand(AddSelectedOrTypedAsync, () => CanEdit);
        PickSuggestionCommand = new AsyncRelayCommand<Airport>(PickSuggestionAsync, _ => CanEdit);
        RemoveCodeCommand = new AsyncRelayCommand<AirportCode>(RemoveCodeAsync, _ => CanEdit);
        ShowCardCommand = new AsyncRelayCommand<CardRef>(ShowCardAsync);
        SearchCommand = new RelayCommand<GsxSearchRow>(Search);
        if (!Update(board))
            throw new ArgumentException("The card is not on the board.", nameof(card));
    }

    /// <summary>The card's Packages other than GSX profiles, which the GSX part lists.</summary>
    public ObservableCollection<PackageRowViewModel> Rows { get; } = [];

    /// <summary>A provisional card is read-only (spec 6.5).</summary>
    public bool CanEdit => card.Id is not null;

    /// <summary>Split needs a stored card with at least two Packages that are not GSX profiles.</summary>
    public bool CanSplit => CanEdit && Rows.Count >= 2;

    public bool SwitchOn => CardSwitch.IsOn(card);

    public bool CanToggle => SwitchReason is null;

    /// <summary>Why the switch is disabled: the sim text wins over "nothing to switch".</summary>
    public string? SwitchReason => simRefused ? SimRunningException.Text : CardSwitch.HasSwitchable(card) ? null : NothingToSwitch;

    public IAsyncRelayCommand ToggleCommand { get; }

    /// <summary>Removes a Package row or a GSX profile row; does nothing for a row without a <c>Key</c>.</summary>
    public IAsyncRelayCommand<IRemovableRow> RemoveCommand { get; }

    public IAsyncRelayCommand<PackageRowViewModel> SplitCommand { get; }

    /// <summary>Saves the name, then shows another card of the dialog's last board.</summary>
    public IAsyncRelayCommand<CardRef> ShowCardCommand { get; }

    /// <summary>Opens a GSX search link; a link the browser cannot open lights the warning on that ICAO code.</summary>
    public IRelayCommand<GsxSearchRow> SearchCommand { get; }

    /// <summary>
    /// Shows the card as it is on <paramref name="board"/>; <c>false</c> when the card is gone. <see cref="Message"/>, the search errors and a name
    /// box that has focus or holds changed text are kept.
    /// </summary>
    public bool Update(Board board)
    {
        if (cardRef.In(board) is not { } found)
            return false;

        this.board = board;
        card = found;
        cardRef = CardRef.Of(found);
        RefreshName();
        RefreshImages();
        Rows.Clear();
        foreach (var package in found.Packages.Where(p => p.Kind != PackageKind.GsxProfile))
            Rows.Add(new PackageRowViewModel(package));
        Info = CardInfoViewModel.Build(found, board, session.Airports);
        Gsx = GsxSectionViewModel.Build(found, board, searchErrors);
        Refresh();
        return true;
    }

    private async Task ToggleAsync() => await SendAsync(() => session.SwitchCardAsync(cardRef));

    private async Task RemoveAsync(IRemovableRow? row)
    {
        Message = null;
        if (row?.Key is not { } key || !services.Confirm("Remove", $"Remove {row.FolderName} and forget it?"))
            return;
        await SendAsync(() => session.RemoveAsync(cardRef, key));
    }

    private async Task SplitAsync(PackageRowViewModel? row)
    {
        if (row is not null)
            await SendAsync(() => session.SplitAsync(cardRef, row.Key));
    }

    private async Task ShowCardAsync(CardRef? target)
    {
        var refused = await CommitNameAsync();
        // Esc or a close may have detached the dialog while the name was saved.
        if (detached)
            return;
        if (refused is not null)
            notice(refused);
        if (target?.In(board) is not { } found)
            return;

        // Whatever the save returned, nothing of the old card is carried over.
        Message = null;
        searchErrors.Clear();
        ResetCodeField();
        cardRef = CardRef.Of(found);
        Update(board);
        FillName();
    }

    private void Search(GsxSearchRow? row)
    {
        if (row is null)
            return;
        try
        {
            services.OpenLink(row.Link);
        }
        catch (Exception e)
        {
            // Never a popup: the error is a warning icon after that search button, until another card is shown.
            searchErrors[row.Code] = e.Message;
            Gsx = GsxSectionViewModel.Build(card, board, searchErrors);
        }
    }

    /// <summary>Clears the message and the notice, sends one change and shows its result.</summary>
    private async Task SendAsync(Func<Task<string?>> change)
    {
        Message = null;
        changeStarting();
        var result = await change();
        // The sim text is shown as SwitchReason; showing it as Message too would repeat it.
        if (result == SimRunningException.Text)
            simRefused = true;
        else
            Message = result;
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(SwitchOn));
        OnPropertyChanged(nameof(SwitchReason));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanSplit));
        OnPropertyChanged(nameof(ShowCodes));
        OnPropertyChanged(nameof(CodeChips));
        ToggleCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        SplitCommand.NotifyCanExecuteChanged();
        SetImageCommand.NotifyCanExecuteChanged();
        ChooseImageCommand.NotifyCanExecuteChanged();
        AddCodeCommand.NotifyCanExecuteChanged();
        PickSuggestionCommand.NotifyCanExecuteChanged();
        RemoveCodeCommand.NotifyCanExecuteChanged();
    }
}
