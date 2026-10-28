using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PrintLink.Desktop.Converters;

/// <summary>
/// Converts a boolean to System.Windows.Visibility with optional inversion.
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool boolVal = value is true;
        if (Invert) boolVal = !boolVal;
        return boolVal ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Visibility vis)
        {
            bool res = vis == Visibility.Visible;
            return Invert ? !res : res;
        }
        return false;
    }
}
