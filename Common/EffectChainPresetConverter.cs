using System;
using System.Globalization;
using System.Windows.Data;

namespace UniPlaySong
{
    public class EffectChainPresetConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EffectChainPreset preset)
            {
                return (int)preset;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                return (EffectChainPreset)index;
            }
            return EffectChainPreset.Standard;
        }
    }
}
