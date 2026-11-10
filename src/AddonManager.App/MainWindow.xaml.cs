using System.Windows;
using System.Windows.Controls;
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
