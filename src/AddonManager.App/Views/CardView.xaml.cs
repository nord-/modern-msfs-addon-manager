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

    private bool pressed;

    /// <summary>Starts a click: only a press that began on this card can open it. The mouse is captured so the release comes back here.</summary>
    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        pressed = true;
        Frame.CaptureMouse();
    }

    /// <summary>Ends the click: opens the card dialog only when the press began on this card and the release is still over it, so a click that
    /// closed the dialog's scrim (press) never opens the card under the pointer (release). An InputBinding cannot reach the window's view model by RelativeSource, so this is forwarded here.</summary>
    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        var click = pressed && Frame.IsMouseOver;
        pressed = false;
        Frame.ReleaseMouseCapture();
        if (click && DataContext is CardViewModel card && Window.GetWindow(this)?.DataContext is MainViewModel main)
            main.OpenCardCommand.Execute(card);
    }

    private void OnCaptureLost(object sender, MouseEventArgs e) => pressed = false;
}
