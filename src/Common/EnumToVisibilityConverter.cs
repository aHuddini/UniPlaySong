using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UniPlaySong
{
    // Visible when the bound enum equals the parameter, Collapsed otherwise. Used to show only the
    // controls belonging to the selected segment, rather than rendering every branch and greying
    // out the ones that do not apply.
    public class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return Visibility.Collapsed;
            return value.ToString() == parameter.ToString() ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
