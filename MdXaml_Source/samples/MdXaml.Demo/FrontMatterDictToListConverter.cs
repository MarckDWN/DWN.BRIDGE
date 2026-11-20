using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace MdXaml.Demo
{
    /// <summary>
    /// Converts Dictionary&lt;string, object&gt; to a list of key-value rows for table binding.
    /// Returns empty list when null.
    /// </summary>
    public sealed class FrontMatterDictToListConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Dictionary<string, object> dict)
                return dict.Select(kvp => new KeyValuePair<string, string>(
                    kvp.Key,
                    ValueToDisplayString(kvp.Value))).ToList();

            return Array.Empty<KeyValuePair<string, string>>();

        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();

        private static string ValueToDisplayString(object value)
        {
            if (value is null) return string.Empty;
            if (value is string s) return s;
            if (value is Dictionary<string, object> d)
                return "{" + string.Join(", ", d.Select(kv => $"{kv.Key}: {ValueToDisplayString(kv.Value)}")) + "}";
            if (value is List<object> list)
                return "[" + string.Join(", ", list.Select(ValueToDisplayString)) + "]";
            return value.ToString() ?? string.Empty;
        }
    }
}
