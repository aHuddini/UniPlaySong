using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace UniPlaySong
{
    // Paints the Quick Start tile's left rail: green when that profile is the active one,
    // transparent otherwise.
    //
    // Deliberately NOT the blue used by the section-header bars. Those are structural furniture
    // marking Fullscreen / Desktop / Options; this rail carries meaning — "this is the profile you
    // are running". Sharing one colour made a state indicator look like more chrome.
    //
    // Green matches the ACTIVE badge in the status strip, so the two agree: whatever the badge says
    // is active is the row wearing the rail.
    //
    // The rail occupies a fixed-width column either way rather than appearing and disappearing, so
    // every row's text starts at the same x whether or not it is active — a rail that changed the
    // layout would make the list jump each time a profile is applied.
    public class ActiveRailConverter : IValueConverter
    {
        private static readonly SolidColorBrush Active =
            new SolidColorBrush(Color.FromRgb(0x5C, 0xB8, 0x5C));

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
