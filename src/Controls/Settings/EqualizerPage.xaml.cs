using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using UniPlaySong.Audio;
using UniPlaySong.Services;

namespace UniPlaySong.Controls.Settings
{
    // Height of the fader's lit tube: from the 0 dB mark to the cap centre, in the half the value is on.
    // The cap centre travels the slider's 160 px less the cap's 18 px, so each half is 71 px for 12 dB.
    public class EqFillConverter : IValueConverter
    {
        private const double HalfTravel = (160 - 18) / 2.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double db = value is double d ? d : 0;
            bool up = (parameter as string) == "up";
            double amount = up ? Math.Max(0, db) : Math.Max(0, -db);
            return amount / Equalizer.RangeDb * HalfTravel;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // Cyan for a boost (and 0 dB), amber for a cut: the value label and the cap's indicator line.
    public class EqToneConverter : IValueConverter
    {
        private static readonly Brush Boost = Frozen(Color.FromRgb(0x4C, 0xC2, 0xFF));
        private static readonly Brush Cut = Frozen(Color.FromRgb(0xE0, 0xA5, 0x5A));

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double db = value is double d ? d : value is int i ? i : 0;
            return db < 0 ? Cut : Boost;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public partial class EqualizerPage : UserControl
    {
        public EqualizerPage()
        {
            InitializeComponent();
        }

        private UniPlaySongSettings Settings => (DataContext as UniPlaySongSettingsViewModel)?.Settings;

        private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var s = Settings;
            var bands = s == null ? null : Equalizer.PresetBands(s.EqualizerPreset, Equalizer.BandCount(s));
            if (bands != null) Equalizer.ApplyBands(s, bands);
        }

        // Switching layouts keeps the sound: a preset is re-applied in the new layout, a custom curve is carried
        // across as what it plays at the new band centres. Only a switch the user makes counts — the binding
        // filling the list on page load, or the other copy of this page following along, must not rewrite a curve.
        private void BandCount_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.RemovedItems.Count == 0 || !(sender is ComboBox combo) || !(combo.IsKeyboardFocusWithin || combo.IsDropDownOpen)) return;
            var s = Settings;
            if (s == null) return;

            int to = Equalizer.BandCount(s);
            var preset = Equalizer.PresetBands(s.EqualizerPreset, to);
            Equalizer.ApplyBands(s, preset ?? Equalizer.CarryCurve(to == 10 ? Equalizer.Bands15(s) : Equalizer.Bands10(s), to));
        }

        // A slider the user is moving means the sound is no longer the preset's, so the list says Custom. Only a
        // slider holding the mouse or keyboard counts: values arriving by binding (page load, a preset, the other
        // copy of this page) must not flip it.
        private void Band_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!(sender is Slider slider) || !(slider.IsMouseCaptureWithin || slider.IsKeyboardFocusWithin)) return;
            var s = Settings;
            if (s != null && s.EqualizerPreset != EqualizerPreset.Custom) s.EqualizerPreset = EqualizerPreset.Custom;
        }

        // Names the properties but not their values — those come off a pristine settings object (every band and the
        // preamp at 0, preset Flat), so this cannot drift from the shipped defaults. The layout choice is left alone.
        private static readonly string[] BandSettings = new[]
        {
            nameof(UniPlaySongSettings.EqualizerPreset),
            nameof(UniPlaySongSettings.EqualizerPreampDb),
            nameof(UniPlaySongSettings.EqualizerBand60),
            nameof(UniPlaySongSettings.EqualizerBand170),
            nameof(UniPlaySongSettings.EqualizerBand310),
            nameof(UniPlaySongSettings.EqualizerBand600),
            nameof(UniPlaySongSettings.EqualizerBand1k),
            nameof(UniPlaySongSettings.EqualizerBand3k),
            nameof(UniPlaySongSettings.EqualizerBand6k),
            nameof(UniPlaySongSettings.EqualizerBand12k),
            nameof(UniPlaySongSettings.EqualizerBand14k),
            nameof(UniPlaySongSettings.EqualizerBand16k),
        }.Concat(typeof(UniPlaySongSettings).GetProperties()
            .Select(p => p.Name).Where(n => n.StartsWith("Equalizer15Band", StringComparison.Ordinal))).ToArray();

        private void ResetFlat_Click(object sender, RoutedEventArgs e)
        {
            var s = Settings;
            if (s == null) return;
            SettingsResetService.ResetProperties(s, BandSettings);
            SettingsPageHelpers.ShowButtonFeedback(sender, "Reset!");
        }
    }
}
