using System;
using System.Globalization;
using System.Windows.Data;

namespace UniPlaySong
{
    public class AudioStateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AudioState state)
            {
                return (int)state;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                return (AudioState)index;
            }
            return AudioState.Always;
        }
    }
}

