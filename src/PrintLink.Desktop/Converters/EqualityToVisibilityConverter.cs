using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PrintLink.Desktop.Converters;

/// <summary>
/// Converts an integer or object comparison to System.Windows.Visibility.
/// Used for tab switching and conditional UI visibility.
/// </summary>
public sealed class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return Visibility.Collapsed;

        string strValue = value.ToString()!;
        string strParam = parameter.ToString()!;

        return string.Equals(strValue, strParam, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
