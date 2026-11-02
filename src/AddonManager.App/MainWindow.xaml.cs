using System.Windows;
using AddonManager.App.ViewModels;
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
}
