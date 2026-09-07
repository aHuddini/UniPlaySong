using System;
using System.Globalization;
using System.Windows.Data;

namespace UniPlaySong
{
    // ComboBox SelectedIndex <-> CalmDownPreset. Mirrors VizPresetConverter: the enum's numbering
    // is the combo's item order, with Custom at 0 so an unrecognised value lands there rather than
    // silently claiming to be a preset the values no longer match.
    public class CalmDownPresetConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CalmDownPreset preset)
            {
                return (int)preset;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                return (CalmDownPreset)index;
            }
            return CalmDownPreset.Custom;
        }
    }
}
