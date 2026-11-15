using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AddonManager.App.ViewModels;
using AddonManager.App.Views;
using Wpf.Ui.Controls;

namespace AddonManager.App;

/// <summary>The main window: the menu, the banner, the tabs, the card wall and the overlays. All state comes from the view models.</summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel main, FirstRunViewModel firstRun)
    {
        InitializeComponent();
        DataContext = main;
        FirstRun.DataContext = firstRun;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>The open Yes/No question, if any.</summary>
    private ContentDialog? asking;

    /// <summary>Asks Yes or No in the app's own dialog over the window; Yes is the default button and Esc answers No.</summary>
    public async Task<bool> ConfirmAsync(string caption, string text)
    {
        var dialog = new ContentDialog(DialogHost)
        {
            Title = caption,
            Content = text,
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            DefaultButton = ContentDialogButton.Primary,
        };
        asking = dialog;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            asking = null;
        }
    }

    /// <summary>
    /// ContentDialog handles no keys, so Esc would reach the window's Escape binding and close the card dialog behind the question.
    /// While a question is open, Esc answers No and goes no further, wherever the focus is.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && asking is { } dialog)
        {
            e.Handled = true;
            dialog.Hide(ContentDialogResult.None);
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private MainViewModel Main => (MainViewModel)DataContext;

    // A card dropped on a tab header: the header's Border holds the TabViewModel and draws the drop border.
    private void OnTabDragOver(object sender, DragEventArgs e)
    {
        var border = (Border)sender;
        DropHighlight.Over(e, border, DropHighlight.Dragged(e) is { } dragged && border.DataContext is TabViewModel tab && Main.CanDropOnTab(dragged, tab));
    }

    private void OnTabDragLeave(object sender, DragEventArgs e) => DropHighlight.Leave(e, (Border)sender, (Border)sender);

    private void OnTabDrop(object sender, DragEventArgs e)
    {
        var border = (Border)sender;
        DropHighlight.Clear(border);
        e.Handled = true;
        if (DropHighlight.Dragged(e) is { } dragged && border.DataContext is TabViewModel tab)
            Dispatcher.BeginInvoke(new Action(() => _ = Main.DropOnTabAsync(dragged, tab)));
    }

    // A card dropped on a menu item: the item's Tag is its section, and the Border it holds draws the drop border.
    private void OnSectionDragOver(object sender, DragEventArgs e)
    {
        var item = (ListBoxItem)sender;
        if (item.Content is Border border)
            DropHighlight.Over(e, border, DropHighlight.Dragged(e) is { } dragged && item.Tag is MenuSection section && Main.CanDropOnSection(dragged, section));
    }

    private void OnSectionDragLeave(object sender, DragEventArgs e)
    {
        var item = (ListBoxItem)sender;
        if (item.Content is Border border)
            DropHighlight.Leave(e, item, border);
    }

    private void OnSectionDrop(object sender, DragEventArgs e)
    {
        var item = (ListBoxItem)sender;
        if (item.Content is Border border)
            DropHighlight.Clear(border);
        e.Handled = true;
        if (DropHighlight.Dragged(e) is { } dragged && item.Tag is MenuSection section)
            Dispatcher.BeginInvoke(new Action(() => _ = Main.DropOnSectionAsync(dragged, section)));
    }
}
