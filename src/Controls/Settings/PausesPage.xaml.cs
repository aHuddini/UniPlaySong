using System.Windows;
using System.Windows.Controls;

namespace UniPlaySong.Controls.Settings
{
    public partial class PausesPage : UserControl
    {
        public PausesPage()
        {
            InitializeComponent();
        }

        private void ResetPausesTab_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsPageHelpers.ConfirmAndGetSettings(this, "Pauses");
            if (s == null) return;

            s.PauseOnGameStart = true;
            s.RadioPlaysThroughGames = false;
            s.PauseOnSystemLock = false;
            s.PauseOnFocusLoss = false;
            s.FocusLossStayPaused = false;
            s.FocusLossIgnoreBrief = false;
            s.PauseOnMinimize = true;
            s.PauseWhenInSystemTray = true;
            s.PauseOnExternalAudio = false;
            s.KeepPausedAfterExternalAudio = false;
            s.ExternalAudioDebounceSeconds = 0;
            s.ExternalAudioInstantPause = false;
            s.ExternalAudioExcludedApps = "obs64, obs32, wallpaper64, wallpaper32, webwallpaper32, sunshine, sunshinesvc";
            s.PauseOnIdle = false;
            s.IdleTimeoutMinutes = 15;

            SettingsPageHelpers.ShowButtonFeedback(sender, "Reset!");
        }
    }
}
