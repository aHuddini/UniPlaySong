using System;
using System.Globalization;
using System.Windows.Data;

namespace UniPlaySong
{
    public class ReverbPresetConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ReverbPreset preset)
            {
                return (int)preset;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                return (ReverbPreset)index;
            }
            return ReverbPreset.Custom;
        }
    }
}
