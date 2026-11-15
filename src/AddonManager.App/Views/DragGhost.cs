using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AddonManager.App.Views;

/// <summary>
/// A see-through picture of the dragged card that follows the pointer over the window while <c>DoDragDrop</c> runs. It never takes hits,
/// so the target under the pointer still gets the drag events. Mouse events stop during a drag, so the window's content takes drags
/// while the ghost is shown: its PreviewDragOver moves the picture, and a drag over anything that is not a target shows "no drop".
/// </summary>
internal sealed class DragGhost : Adorner
{
    private readonly Rectangle copy;
    private readonly Point grab;
    private readonly UIElement root;
    private readonly bool rootAllowedDrop;
    private Point at = new(double.NaN, double.NaN);

    private DragGhost(UIElement root, FrameworkElement card, Point grab, double radius) : base(root)
    {
        this.root = root;
        this.grab = grab;
        copy = new Rectangle
        {
            Width = card.ActualWidth,
            Height = card.ActualHeight,
            RadiusX = radius,
            RadiusY = radius,
            Fill = new ImageBrush(Snapshot(card)),
            Opacity = 0.8,
            IsHitTestVisible = false,
        };
        IsHitTestVisible = false;
        AddVisualChild(copy);

        rootAllowedDrop = root.AllowDrop;
        root.AllowDrop = true;
        root.PreviewDragOver += OnPreviewDragOver;
        root.DragOver += OnUnhandledDragOver;
    }

    /// <summary>Shows the picture of <paramref name="card"/> over the window's content, held at <paramref name="grab"/> (the press point on the card);
    /// <c>null</c> when the window has no adorner layer.</summary>
    public static DragGhost? Show(FrameworkElement card, Point grab, double radius)
    {
        if (Window.GetWindow(card)?.Content is not UIElement root || AdornerLayer.GetAdornerLayer(root) is not { } layer)
            return null;
        var ghost = new DragGhost(root, card, grab, radius);
        layer.Add(ghost);
        return ghost;
    }

    public void Remove()
    {
        root.PreviewDragOver -= OnPreviewDragOver;
        root.DragOver -= OnUnhandledDragOver;
        root.AllowDrop = rootAllowedDrop;
        AdornerLayer.GetAdornerLayer(root)?.Remove(this);
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        at = e.GetPosition(root);
        InvalidateArrange();
    }

    /// <summary>A drop target marks its DragOver handled; one that reaches the window's content is over no target.</summary>
    private static void OnUnhandledDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>A picture of the card as it looks now, so dimming the card on the wall during the drag leaves the copy as it was.
    /// Drawn through a <c>DrawingVisual</c> so the card's place in the wall does not shift the picture.</summary>
    private static BitmapSource Snapshot(FrameworkElement card)
    {
        var dpi = VisualTreeHelper.GetDpi(card);
        var size = new Size(card.ActualWidth, card.ActualHeight);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(card), null, new Rect(size));
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => copy;

    protected override Size MeasureOverride(Size constraint)
    {
        copy.Measure(constraint);
        return constraint;
    }

    /// <summary>Hidden until the first DragOver gives the pointer's place.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        copy.Arrange(double.IsNaN(at.X) ? new Rect(-copy.Width * 2, 0, copy.Width, copy.Height) : new Rect(at.X - grab.X, at.Y - grab.Y, copy.Width, copy.Height));
        return finalSize;
    }
}
