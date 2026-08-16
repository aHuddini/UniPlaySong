using System.Windows;
using System.Windows.Controls;

namespace UniPlaySong.Controls.Settings
{
    public partial class DownloadsPage : UserControl
    {
        public DownloadsPage()
        {
            InitializeComponent();
        }

        private void ResetDownloadsTab_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsPageHelpers.ConfirmAndGetSettings(this, "Downloads");
            if (s == null) return;

            // YtDlpPath is preserved
            s.CookieMode = CookieMode.None;
            s.CustomCookiesFilePath = string.Empty;
            s.AutoDownloadOnLibraryUpdate = true;
            s.AutoDownloadOnGameInstall = true;
            s.MaxConcurrentDownloads = 3;

            // Search settings (merged into the Downloads tab in v1.5.10)
            s.EnableSearchCache = true;
            s.SearchCacheDurationDays = 7;
            s.UseCustomHintsDatabase = false;
            s.CustomHintsDatabasePath = "";

            SettingsPageHelpers.ShowButtonFeedback(sender, "Reset!");
        }
    }
}
