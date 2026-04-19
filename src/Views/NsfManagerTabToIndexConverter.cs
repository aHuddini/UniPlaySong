using System;
using System.Globalization;
using System.Windows.Data;
using UniPlaySong.Models;

namespace UniPlaySong.Views
{
    // Two-way converts NsfManagerTab <-> int SelectedIndex for a 2-tab TabControl.
    // SplitTracks = 0, EditLoops = 1.
    public sealed class NsfManagerTabToIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is NsfManagerTab tab)
                return tab == NsfManagerTab.EditLoops ? 1 : 0;
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i && i == 1) return NsfManagerTab.EditLoops;
            return NsfManagerTab.SplitTracks;
        }
    }
}
