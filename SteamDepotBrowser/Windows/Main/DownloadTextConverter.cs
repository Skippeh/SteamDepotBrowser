using System;
using System.Globalization;
using System.Windows.Data;

namespace SteamDepotBrowser.Windows.Main
{
    /// <summary>
    /// Takes a bool and returns a string.
    /// </summary>
    public class DownloadTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is bool boolValue))
                throw new ArgumentException("Value must be a boolean.");

            return boolValue ? "Cancel" : "Download";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}