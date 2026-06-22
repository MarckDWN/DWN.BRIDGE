using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIBridge.Converters
{
    /// <summary>
    /// Converte bool → Visibility in modo inverso:
    /// true  → Collapsed
    /// false → Visible
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
                return v == Visibility.Collapsed;
            return false;
        }
    }
}
