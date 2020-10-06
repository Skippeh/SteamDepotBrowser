using System;
using System.Globalization;
using System.Windows.Data;

namespace SteamDepotBrowser.Converters
{
    public class PercentageTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var doubleValue = System.Convert.ToDouble(value);

            return $"{doubleValue:F}%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}