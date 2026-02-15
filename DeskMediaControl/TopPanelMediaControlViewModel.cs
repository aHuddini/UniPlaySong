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
    // Desktop top panel media controls ViewModel (play/pause, skip).
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
        private TopPanelItem _spectrumItem;
        private TextBlock _playPauseIcon;
        private TextBlock _skipIcon;
        private NowPlayingPanel _nowPlayingPanel;
        private SpectrumVisualizerControl _spectrumVisualizer;
        private SongMetadataService _metadataService;

        public TopPanelItem PlayPauseItem => _playPauseItem;
        public TopPanelItem SkipItem => _skipItem;
        public TopPanelItem NowPlayingItem => _nowPlayingItem;

        public IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            var settings = _getSettings?.Invoke();

            // Only include play/pause and skip buttons if media controls setting is enabled
            if (settings?.ShowDesktopMediaControls != false)
            {
                yield return _playPauseItem;
                yield return _skipItem;
            }

            // Spectrum visualizer (between controls and now playing)
            // Always yield — Visible toggles at runtime via UpdateIcons()
            if (_spectrumItem != null)
            {
                _spectrumItem.Visible = settings?.ShowSpectrumVisualizer == true;
                yield return _spectrumItem;
            }

            // Only include Now Playing panel if setting is enabled
            if (settings?.ShowNowPlayingInTopPanel == true && _nowPlayingItem != null)
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
            InitializeSpectrumVisualizer();
            InitializeNowPlayingPanel();
            SubscribeToEvents(_getPlaybackService());
        }

        private void InitializeTopPanelItems()
        {
            var icoFont = ResourceProvider.GetResource("FontIcoFont") as FontFamily;

            // Play/Pause button — 18pt, no bold, theme-adaptive margins
            _playPauseIcon = new TextBlock
            {
                Text = MediaControlIcons.Play,
                FontSize = 18,
                FontFamily = icoFont,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _playPauseItem = new TopPanelItem
            {
                Icon = _playPauseIcon,
                Title = "UniPlaySong: Play Music",
                Visible = true,
                Activated = OnPlayPauseActivated
            };

            // Skip/Next button — greyed out until 2+ songs available
            _skipIcon = new TextBlock
            {
                Text = MediaControlIcons.Next,
                FontSize = 18,
                FontFamily = icoFont,
                Opacity = 0.3,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _skipItem = new TopPanelItem
            {
                Icon = _skipIcon,
                Title = "UniPlaySong: Skip to Next Song (No additional songs)",
                Visible = true,
                Activated = OnSkipActivated
            };

            // Reduce the gap between play/pause and skip so they look grouped.
            // Trims play/pause's right margin and skip's left margin symmetrically,
            // leaving all other theme styling (MinWidth, Padding, Background) intact.
            _playPauseIcon.Loaded += (s, ev) => AdjustTopPanelItemMargin(_playPauseIcon, trimRight: true);
            _skipIcon.Loaded += (s, ev) => AdjustTopPanelItemMargin(_skipIcon, trimLeft: true);
        }

        // Finds the TopPanelItem container for an icon and trims one side of its margin
        // to group adjacent buttons closer together.
        private void AdjustTopPanelItemMargin(FrameworkElement icon, bool trimLeft = false, bool trimRight = false)
        {
            try
            {
                DependencyObject current = icon;
                for (int i = 0; i < 10; i++)
                {
                    current = VisualTreeHelper.GetParent(current);
                    if (current == null) break;

                    var fe = current as FrameworkElement;
                    if (fe != null && fe.GetType().Name == "TopPanelItem")
                    {
                        var m = fe.Margin;
                        double left = trimLeft ? Math.Max(1, Math.Round(m.Left * 0.2)) : m.Left;
                        double right = trimRight ? Math.Max(1, Math.Round(m.Right * 0.2)) : m.Right;
                        fe.Margin = new Thickness(left, m.Top, right, m.Bottom);
                        break;
                    }

                    if (current.GetType().Name == "StackPanel")
                        break;
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"TopPanel: Error adjusting margin: {ex.Message}");
            }
        }

        private void InitializeSpectrumVisualizer()
        {
            try
            {
                _spectrumVisualizer = new SpectrumVisualizerControl();
                _spectrumVisualizer.SetSettingsProvider(_getSettings);
                _spectrumItem = new TopPanelItem
                {
                    Icon = _spectrumVisualizer,
                    Title = "UniPlaySong: Spectrum Visualizer",
                    Visible = true
                };
                _log?.Invoke("TopPanel: Spectrum visualizer initialized");
            }
            catch (System.Exception ex)
            {
                _log?.Invoke($"TopPanel: Error initializing spectrum visualizer: {ex.Message}");
            }
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
            playbackService.OnSongChanged += _ => UpdateIcons();
            playbackService.OnSongCountChanged += UpdateSkipVisibility;
        }

        // Re-subscribe to events after playback service recreation (e.g., Live Effects toggle).
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
            UpdateSkipState(_getPlaybackService?.Invoke());
        }

        private void UpdateSkipState(IMusicPlaybackService playbackService)
        {
            if (_skipItem == null || _skipIcon == null) return;

            _canSkip = playbackService?.CurrentGameSongCount >= 2;
            _skipIcon.Opacity = _canSkip ? 1.0 : 0.3;
            _skipItem.Title = _canSkip
                ? "UniPlaySong: Skip to Next Song"
                : "UniPlaySong: Skip to Next Song (No additional songs)";
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

                    UpdateSkipState(playbackService);

                    var settings = _getSettings?.Invoke();
                    bool vizEnabled = settings?.ShowSpectrumVisualizer == true;
                    if (_spectrumItem != null)
                        _spectrumItem.Visible = vizEnabled;
                    _spectrumVisualizer?.SetActive(isPlaying && vizEnabled);

                    // Hide Now Playing panel when default music is playing (no game-specific music)
                    if (_nowPlayingItem != null && settings?.ShowNowPlayingInTopPanel == true)
                    {
                        bool hideForDefault = settings.HideNowPlayingForDefaultMusic &&
                                              playbackService?.IsPlayingDefaultMusic == true;
                        _nowPlayingItem.Visible = !hideForDefault;
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
