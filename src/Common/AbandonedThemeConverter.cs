using System;
using System.Globalization;
using System.Windows.Data;

namespace UniPlaySong
{
    public class AbandonedThemeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AbandonedToastTheme theme)
                return (int)theme;
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
                return (AbandonedToastTheme)index;
            return AbandonedToastTheme.Tombstone;
        }
    }
}
