using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AddonManager.App.ViewModels;

namespace AddonManager.App.Views;

/// <summary>The view side of a card drag shared by the wall, the tab headers and the menu: the dragged card and the accent border of a valid target.</summary>
internal static class DropHighlight
{
    /// <summary>The card being dragged, or <c>null</c> when the drag carries something else.</summary>
    public static CardViewModel? Dragged(DragEventArgs e) => e.Data.GetData(typeof(CardViewModel)) as CardViewModel;

    /// <summary>Shows "move" and the accent border when the target takes the drop, "no drop" and no border otherwise.</summary>
    public static void Over(DragEventArgs e, Border border, bool canDrop)
    {
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        if (canDrop)
        {
            border.BorderBrush = border.TryFindResource("AccentFillColorDefaultBrush") as Brush;
            border.BorderThickness = new Thickness(2);
        }
        else
        {
            Clear(border);
        }
    }

    /// <summary>DragLeave also fires when the pointer enters a child, so the border goes only when the pointer has left the element.</summary>
    public static void Leave(DragEventArgs e, FrameworkElement element, Border border)
    {
        var at = e.GetPosition(element);
        if (at.X < 0 || at.Y < 0 || at.X >= element.ActualWidth || at.Y >= element.ActualHeight)
            Clear(border);
    }

    public static void Clear(Border border)
    {
        border.ClearValue(Border.BorderBrushProperty);
        border.ClearValue(Border.BorderThicknessProperty);
    }
}
