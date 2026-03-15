using System;
using System.Globalization;
using System.Windows.Data;

namespace UniPlaySong
{
    public class SidebarGlowModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SidebarGlowMode mode)
                return (int)mode;
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
                return (SidebarGlowMode)index;
            return SidebarGlowMode.Breathing;
        }
    }
}
