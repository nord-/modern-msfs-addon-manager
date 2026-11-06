using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AddonManager.App.ViewModels;

namespace AddonManager.App.Views;

/// <summary>One card of the wall; bound to a <c>CardViewModel</c>. Clips its content to the rounded corners.</summary>
public partial class CardView : UserControl
{
    private const double Radius = 8;

    public CardView()
    {
        InitializeComponent();
        Frame.SizeChanged += (_, e) => Frame.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), Radius, Radius);
    }

    /// <summary>A left click opens the card dialog. An InputBinding cannot reach the window's view model by RelativeSource, so the click is forwarded here.</summary>
    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CardViewModel card && Window.GetWindow(this)?.DataContext is MainViewModel main)
            main.OpenCardCommand.Execute(card);
    }
}
