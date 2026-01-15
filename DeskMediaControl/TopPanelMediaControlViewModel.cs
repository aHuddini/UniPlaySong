using System;
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
    /// Desktop top panel play/pause control ViewModel.
    /// </summary>
    public class TopPanelMediaControlViewModel
    {
        private readonly Func<IMusicPlaybackService> _getPlaybackService;
        private readonly Func<UniPlaySongSettings> _getSettings;
        private readonly Func<Game> _getCurrentGame;
        private readonly Action<string> _log;
        private readonly Action<Exception, string> _handleError;

        private TopPanelItem _playPauseItem;
        private TextBlock _iconTextBlock;

        public TopPanelItem PlayPauseItem => _playPauseItem;

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

            InitializeTopPanelItem();
            SubscribeToEvents(_getPlaybackService());
        }

        private void InitializeTopPanelItem()
        {
            _iconTextBlock = new TextBlock
            {
                Text = MediaControlIcons.Play,
                FontSize = 18,
                FontFamily = ResourceProvider.GetResource("FontIcoFont") as FontFamily,
                FontWeight = FontWeights.Bold
            };

            _playPauseItem = new TopPanelItem
            {
                Icon = _iconTextBlock,
                Title = "UniPlaySong: Play Music",
                Visible = true,
                Activated = OnPlayPauseActivated
            };
        }

        private void SubscribeToEvents(IMusicPlaybackService playbackService)
        {
            if (playbackService == null) return;

            playbackService.OnMusicStarted += _ => UpdateIcon();
            playbackService.OnMusicStopped += _ => UpdateIcon();
            playbackService.OnPlaybackStateChanged += UpdateIcon;
        }

        /// <summary>
        /// Re-subscribe to events after playback service recreation (e.g., Live Effects toggle).
        /// </summary>
        public void ResubscribeToEvents(IMusicPlaybackService newPlaybackService)
        {
            SubscribeToEvents(newPlaybackService);
            UpdateIcon();
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

                UpdateIcon();
            }
            catch (Exception ex)
            {
                _handleError?.Invoke(ex, "toggling playback from top panel");
            }
        }

        public void UpdateIcon()
        {
            if (_iconTextBlock == null || _playPauseItem == null) return;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                try
                {
                    var playbackService = _getPlaybackService?.Invoke();
                    bool isPlaying = playbackService?.IsPlaying == true && playbackService?.IsPaused != true;

                    _iconTextBlock.Text = isPlaying ? MediaControlIcons.Pause : MediaControlIcons.Play;
                    _playPauseItem.Title = isPlaying ? "UniPlaySong: Pause Music" : "UniPlaySong: Play Music";
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Error updating top panel icon: {ex.Message}");
                }
            });
        }
    }
}
