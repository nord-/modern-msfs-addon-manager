using System.Collections.ObjectModel;
using AddonManager.App.Session;
using AddonManager.Core.Cards;
using AddonManager.Core.Linking;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AddonManager.App.ViewModels;

/// <summary>
/// The card dialog: the card's Packages, its one switch and Remove. The view binds <c>IsChecked</c> OneWay to <see cref="SwitchOn"/> and lets the click
/// run <see cref="ToggleCommand"/>; the command always raises <c>PropertyChanged(SwitchOn)</c> when it completes, so a toggle that changed nothing snaps back.
/// </summary>
public sealed partial class CardDialogViewModel : ObservableObject
{
    private const string NothingToSwitch = "No package on this card can be switched";

    private readonly IAppSession session;
    private readonly Func<string, bool> confirm;
    private CardRef cardRef;
    private Card card = null!;
    private bool simRefused;

    [ObservableProperty]
    private string title = "";

    [ObservableProperty]
    private string? codes;

    [ObservableProperty]
    private string? message;

    public CardDialogViewModel(IAppSession session, Board board, CardRef card, Func<string, bool> confirm)
    {
        this.session = session;
        this.confirm = confirm;
        cardRef = card;
        simRefused = session.IsSimRunning;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => CanToggle);
        RemoveCommand = new AsyncRelayCommand<PackageRowViewModel>(RemoveAsync, row => row is { CanRemove: true } && !simRefused);
        Update(board);
    }

    public ObservableCollection<PackageRowViewModel> Rows { get; } = [];

    public bool SwitchOn => CardSwitch.IsOn(card);

    public bool CanToggle => SwitchReason is null;

    /// <summary>Why the switch is disabled: the sim text wins over "nothing to switch".</summary>
    public string? SwitchReason => simRefused ? SimRunningException.Text : CardSwitch.HasSwitchable(card) ? null : NothingToSwitch;

    public IAsyncRelayCommand ToggleCommand { get; }

    public IAsyncRelayCommand<PackageRowViewModel> RemoveCommand { get; }

    /// <summary>Raised when the card is gone from the board.</summary>
    public event Action? Closed;

    /// <summary>Shows the card as it is on <paramref name="board"/>; <c>false</c> (and <see cref="Closed"/>) when the card is gone. <see cref="Message"/> is kept.</summary>
    public bool Update(Board board)
    {
        if (cardRef.In(board) is not { } found)
        {
            Closed?.Invoke();
            return false;
        }

        card = found;
        cardRef = CardRef.Of(found);
        Title = found.Name;
        Codes = found.Codes.Count == 0 ? null : string.Join(", ", found.Codes.Select(c => c.Value));
        Rows.Clear();
        foreach (var package in found.Packages)
            Rows.Add(new PackageRowViewModel(package));
        Refresh();
        return true;
    }

    private async Task ToggleAsync()
    {
        Message = null;
        await Apply(session.SwitchCardAsync(cardRef));
    }

    private async Task RemoveAsync(PackageRowViewModel? row)
    {
        Message = null;
        if (row is null || !confirm($"Remove {row.FolderName} and forget it?"))
            return;
        await Apply(session.RemoveAsync(cardRef, row.Key));
    }

    private async Task Apply(Task<string?> change)
    {
        var result = await change;
        Message = result;
        if (result == SimRunningException.Text)
            simRefused = true;
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(SwitchOn));
        OnPropertyChanged(nameof(SwitchReason));
        OnPropertyChanged(nameof(CanToggle));
        ToggleCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }
}
