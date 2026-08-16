using System.Windows;
using System.Windows.Controls;
using UniPlaySong.Common;

namespace UniPlaySong.Controls.Settings
{
    public partial class GeneralPage : UserControl
    {
        public GeneralPage()
        {
            InitializeComponent();
        }

        // Live now-playing preview card. Subscribes to the LIVE settings object (the one the
        // NowPlayingPublisher writes, exposed via UniPlaySong.Settings) while the card is shown,
        // and unsubscribes when it unloads so the handler doesn't outlive the dialog.
        private System.ComponentModel.INotifyPropertyChanged _liveSettingsForPreview;
        private Window _previewHostWindow;

        private void NowPlayingPreview_OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_liveSettingsForPreview != null) return; // already subscribed; avoid double-subscription on repeated Loaded
            var vm = DataContext as UniPlaySongSettingsViewModel;
            var live = vm?.Plugin?.Settings; // the live UniPlaySongSettings the publisher writes
            if (live is System.ComponentModel.INotifyPropertyChanged inpc)
            {
                _liveSettingsForPreview = inpc;
                inpc.PropertyChanged += LiveSettings_PropertyChanged;
                vm.RefreshNowPlayingPreview(); // initial paint

                // WPF does NOT reliably raise Unloaded when Playnite closes its settings window, so this subscription — and
                // via it the whole view — would leak. Each reopen then stacks another stale view onto the one cached VM, and
                // their binding updates re-enter until the 1 MB UI stack overflows (0xc00000fd). The host Window's Closed DOES
                // fire, so release there too.
                _previewHostWindow = Window.GetWindow(this);
                if (_previewHostWindow != null) _previewHostWindow.Closed += PreviewHost_OnClosed;
            }
        }

        private void PreviewHost_OnClosed(object sender, System.EventArgs e) => ReleaseNowPlayingPreview();

        private void NowPlayingPreview_OnUnloaded(object sender, RoutedEventArgs e) => ReleaseNowPlayingPreview();

        // Idempotent: safe to call from Unloaded AND the host Window's Closed (whichever fires).
        private void ReleaseNowPlayingPreview()
        {
            if (_liveSettingsForPreview != null)
            {
                _liveSettingsForPreview.PropertyChanged -= LiveSettings_PropertyChanged;
                _liveSettingsForPreview = null;
            }
            if (_previewHostWindow != null)
            {
                _previewHostWindow.Closed -= PreviewHost_OnClosed;
                _previewHostWindow = null;
            }
        }

        private void LiveSettings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UniPlaySongSettings.NowPlayingTitle) ||
                e.PropertyName == nameof(UniPlaySongSettings.NowPlayingArtist) ||
                e.PropertyName == nameof(UniPlaySongSettings.NowPlayingAlbumArtPath))
            {
                var vm = DataContext as UniPlaySongSettingsViewModel;
                // The publisher may raise PropertyChanged off the UI thread; marshal the VM refresh.
                // BeginInvoke (async), never Invoke: a sync Invoke from an SMTC callback thread can
                // deadlock against a UI thread waiting on the raiser's lock (launch-freeze class).
                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                    Application.Current.Dispatcher.BeginInvoke(new System.Action(() => vm?.RefreshNowPlayingPreview()));
                else
                    vm?.RefreshNowPlayingPreview();
            }
        }

        private void ResetGeneralTab_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsPageHelpers.ConfirmAndGetSettings(this, "General");
            if (s == null) return;

            s.EnableMusic = true;
            s.SuppressPlayniteBackgroundMusic = true;
            s.ShowDesktopMediaControls = true;
            s.ShowTaskbarMediaControls = true;
            s.ShowNowPlayingInTopPanel = true;
            s.HideNowPlayingForDefaultMusic = false;
            s.ShowDefaultMusicIndicator = true;
            s.ShowProgressBar = false;
            s.ProgressBarPosition = ProgressBarPosition.AfterSkipButton;
            s.AutoTagOnLibraryUpdate = true;
            s.AutoDeleteMusicOnGameRemoval = true;
            s.EnableSongListCache = false;
            s.EnableDebugLogging = false;

            SettingsPageHelpers.ShowButtonFeedback(sender, "Reset!");
        }
    }
}
