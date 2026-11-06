using System.Windows;
using System.Windows.Controls;
using AddonManager.App.ViewModels;
using AddonManager.Core.Sync;

namespace AddonManager.App.Views;

/// <summary>The sync dialog panel (spec 7.4); bound to a <c>SyncDialogViewModel</c>. It has no close control: only Apply ends it.</summary>
public partial class SyncDialog : UserControl
{
    public SyncDialog()
    {
        InitializeComponent();
    }

    private void OnKeepAll(object sender, RoutedEventArgs e) => (DataContext as SyncDialogViewModel)?.SetAll(SyncChoice.KeepCommunity);

    private void OnRestoreAll(object sender, RoutedEventArgs e) => (DataContext as SyncDialogViewModel)?.SetAll(SyncChoice.RestoreApp);
}
