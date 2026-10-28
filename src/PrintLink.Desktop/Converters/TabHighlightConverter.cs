using System.Globalization;
using System.Windows.Data;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace PrintLink.Desktop.Converters;

/// <summary>
/// Converts the selected tab index into an active or inactive background brush.
/// </summary>
public sealed class TabBackgroundConverter : IValueConverter
{
    private static readonly MediaBrush ActiveBrush = new MediaSolidColorBrush(MediaColor.FromRgb(37, 99, 235)); // #2563EB
    private static readonly MediaBrush InactiveBrush = MediaBrushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return InactiveBrush;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase)
            ? ActiveBrush
            : InactiveBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts the selected tab index into an active or inactive foreground brush.
/// </summary>
public sealed class TabForegroundConverter : IValueConverter
{
    private static readonly MediaBrush ActiveBrush = MediaBrushes.White;
    private static readonly MediaBrush InactiveBrush = new MediaSolidColorBrush(MediaColor.FromRgb(148, 163, 184)); // #94A3B8

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return InactiveBrush;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase)
            ? ActiveBrush
            : InactiveBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
