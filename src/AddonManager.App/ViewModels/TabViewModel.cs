using AddonManager.Core.Cards;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AddonManager.App.ViewModels;

/// <summary>One tab above the wall: "Airports (12)".</summary>
public sealed partial class TabViewModel(CardTab tab) : ObservableObject
{
    public CardTab Tab { get; } = tab;

    [ObservableProperty]
    private string header = tab.ToString();

    [ObservableProperty]
    private bool isSelected;

    /// <summary>UI Automation falls back to this for the tab item's name.</summary>
    public override string ToString() => Header;
}
