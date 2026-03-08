using System;
using System.Globalization;
using System.Windows.Data;

namespace UniPlaySong
{
    public class CelebrationThemeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CelebrationToastTheme theme)
                return (int)theme;
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
                return (CelebrationToastTheme)index;
            return CelebrationToastTheme.Gold;
        }
    }
}
