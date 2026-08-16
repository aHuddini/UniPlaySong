using System.Windows;
using System.Windows.Controls;

namespace UniPlaySong.Controls.Settings
{
    // Theme Support tab (v1.5.6): PS5-Experience compatibility + theme-developer
    // overlay/video pause opt-outs. Moved out of the Experimental tab.
    public partial class ThemeSupportPage : UserControl
    {
        public ThemeSupportPage()
        {
            InitializeComponent();
        }

        private void ResetThemeSupportTab_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsPageHelpers.ConfirmAndGetSettings(this, "Theme Support");
            if (s == null) return;

            s.ThemeCompatibleSilentSkip = true;  // moved here from the General tab
            s.PS5ThemeCompatMode = false;  // PS5-Experience theme compatibility (default off)
            s.PauseOnThemeOverlay = true;
            s.PauseOnThemeVideo = true;
            // Moved here with its checkbox. It was previously assigned by BOTH the Playback and
            // Pauses handlers even though the control only ever appeared on Pauses.
            s.PauseOnTrailer = true;
            SettingsPageHelpers.ShowButtonFeedback(sender, "Reset!");
        }
    }
}
