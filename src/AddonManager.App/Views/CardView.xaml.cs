using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
}
