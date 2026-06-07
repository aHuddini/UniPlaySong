using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UniPlaySong
{
    // Returns Visibility.Visible when value is non-null AND (if string) non-empty.
    // Used by the Active Theme UPS Audio copy-result message: stays Collapsed until
    // the user clicks the button, then shows the inline result text.
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Visibility.Collapsed;
            if (value is string s && string.IsNullOrEmpty(s)) return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
