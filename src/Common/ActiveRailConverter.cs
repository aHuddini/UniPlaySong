using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace UniPlaySong
{
    // Paints the Quick Start tile's left accent rail: the plugin's accent colour when that profile
    // is the active one, transparent otherwise.
    //
    // The rail occupies a fixed-width column either way rather than appearing and disappearing, so
    // every row's text starts at the same x whether or not it is active — a rail that changed the
    // layout would make the list jump each time a profile is applied.
    public class ActiveRailConverter : IValueConverter
    {
        private static readonly SolidColorBrush Active =
            new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? (Brush)Active : Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
