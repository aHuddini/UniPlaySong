using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using UniPlaySong.Common;

namespace UniPlaySong.Controls.Settings
{
    public partial class PlaybackPage : UserControl
    {
        public PlaybackPage()
        {
            InitializeComponent();
        }

        private void ResetPlaybackTab_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsPageHelpers.ConfirmAndGetSettings(this, "Playback");
            if (s == null) return;

            // Moved here from the General handler with their controls: these three decide when
            // music plays at all, and now sit at the head of this tab.
            s.MusicState = AudioState.Always;
            s.AutoPlayOnFirstLaunchDesktop = true;
            s.SkipFirstSelectionAfterModeSwitch = false;

            s.MusicVolume = Constants.DefaultMusicVolume;
            s.FullscreenVolumeBoostPercent = 0;
            s.LowerVolumeOnIdle = false;
            s.IdleVolumeTimeoutMinutes = 15;
            s.FadeInDuration = Constants.DefaultFadeInDuration;
            s.FadeOutDuration = Constants.DefaultFadeOutDuration;
            s.EnablePreviewMode = false;
            s.PreviewDuration = Constants.DefaultPreviewDuration;
            s.RandomizeOnEverySelect = true;
            s.RandomizeOnMusicEnd = true;
            s.RandomizeDefaultMusicOnEnd = true;
            s.StopAfterSongEnds = false;
            s.EnableDefaultMusic = true;
            s.DefaultMusicSourceOption = DefaultMusicSource.BundledPreset;
            s.SelectedBundledPreset = "tunetank-dark-ambient-soundscape-music.mp3";
            s.RandomizeBundledTrackOnStartup = false;
            s.DefaultMusicPath = string.Empty;
            s.DefaultMusicFolderPath = string.Empty;
            s.CustomRotationGameIds = new List<Guid>();
            s.DefaultMusicContinueSameSong = true;
            s.BackupCustomMusicPath = string.Empty;
            s.MusicOnlyForInstalledGames = false;
            s.NostalgiaMode = false;
            s.NostalgiaStatusIds = new List<Guid>();
            s.DefaultMusicStatusPoolIds = new List<Guid>();
            s.GamePropFilterEnabled = false;
            s.GamePropFilterPlatformIds = new List<Guid>();
            s.GamePropFilterGenreIds = new List<Guid>();
            s.GamePropFilterSourceIds = new List<Guid>();
            s.FilterModeEnabled = false;
            s.RadioModeEnabled = false;
            s.SpotifySkipOnGap = false;
            s.PlayOnlyOnGameSelect = false;
            s.RadioMusicSource = RadioMusicSource.FullLibrary;
            s.RadioCustomFolderPath = null;
            s.FadeOutBeforeSongEnd = false;
            s.FadeOutBeforeSongEndDuration = 3.0;
            s.EnableTrueCrossfade = false;
            s.CrossfadeDurationSeconds = 9;
            s.PlaySoundOnDownloadComplete = false;
            s.EnableRandomPickerMusic = true;

            SettingsPageHelpers.ShowButtonFeedback(sender, "Reset!");
        }
    }
}
