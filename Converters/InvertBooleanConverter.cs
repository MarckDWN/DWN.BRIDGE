using System;
using System.Globalization;
using System.Windows.Data;

namespace AIBridge.Converters
{
    public class InvertBooleanConverter : IValueConverter
    {
        public object Translate(object value)
        {
            if (value is bool b)
                return !b;
            return value;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Translate(value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Translate(value);
        }
    }
}
