using System.Windows.Media;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using AddonManager.Core.Packages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AddonManager.App.ViewModels;

/// <summary>
/// The dialog's header (spec 3): the name box and the image menu. The name is saved only when its trimmed text differs from the text the box
/// was last filled with from the card, so clicking through the box never pins the computed name.
/// </summary>
public sealed partial class CardDialogViewModel
{
    private string filledName = "";
    private bool detached;
    private (Task<string?> Task, string Sent)? pendingName;

    /// <summary>The name box; two-way.</summary>
    [ObservableProperty]
    private string nameText = "";

    /// <summary>The image of the card, or <c>null</c> for the placeholder of <see cref="Tab"/>.</summary>
    [ObservableProperty]
    private ImageSource? image;

    /// <summary>The card's tab, for the image placeholder.</summary>
    [ObservableProperty]
    private CardTab tab;

    /// <summary>One item per Package of the card that has a thumbnail.</summary>
    [ObservableProperty]
    private IReadOnlyList<ImageChoice> imageChoices = [];

    /// <summary>The ICAO code text box; two-way, on every keystroke. Each change recomputes <see cref="Suggestions"/>.</summary>
    [ObservableProperty]
    private string codeText = "";

    /// <summary>The refusal of the last code the user tried to add, shown under the code field; the text is kept.</summary>
    [ObservableProperty]
    private string? codeMessage;

    /// <summary>The known Airports that match <see cref="CodeText"/>, at most ten.</summary>
    [ObservableProperty]
    private IReadOnlyList<Airport> suggestions = [];

    /// <summary>The highlighted suggestion; Enter adds it while the list is open.</summary>
    [ObservableProperty]
    private Airport? selectedSuggestion;

    [ObservableProperty]
    private bool suggestionsOpen;

    /// <summary>The code field belongs to Scenery cards only.</summary>
    public bool ShowCodes => card.Section == Category.Scenery;

    /// <summary>The ICAO codes of the card, shown as chips.</summary>
    public IReadOnlyList<AirportCode> CodeChips => card.Codes;

    /// <summary>Adds the selected suggestion while the list is open, else the typed text.</summary>
    public IAsyncRelayCommand AddCodeCommand { get; }

    /// <summary>Adds the code of a clicked suggestion.</summary>
    public IAsyncRelayCommand<Airport> PickSuggestionCommand { get; }

    /// <summary>Removes the code of a chip.</summary>
    public IAsyncRelayCommand<AirportCode> RemoveCodeCommand { get; }

    /// <summary>Set by the view when the name box gets and loses keyboard focus. A new board does not refresh a focused box.</summary>
    public bool NameFocused { get; set; }

    /// <summary>Sets the user's image to a path; <c>null</c> brings the automatic image back.</summary>
    public IAsyncRelayCommand<string?> SetImageCommand { get; }

    /// <summary>Lets the user pick an image file and sets it; a cancelled pick does nothing.</summary>
    public IAsyncRelayCommand ChooseImageCommand { get; }

    /// <summary>
    /// Saves the name (Enter, close, card switch) and returns the refusal, or <c>null</c>. A commit that starts while another is in flight awaits it
    /// and sends only when the text still differs from both the filled text and the text that call sent; otherwise it returns that call's result.
    /// After <see cref="Detach"/> it does nothing.
    /// </summary>
    public async Task<string?> CommitNameAsync()
    {
        string? result = null;
        while (!detached)
        {
            var text = NameText.Trim();
            if (pendingName is { Task.IsCompleted: false } running)
            {
                result = await running.Task;
                text = NameText.Trim();
                if (text == filledName || text == running.Sent)
                    return result;
                continue;
            }

            if (text == filledName)
                return result;
            var send = SendNameAsync(text);
            pendingName = (send, text);
            return await send;
        }

        return result;
    }

    /// <summary>The view's only focus-loss entry: commits, shows a refusal as <see cref="Message"/>, then refreshes the box as a new board would.</summary>
    public async Task NameLostFocusAsync()
    {
        NameFocused = false;
        if (await CommitNameAsync() is { } refused)
            Message = refused;
        RefreshName();
    }

    /// <summary>Drops the text not yet saved (Esc).</summary>
    public void DiscardName() => NameText = filledName;

    /// <summary>The dialog is closing: no later commit sends anything, so the unloading name box saves nothing.</summary>
    public void Detach() => detached = true;

    private async Task<string?> SendNameAsync(string text)
    {
        Message = null;
        changeStarting();
        var result = await session.RenameAsync(cardRef, text.Length == 0 ? null : text);
        if (result is null)
            filledName = text;
        return result;
    }

    private void RefreshName()
    {
        if (!NameFocused && NameText.Trim() == filledName)
            FillName();
    }

    private void FillName()
    {
        filledName = card.Name;
        NameText = card.Name;
    }

    private void ResetCodeField()
    {
        CodeText = "";
        CodeMessage = null;
        SuggestionsOpen = false;
    }

    partial void OnCodeTextChanged(string value)
    {
        // A refusal is about the text that was sent; typing makes it stale.
        CodeMessage = null;
        Suggestions = AirportSuggestions.Find(session.Airports, value);
        SelectedSuggestion = Suggestions.Count > 0 ? Suggestions[0] : null;
        SuggestionsOpen = Suggestions.Count > 0;
    }

    /// <summary>Moves the highlight by <paramref name="delta"/> within the open list, never past either end.</summary>
    public void MoveSelection(int delta)
    {
        if (!SuggestionsOpen || Suggestions.Count == 0)
            return;
        var index = SelectedSuggestion is null ? -1 : Suggestions.ToList().IndexOf(SelectedSuggestion);
        SelectedSuggestion = Suggestions[Math.Clamp(index + delta, 0, Suggestions.Count - 1)];
    }

    /// <summary>Closes the list; <c>true</c> when it was open, so the view marks Esc handled.</summary>
    public bool CloseSuggestions()
    {
        var wasOpen = SuggestionsOpen;
        SuggestionsOpen = false;
        return wasOpen;
    }

    private Task AddSelectedOrTypedAsync()
        => AddCodeAsync(SuggestionsOpen && SelectedSuggestion is { } selected ? selected.Code.Value : CodeText);

    private Task PickSuggestionAsync(Airport? airport) => airport is null ? Task.CompletedTask : AddCodeAsync(airport.Code.Value);

    private async Task AddCodeAsync(string text)
    {
        CodeMessage = null;
        (CodeOutcome? Outcome, string? Message) result = default;
        await SendAsync(async () =>
        {
            result = await session.AddCodeAsync(cardRef, text);
            return result.Message;
        });
        switch (result.Outcome)
        {
            case { Result: CodeResult.Added }:
                CodeText = "";
                break;
            case { Result: CodeResult.Invalid }:
                CodeMessage = "Use 3–5 letters or digits.";
                break;
            case { Result: CodeResult.Taken } taken:
                var code = text.Trim().ToUpperInvariant();
                CodeMessage = taken.HolderName is { } holder ? $"{code} is already on {holder}." : $"{code} is already on another card.";
                break;
        }
    }

    private Task RemoveCodeAsync(AirportCode code) => SendAsync(() => session.RemoveCodeAsync(cardRef, code));

    /// <summary>Loads the card's image again on every board, so a file that is gone shows the placeholder, and rebuilds the image menu.</summary>
    private void RefreshImages()
    {
        Tab = card.Tab;
        _ = LoadImageAsync(card.ImagePath);
        ImageChoices = [.. card.Packages.Where(p => p.ThumbnailPath is not null).Select(p => new ImageChoice(p.DisplayName, p.ThumbnailPath!))];
        foreach (var choice in ImageChoices)
            _ = LoadChoiceAsync(choice);
    }

    private async Task LoadImageAsync(string? path)
    {
        var result = await TryLoadAsync(path);
        if (card.ImagePath == path)
            Image = result;
    }

    private async Task LoadChoiceAsync(ImageChoice choice) => choice.Image = await TryLoadAsync(choice.Path);

    private async Task<ImageSource?> TryLoadAsync(string? path)
    {
        if (path is null)
            return null;
        try
        {
            return await services.LoadImage(path);
        }
        catch (Exception)
        {
            // An image that cannot be loaded shows the placeholder; the next board tries again.
            return null;
        }
    }

    private Task SetImageAsync(string? path) => SendAsync(() => session.SetImageAsync(cardRef, path));

    private async Task ChooseImageAsync()
    {
        if (services.PickImage() is { } path)
            await SetImageAsync(path);
    }
}

/// <summary>An item of the image menu: a Package's display name and thumbnail.</summary>
public sealed partial class ImageChoice(string displayName, string path) : ObservableObject
{
    public string DisplayName { get; } = displayName;

    public string Path { get; } = path;

    [ObservableProperty]
    private ImageSource? image;
}
