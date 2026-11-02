using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AddonManager.Core.Cards;
using Wpf.Ui.Controls;

namespace AddonManager.App.Views;

/// <summary>The traffic light colour of a card: red Off, yellow Mixed, green On, grey Locked.</summary>
public sealed class StateBrushConverter : IValueConverter
{
    private static readonly Brush Red = Frozen("#E5484D");
    private static readonly Brush Yellow = Frozen("#F5C518");
    private static readonly Brush Green = Frozen("#30A46C");
    private static readonly Brush Grey = Frozen("#8B8D98");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CardState.Off => Red,
        CardState.Mixed => Yellow,
        CardState.On => Green,
        _ => Grey,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    private static Brush Frozen(string colour)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour));
        brush.Freeze();
        return brush;
    }
}

/// <summary>The placeholder symbol of a card's tab.</summary>
public sealed class TabSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CardTab.Airports => SymbolRegular.AirplaneTakeOff24,
        CardTab.Landscapes => SymbolRegular.Image24,
        CardTab.Assets => SymbolRegular.Box24,
        CardTab.Aircraft => SymbolRegular.Airplane24,
        CardTab.Instruments => SymbolRegular.Gauge24,
        _ => SymbolRegular.Apps24,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible when the value is a non-empty string or a non-null object; with parameter <c>Invert</c> the other way round.</summary>
public sealed class PresentToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var present = value is string s ? s.Length > 0 : value is not null;
        if (parameter is "Invert")
            present = !present;
        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible when the card state is <see cref="CardState.Locked"/>.</summary>
public sealed class LockedToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is CardState.Locked ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Joins a list of strings with a separator (the parameter, default " · ").</summary>
public sealed class JoinConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is IEnumerable<string> items ? string.Join(parameter as string ?? " · ", items) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
