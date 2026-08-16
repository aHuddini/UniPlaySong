using System.Windows;
using System.Windows.Controls;

namespace UniPlaySong.Controls.Settings
{
    public partial class ExperimentalPage : UserControl
    {
        public ExperimentalPage()
        {
            InitializeComponent();
        }

        private void ResetIconGlow_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as UniPlaySongSettingsViewModel;
            var s = vm?.Settings;
            if (s == null) return;

            s.IconGlowPreset = IconGlowPreset.Custom;
            s.EnableIconGlow = true;
            s.EnableIconGlowPulse = true;
            s.EnableIconGlowSpin = false;
            s.EnableIconGlowSpinAcceleration = false;
            s.EnableListIconGlow = false;
            s.SubtleListGlow = false;
            s.IconGlowSpinSpeed = 20.0;
            s.IconGlowIntensity = 1.8;
            s.IconGlowSize = 6.0;
            s.IconGlowPulseSpeed = 1.5;
            s.IconGlowAudioSensitivity = 2.0;

            SettingsPageHelpers.ShowButtonFeedback(sender, "Reset!");
        }
    }
}
