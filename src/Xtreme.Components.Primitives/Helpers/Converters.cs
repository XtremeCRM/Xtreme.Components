using Microsoft.UI.Xaml.Data;
using Xtreme.Components.Core;

namespace Xtreme.Components.Primitives;

/// <summary>True → Visible, false → Collapsed. ConverterParameter "invert" flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is bool b && b;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>Picks <see cref="TrueBrush"/> or <see cref="FalseBrush"/> from a bool.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush? TrueBrush { get; set; }
    public Brush? FalseBrush { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isTrue = value is bool b && b;
        return (isTrue ? TrueBrush ?? FalseBrush : FalseBrush ?? TrueBrush) ?? new SolidColorBrush();
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// A <see cref="Color"/> becomes a <see cref="SolidColorBrush"/>. A null color falls back to the
/// brush resource named by ConverterParameter, resolved for the app's effective theme through
/// <see cref="ComponentHost.TryGetBrush"/>; a missing resource paints hot pink so the gap is visible.
/// </summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is Color c)
            return new SolidColorBrush(c);
        if (parameter is string key && ComponentHost.TryGetBrush(key) is { } b)
            return b;
        return new SolidColorBrush(Microsoft.UI.Colors.HotPink);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is SolidColorBrush brush ? brush.Color : Microsoft.UI.Colors.Transparent;
}

/// <summary>Multiplies a double by the numeric ConverterParameter (template size maths).</summary>
public sealed class MultiplyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is double d && double.TryParse(parameter?.ToString(), System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out var factor)
            ? d * factor
            : value;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
