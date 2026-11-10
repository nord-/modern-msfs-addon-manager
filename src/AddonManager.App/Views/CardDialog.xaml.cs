using System.Windows;
using System.Windows.Controls;
using AddonManager.App.ViewModels;

namespace AddonManager.App.Views;

/// <summary>The card dialog panel: a fixed header over one scrolling area; bound to a <c>CardDialogViewModel</c>. The window's overlay around it closes the dialog on a click outside.</summary>
public partial class CardDialog : UserControl
{
    public CardDialog()
    {
        InitializeComponent();
        // A click inside the panel must not reach the overlay, which closes the dialog. A click on an empty spot takes the focus,
        // so the name box loses it and saves.
        MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Focus();
        };
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is MainViewModel main)
            main.CloseCardCommand.Execute(null);
    }
}
