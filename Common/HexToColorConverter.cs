using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace UniPlaySong
{
    /// <summary>
    /// Converts a hex color string (e.g., "1E1E1E" or "#1E1E1E") to a SolidColorBrush.
    /// Used for live color preview in settings UI.
    /// </summary>
    public class HexToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is string hexString && !string.IsNullOrEmpty(hexString))
                {
                    var hex = hexString.TrimStart('#');
                    if (hex.Length == 6)
                    {
                        byte r = System.Convert.ToByte(hex.Substring(0, 2), 16);
                        byte g = System.Convert.ToByte(hex.Substring(2, 2), 16);
                        byte b = System.Convert.ToByte(hex.Substring(4, 2), 16);
                        return new SolidColorBrush(Color.FromRgb(r, g, b));
                    }
                }
            }
            catch
            {
                // Return default on parse error
            }

            // Default: dark gray
            return new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
            {
                var color = brush.Color;
                return $"{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            return "1E1E1E";
        }
    }
}
