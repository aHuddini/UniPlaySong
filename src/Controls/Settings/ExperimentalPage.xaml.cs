using System.Windows;
using System.Windows.Controls;
using UniPlaySong.Services;

namespace UniPlaySong.Controls.Settings
{
    public partial class ExperimentalPage : UserControl
    {
        public ExperimentalPage()
        {
            InitializeComponent();
        }

        // Icon Glow is a section reset, narrower than the Advanced group button in the strip.
        // It names the properties but not their values — those come off a pristine settings
        // object, so this can no longer disagree with the shipped defaults the way it did when
        // it hardcoded EnableIconGlow = true against a default of false.
        private static readonly string[] IconGlowSettings =
        {
            nameof(UniPlaySongSettings.IconGlowPreset),
            nameof(UniPlaySongSettings.EnableIconGlow),
            nameof(UniPlaySongSettings.EnableIconGlowPulse),
            nameof(UniPlaySongSettings.EnableIconGlowSpin),
            nameof(UniPlaySongSettings.EnableIconGlowSpinAcceleration),
            nameof(UniPlaySongSettings.EnableListIconGlow),
            nameof(UniPlaySongSettings.SubtleListGlow),
            nameof(UniPlaySongSettings.IconGlowSpinSpeed),
            nameof(UniPlaySongSettings.IconGlowIntensity),
            nameof(UniPlaySongSettings.IconGlowSize),
            nameof(UniPlaySongSettings.IconGlowPulseSpeed),
            nameof(UniPlaySongSettings.IconGlowAudioSensitivity),
        };

        private void ResetIconGlow_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as UniPlaySongSettingsViewModel;
            if (vm?.Settings == null) return;

            SettingsResetService.ResetProperties(vm.Settings, IconGlowSettings);
            SettingsPageHelpers.ShowButtonFeedback(sender, "Reset!");
        }
    }
}
