using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.DeskMediaControl
{
    /// <summary>
    /// Desktop top panel media controls ViewModel (play/pause, skip).
    /// </summary>
    public class TopPanelMediaControlViewModel
    {
        private readonly Func<IMusicPlaybackService> _getPlaybackService;
        private readonly Func<UniPlaySongSettings> _getSettings;
        private readonly Func<Game> _getCurrentGame;
        private readonly Action<string> _log;
        private readonly Action<Exception, string> _handleError;

        private TopPanelItem _playPauseItem;
        private TopPanelItem _skipItem;
        private TopPanelItem _nowPlayingItem;
        private TextBlock _playPauseIcon;
        private TextBlock _skipIcon;
        private NowPlayingPanel _nowPlayingPanel;
        private SongMetadataService _metadataService;

        public TopPanelItem PlayPauseItem => _playPauseItem;
        public TopPanelItem SkipItem => _skipItem;
        public TopPanelItem NowPlayingItem => _nowPlayingItem;

        public IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            yield return _playPauseItem;
            yield return _skipItem;

            // Only include Now Playing panel if setting is enabled
            if (_getSettings?.Invoke()?.ShowNowPlayingInTopPanel == true && _nowPlayingItem != null)
            {
                yield return _nowPlayingItem;
            }
        }

        public TopPanelMediaControlViewModel(
            Func<IMusicPlaybackService> getPlaybackService,
            Func<UniPlaySongSettings> getSettings,
            Func<Game> getCurrentGame,
            Action<string> log = null,
            Action<Exception, string> handleError = null)
        {
            _getPlaybackService = getPlaybackService ?? throw new ArgumentNullException(nameof(getPlaybackService));
            _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
            _getCurrentGame = getCurrentGame ?? throw new ArgumentNullException(nameof(getCurrentGame));
            _log = log;
            _handleError = handleError;

            InitializeTopPanelItems();
            InitializeNowPlayingPanel();
            SubscribeToEvents(_getPlaybackService());
        }

        private void InitializeTopPanelItems()
        {
            var icoFont = ResourceProvider.GetResource("FontIcoFont") as FontFamily;

            // Play/Pause button
            _playPauseIcon = new TextBlock
            {
                Text = MediaControlIcons.Play,
                FontSize = 18,
                FontFamily = icoFont,
                FontWeight = FontWeights.Bold
            };

            _playPauseItem = new TopPanelItem
            {
                Icon = _playPauseIcon,
                Title = "UniPlaySong: Play Music",
                Visible = true,
                Activated = OnPlayPauseActivated
            };

            // Skip/Next button
            _skipIcon = new TextBlock
            {
                Text = MediaControlIcons.Next,
                FontSize = 18,
                FontFamily = icoFont,
                FontWeight = FontWeights.Bold,
                Opacity = 0.3 // Start greyed out until 2+ songs available
            };

            _skipItem = new TopPanelItem
            {
                Icon = _skipIcon,
                Title = "UniPlaySong: Skip to Next Song (No additional songs)",
                Visible = true, // Always visible, but greyed out when disabled
                Activated = OnSkipActivated
            };
        }

        private void InitializeNowPlayingPanel()
        {
            try
            {
                // Create the Now Playing panel
                _nowPlayingPanel = new NowPlayingPanel();

                // Create the TopPanelItem for the Now Playing display
                _nowPlayingItem = new TopPanelItem
                {
                    Icon = _nowPlayingPanel,
                    Title = "UniPlaySong: Now Playing (click to open music folder)",
                    Visible = true,
                    Activated = OnNowPlayingActivated
                };

                // Create metadata service and subscribe to song changes
                var playbackService = _getPlaybackService?.Invoke();
                if (playbackService != null)
                {
                    _metadataService = new SongMetadataService(playbackService, null);
                    _metadataService.OnSongInfoChanged += OnSongInfoChanged;

                    // If there's already a song playing, update immediately
                    if (!string.IsNullOrEmpty(playbackService.CurrentSongPath))
                    {
                        var songInfo = _metadataService.ReadMetadata(playbackService.CurrentSongPath);
                        _nowPlayingPanel.UpdateSongInfo(songInfo);
                    }
                }

                _log?.Invoke("TopPanel: Now Playing panel initialized");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"TopPanel: Error initializing Now Playing panel: {ex.Message}");
            }
        }

        private void OnNowPlayingActivated()
        {
            try
            {
                var playbackService = _getPlaybackService?.Invoke();
                var currentSongPath = playbackService?.CurrentSongPath;

                if (!string.IsNullOrEmpty(currentSongPath) && File.Exists(currentSongPath))
                {
                    // Open the folder containing the current song
                    var folder = Path.GetDirectoryName(currentSongPath);
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", folder);
                        _log?.Invoke($"TopPanel: Opening music folder: {folder}");
                    }
                }
                else
                {
                    _log?.Invoke("TopPanel: No song playing, cannot open folder");
                }
            }
            catch (Exception ex)
            {
                _handleError?.Invoke(ex, "opening music folder from top panel");
            }
        }

        private void OnSongInfoChanged(SongInfo songInfo)
        {
            _nowPlayingPanel?.UpdateSongInfo(songInfo);
        }

        private void SubscribeToEvents(IMusicPlaybackService playbackService)
        {
            if (playbackService == null) return;

            playbackService.OnMusicStarted += _ => UpdateIcons();
            playbackService.OnMusicStopped += _ => UpdateIcons();
            playbackService.OnPlaybackStateChanged += UpdateIcons;
            playbackService.OnSongCountChanged += UpdateSkipVisibility;
        }

        /// <summary>
        /// Re-subscribe to events after playback service recreation (e.g., Live Effects toggle).
        /// </summary>
        public void ResubscribeToEvents(IMusicPlaybackService newPlaybackService)
        {
            SubscribeToEvents(newPlaybackService);
            _metadataService?.ResubscribeToService(newPlaybackService);
            UpdateIcons();
        }

        private void OnPlayPauseActivated()
        {
            try
            {
                var playbackService = _getPlaybackService?.Invoke();
                if (playbackService == null)
                {
                    _log?.Invoke("TopPanel: PlaybackService is null");
                    return;
                }

                if (playbackService.IsPaused)
                {
                    _log?.Invoke("TopPanel: Resuming playback via manual toggle");
                    playbackService.RemovePauseSource(PauseSource.Manual);
                }
                else if (playbackService.IsPlaying || playbackService.IsLoaded)
                {
                    _log?.Invoke("TopPanel: Pausing playback via manual toggle");
                    playbackService.AddPauseSource(PauseSource.Manual);
                }
                else
                {
                    var currentGame = _getCurrentGame?.Invoke();
                    if (currentGame != null)
                    {
                        _log?.Invoke($"TopPanel: Starting playback for {currentGame.Name}");
                        playbackService.PlayGameMusic(currentGame, _getSettings?.Invoke());
                    }
                    else
                    {
                        _log?.Invoke("TopPanel: No game selected, cannot start playback");
                    }
                }

                UpdateIcons();
            }
            catch (Exception ex)
            {
                _handleError?.Invoke(ex, "toggling playback from top panel");
            }
        }

        private void OnSkipActivated()
        {
            try
            {
                if (!_canSkip)
                {
                    _log?.Invoke("TopPanel: Skip disabled (not enough songs)");
                    return;
                }

                var playbackService = _getPlaybackService?.Invoke();
                if (playbackService == null)
                {
                    _log?.Invoke("TopPanel: PlaybackService is null, cannot skip");
                    return;
                }

                _log?.Invoke("TopPanel: Skipping to next song");
                playbackService.SkipToNextSong();
            }
            catch (Exception ex)
            {
                _handleError?.Invoke(ex, "skipping to next song from top panel");
            }
        }

        private bool _canSkip = false;

        private void UpdateSkipVisibility()
        {
            if (_skipItem == null || _skipIcon == null) return;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                try
                {
                    var playbackService = _getPlaybackService?.Invoke();
                    _canSkip = playbackService?.CurrentGameSongCount >= 2;

                    // Grey out icon when disabled (opacity 0.3), full opacity when enabled
                    _skipIcon.Opacity = _canSkip ? 1.0 : 0.3;
                    _skipItem.Title = _canSkip
                        ? "UniPlaySong: Skip to Next Song"
                        : "UniPlaySong: Skip to Next Song (No additional songs)";

                    _log?.Invoke($"TopPanel: Skip button enabled: {_canSkip} (song count: {playbackService?.CurrentGameSongCount ?? 0})");
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Error updating skip state: {ex.Message}");
                }
            });
        }

        public void UpdateIcons()
        {
            if (_playPauseIcon == null || _playPauseItem == null) return;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                try
                {
                    var playbackService = _getPlaybackService?.Invoke();
                    bool isPlaying = playbackService?.IsPlaying == true && playbackService?.IsPaused != true;

                    _playPauseIcon.Text = isPlaying ? MediaControlIcons.Pause : MediaControlIcons.Play;
                    _playPauseItem.Title = isPlaying ? "UniPlaySong: Pause Music" : "UniPlaySong: Play Music";

                    // Also update skip state when icons update
                    _canSkip = playbackService?.CurrentGameSongCount >= 2;
                    if (_skipIcon != null)
                    {
                        _skipIcon.Opacity = _canSkip ? 1.0 : 0.3;
                    }
                    if (_skipItem != null)
                    {
                        _skipItem.Title = _canSkip
                            ? "UniPlaySong: Skip to Next Song"
                            : "UniPlaySong: Skip to Next Song (No additional songs)";
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Error updating top panel icons: {ex.Message}");
                }
            });
        }

        // Keep old method name for backwards compatibility
        public void UpdateIcon() => UpdateIcons();
    }
}
