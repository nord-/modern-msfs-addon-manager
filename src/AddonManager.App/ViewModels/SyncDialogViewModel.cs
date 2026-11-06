using System.Collections.ObjectModel;
using AddonManager.App.Session;
using AddonManager.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AddonManager.App.ViewModels;

/// <summary>A group of sync rows under one card (or "No card yet" / "Not shown").</summary>
public sealed record SyncGroupViewModel(string Title, IReadOnlyList<SyncRowViewModel> Rows);

/// <summary>The sync dialog (spec 7.4): rows grouped by card, a choice per row, and Apply.</summary>
public sealed partial class SyncDialogViewModel : ObservableObject
{
    private const string NoCard = "No card yet";
    private const string NotShown = "Not shown";

    private readonly IAppSession session;

    [ObservableProperty]
    private string? message;

    public SyncDialogViewModel(IAppSession session)
    {
        this.session = session;
        ApplyCommand = new AsyncRelayCommand(ApplyAsync);
    }

    public ObservableCollection<SyncGroupViewModel> Groups { get; } = [];

    public IAsyncRelayCommand ApplyCommand { get; }

    /// <summary>Shows the rows of a new discovery. Rows still listed with the same Package and reason keep their choice.</summary>
    public void Replace(SyncPrompt prompt)
    {
        var old = Groups.SelectMany(g => g.Rows).ToDictionary(r => KeyOf(r.Row), r => r.Choice);
        var board = prompt.Board;

        // Grouped by CardRef, not by title: two cards with the same name stay apart (D7).
        var cardGroups = new Dictionary<CardRef, List<SyncRowViewModel>>();
        var noCard = new List<SyncRowViewModel>();
        var notShown = new List<SyncRowViewModel>();
        foreach (var row in prompt.Rows)
        {
            var card = board.Cards.FirstOrDefault(c => c.Packages.Any(p => p.Name == row.Package));
            var hidden = board.NotShown.FirstOrDefault(n => n.Name == row.Package);
            var name = card?.Packages.First(p => p.Name == row.Package).DisplayName ?? hidden?.DisplayName ?? row.Package.Value;
            var vm = new SyncRowViewModel(row, name);
            if (old.TryGetValue(KeyOf(row), out var choice) && (choice == SyncChoice.KeepCommunity || vm.CanRestore))
                vm.Choice = choice;

            if (card is not null)
            {
                var key = CardRef.Of(card);
                if (!cardGroups.TryGetValue(key, out var list))
                    cardGroups[key] = list = [];
                list.Add(vm);
            }
            else if (hidden is not null)
                notShown.Add(vm);
            else
                noCard.Add(vm);
        }

        Groups.Clear();
        foreach (var card in board.Cards)
            if (cardGroups.TryGetValue(CardRef.Of(card), out var list))
                Groups.Add(new SyncGroupViewModel(card.Name, list));
        if (noCard.Count > 0)
            Groups.Add(new SyncGroupViewModel(NoCard, noCard));
        if (notShown.Count > 0)
            Groups.Add(new SyncGroupViewModel(NotShown, notShown));
    }

    /// <summary>Sets every row that can take it; a row that can't be restored stays on Keep Community.</summary>
    public void SetAll(SyncChoice choice)
    {
        foreach (var row in Groups.SelectMany(g => g.Rows))
            if (choice == SyncChoice.KeepCommunity || row.CanRestore)
                row.Choice = choice;
    }

    private static SyncKey KeyOf(SyncRow row) => new(row.Package, row.Reason);

    private async Task ApplyAsync()
    {
        var choices = Groups.SelectMany(g => g.Rows).ToDictionary(r => KeyOf(r.Row), r => r.Choice);
        Message = await session.ResolveSyncAsync(choices);
    }
}
