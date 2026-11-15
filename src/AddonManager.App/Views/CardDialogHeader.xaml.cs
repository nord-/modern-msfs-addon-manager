using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AddonManager.App.ViewModels;

namespace AddonManager.App.Views;

/// <summary>The card dialog's header (spec 3): image, image menu, name, ICAO codes and the switch. Inherits the <c>CardDialogViewModel</c>.</summary>
public partial class CardDialogHeader : UserControl
{
    public CardDialogHeader() => InitializeComponent();

    private CardDialogViewModel? Dialog => DataContext as CardDialogViewModel;

    private void OnImageClick(object sender, RoutedEventArgs e)
    {
        var menu = ImageButton.ContextMenu;
        menu.PlacementTarget = ImageButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private async void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Dialog is not { } dialog)
            return;
        e.Handled = true;
        if (await dialog.CommitNameAsync() is { } refused)
            dialog.Message = refused;
    }

    private void OnNameGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Dialog is { } dialog)
            dialog.NameFocused = true;
    }

    private async void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Dialog is { } dialog)
            await dialog.NameLostFocusAsync();
    }

    private void OnCodePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Dialog is not { } dialog)
            return;
        switch (e.Key)
        {
            case Key.Up:
                dialog.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Down:
                dialog.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Enter:
                // Execute does not check CanExecute; a provisional card adds nothing.
                if (dialog.AddCodeCommand.CanExecute(null))
                    dialog.AddCodeCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                // Esc closes the list first; with the list closed it reaches the window and closes the dialog.
                e.Handled = dialog.CloseSuggestions();
                break;
        }
    }

    private void OnCodeLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Dialog?.CloseSuggestions();

    private void OnSuggestionPress(object sender, MouseButtonEventArgs e)
    {
        // The item under the pointer, not SelectedItem: a non-focusable item never becomes selected by a click.
        if (Dialog is not { } dialog || ItemsControl.ContainerFromElement((ItemsControl)sender, (DependencyObject)e.OriginalSource) is not ListBoxItem item)
            return;
        e.Handled = true;
        if (dialog.PickSuggestionCommand.CanExecute(item.DataContext))
            dialog.PickSuggestionCommand.Execute(item.DataContext);
    }
}
