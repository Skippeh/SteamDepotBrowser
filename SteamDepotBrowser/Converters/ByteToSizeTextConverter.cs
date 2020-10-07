using System;
using System.Globalization;
using System.Windows.Data;

namespace SteamDepotBrowser.Converters
{
    public class ByteToSizeTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return "unknown";
            
            ulong numericValue = System.Convert.ToUInt64(value);
            
            string size;
            string suffix;

            if (numericValue >= 1024 * 1024 * 1024)
            {
                size = (numericValue / 1024d / 1024d / 1024d).ToString("F");
                suffix = "GB";
            }
            else if (numericValue >= 1024 * 1024)
            {
                size = (numericValue / 1024d / 1024d).ToString("F");
                suffix = "MB";
            }
            else if (numericValue >= 1024)
            {
                size = (numericValue / 1024d).ToString("F");
                suffix = "KB";
            }
            else
            {
                size = numericValue.ToString();
                suffix = "B";
            }

            return $"{size}{suffix}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}