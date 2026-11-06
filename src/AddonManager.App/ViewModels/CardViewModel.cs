using System.Windows.Media;
using AddonManager.App.Session;
using AddonManager.Core.Cards;
using AddonManager.Core.Content;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AddonManager.App.ViewModels;

/// <summary>One card on the wall. Refreshed in place by <see cref="Update"/> so the wall keeps its scroll position.</summary>
public sealed partial class CardViewModel : ObservableObject
{
    [ObservableProperty]
    private string? code;

    [ObservableProperty]
    private string? codesLine;

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string? subtitle;

    [ObservableProperty]
    private string? typeDesignators;

    [ObservableProperty]
    private string packageCount = "";

    [ObservableProperty]
    private string? gsx;

    [ObservableProperty]
    private IReadOnlyList<string> warnings = [];

    /// <summary>What the card shows of its warnings: the one warning, or a count; the full list is the tooltip. Keeps the code and name in view.</summary>
    [ObservableProperty]
    private string warningsSummary = "";

    [ObservableProperty]
    private IReadOnlyList<string> labels = [];

    [ObservableProperty]
    private CardState light;

    [ObservableProperty]
    private string? imagePath;

    [ObservableProperty]
    private CardTab tab;

    /// <summary>The thumbnail; set by the loader once <see cref="ImagePath"/> has been read.</summary>
    [ObservableProperty]
    private ImageSource? image;

    public CardViewModel(Card card, string? subtitle, bool showGsx)
    {
        Key = KeyOf(card);
        Update(card, subtitle, showGsx);
    }

    /// <summary>What identifies the card across boards: its id, or its Package for a provisional card.</summary>
    public string Key { get; }

    /// <summary>Names the card for the session, refreshed by <see cref="Update"/>.</summary>
    public CardRef Ref { get; private set; } = null!;

    /// <summary>UI Automation falls back to this for the card's name: the code and the name, as the card shows them.</summary>
    public override string ToString() => Code is null ? Name : $"{Code} {Name}";

    public static string KeyOf(Card card)
        => card.Id is { } id ? $"#{id}" : $"P:{card.ProvisionalPackage?.Kind}:{card.ProvisionalPackage?.Name.Value.ToUpperInvariant()}";

    public void Update(Card card, string? subtitle, bool showGsx)
    {
        Ref = CardRef.Of(card);
        Code = card.Several ? "(several)" : card.Codes.Count == 1 ? card.Codes[0].Value : null;
        CodesLine = card.Several ? string.Join(", ", card.Codes.Select(c => c.Value)) : null;
        Name = card.Name;
        Subtitle = subtitle;
        TypeDesignators = card.Section == Category.Aircraft && card.TypeDesignators.Count > 0 ? string.Join(", ", card.TypeDesignators) : null;
        PackageCount = CardText.PackageCount(card.Packages.Count);
        Gsx = showGsx ? CardText.GsxMark(card.Gsx) : null;
        Warnings = Same(Warnings, [.. card.Warnings.Select(CardText.Warning)]);
        WarningsSummary = Warnings.Count switch { 0 => "", 1 => Warnings[0], var n => $"{n} warnings" };
        Labels = Same(Labels, [.. LabelsOf(card)]);
        Light = card.State;
        ImagePath = card.ImagePath;
        Tab = card.Tab;
    }

    private static IEnumerable<string> LabelsOf(Card card)
    {
        if (card.BuiltIns.Count > 0)
            yield return "Built-in";
        if (card.Pending)
            yield return "Next sim start";
    }

    /// <summary>Keeps the old list when the new one has the same items, so an unchanged card raises no change.</summary>
    private static IReadOnlyList<string> Same(IReadOnlyList<string> old, IReadOnlyList<string> next) => old.SequenceEqual(next) ? old : next;
}
