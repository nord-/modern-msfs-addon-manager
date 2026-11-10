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
    private Point start;

    private MainViewModel? Main => Window.GetWindow(this)?.DataContext as MainViewModel;

    /// <summary>Starts a click: only a press that began on this card can open it. The mouse is captured so the release comes back here.</summary>
    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        pressed = true;
        start = e.GetPosition(Frame);
        Frame.CaptureMouse();
    }

    /// <summary>Ends the click: opens the card dialog only when the press began on this card and the release is still over it, so a click that
    /// closed the dialog's scrim (press) never opens the card under the pointer (release). An InputBinding cannot reach the window's view model by RelativeSource, so this is forwarded here.</summary>
    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        var click = pressed && Frame.IsMouseOver;
        pressed = false;
        Frame.ReleaseMouseCapture();
        if (click && DataContext is CardViewModel card && Main is { } main)
            main.OpenCardCommand.Execute(card);
    }

    private void OnCaptureLost(object sender, MouseEventArgs e) => pressed = false;

    /// <summary>A press that moves past the system drag distance starts a drag instead of a click. A provisional card cannot be dragged,
    /// so it still opens on a click that moved a little.</summary>
    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!pressed || DataContext is not CardViewModel { CanDrag: true } card)
            return;
        var moved = e.GetPosition(Frame) - start;
        if (Math.Abs(moved.X) <= SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) <= SystemParameters.MinimumVerticalDragDistance)
            return;
        pressed = false;
        Frame.ReleaseMouseCapture();
        DragDrop.DoDragDrop(Frame, new DataObject(typeof(CardViewModel), card), DragDropEffects.Move);
    }

    private void OnDragOver(object sender, DragEventArgs e)
        => DropHighlight.Over(e, Frame, DropHighlight.Dragged(e) is { } dragged && DataContext is CardViewModel target && Main?.CanDropOnCard(dragged, target) == true);

    private void OnDragLeave(object sender, DragEventArgs e) => DropHighlight.Leave(e, Frame, Frame);

    /// <summary>The merge runs after <c>DoDragDrop</c> has returned, so its confirm prompt does not show inside the drag.</summary>
    private void OnDrop(object sender, DragEventArgs e)
    {
        DropHighlight.Clear(Frame);
        e.Handled = true;
        if (DropHighlight.Dragged(e) is { } dragged && DataContext is CardViewModel target && Main is { } main)
            Dispatcher.BeginInvoke(new Action(() => _ = main.DropOnCardAsync(dragged, target)));
    }
}
