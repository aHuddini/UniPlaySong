using System;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Coordinates music playback decisions, state management, and skip logic
    /// Centralizes all "should play" logic to prevent timing issues and complexity
    /// </summary>
    public class MusicPlaybackCoordinator : IMusicPlaybackCoordinator
    {
        private readonly IMusicPlaybackService _playbackService;
        private readonly SettingsService _settingsService;
        private UniPlaySongSettings _settings; // Access via _settingsService.Current
        private readonly ILogger _logger;
        private readonly FileLogger _fileLogger;
        
        private bool _firstSelect = true;
        private bool _loginSkipActive = false;
        private bool _skipFirstSelectActive = false;
        private bool _hasSeenFullscreen = false;
        private Game _currentGame;
        
        private readonly Func<bool> _isFullscreen;
        private readonly Func<bool> _isDesktop;
        private readonly Func<Game> _getSelectedGame;
        
        public MusicPlaybackCoordinator(
            IMusicPlaybackService playbackService,
            SettingsService settingsService,
            ILogger logger,
            FileLogger fileLogger,
            Func<bool> isFullscreen,
            Func<bool> isDesktop,
            Func<Game> getSelectedGame)
        {
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _settings = _settingsService.Current ?? throw new InvalidOperationException("SettingsService.Current is null");
            _logger = logger;
            _fileLogger = fileLogger;
            _isFullscreen = isFullscreen ?? throw new ArgumentNullException(nameof(isFullscreen));
            _isDesktop = isDesktop ?? throw new ArgumentNullException(nameof(isDesktop));
            _getSelectedGame = getSelectedGame ?? throw new ArgumentNullException(nameof(getSelectedGame));
            
            _settingsService.SettingsChanged += OnSettingsChanged;
            _fileLogger?.Info("MusicPlaybackCoordinator: Subscribed to SettingsService");
        }
        
        /// <summary>
        /// Determines whether music should play for the given game.
        /// This is the central gatekeeper for all playback decisions.
        /// </summary>
        /// <param name="game">The game to check. Must not be null.</param>
        /// <returns>True if music should play; otherwise, false.</returns>
        /// <remarks>
        /// Checks skip logic, settings, mode, video state, and volume.
        /// All music playback decisions must pass through this method.
        /// </remarks>
        public bool ShouldPlayMusic(Game game)
        {
            if (_settings == null || !_settings.EnableMusic)
            {
                _fileLogger?.Info("ShouldPlayMusic: Returning false - music disabled or settings null");
                return false;
            }
            
            if (_playbackService == null)
            {
                _fileLogger?.Info("ShouldPlayMusic: Returning false - playback service null");
                return false;
            }
            
            if (_settings.MusicVolume <= 0)
            {
                _fileLogger?.Info("ShouldPlayMusic: Returning false - volume is 0");
                return false;
            }
            
            if (_settings.VideoIsPlaying)
            {
                _fileLogger?.Info("ShouldPlayMusic: Returning false - video is playing");
                return false;
            }
            
            if (game == null)
            {
                _fileLogger?.Info("ShouldPlayMusic: Returning false - game is null");
                return false;
            }
            
            if (_loginSkipActive)
            {
                _fileLogger?.Info("ShouldPlayMusic: Returning false - login skip active");
                return false;
            }
            
            // Check both _firstSelect (initial state) and _skipFirstSelectActive (skip window)
            if ((_firstSelect || _skipFirstSelectActive) && _settings.SkipFirstSelectionAfterModeSwitch)
            {
                _fileLogger?.Info($"ShouldPlayMusic: Returning false - first select skip enabled (FirstSelect: {_firstSelect}, SkipActive: {_skipFirstSelectActive})");
                return false;
            }
            
            var state = _settings.MusicState;
            if (_isFullscreen() && state != AudioState.Fullscreen && state != AudioState.Always)
            {
                _fileLogger?.Info($"ShouldPlayMusic: Returning false - fullscreen mode but state is {state}");
                return false;
            }
            
            if (_isDesktop() && state != AudioState.Desktop && state != AudioState.Always)
            {
                _fileLogger?.Info($"ShouldPlayMusic: Returning false - desktop mode but state is {state}");
                return false;
            }
            
            _fileLogger?.Info($"ShouldPlayMusic: Returning true - all checks passed (Game: {game.Name})");
            return true;
        }
        
        /// <summary>
        /// Handles game selection events.
        /// Coordinates skip logic and initiates music playback if appropriate.
        /// </summary>
        /// <param name="game">The selected game. Can be null.</param>
        /// <param name="isFullscreen">Whether Playnite is in fullscreen mode.</param>
        public void HandleGameSelected(Game game, bool isFullscreen)
        {
            // Reset skip state when entering fullscreen for first time (matches PNS "Skip on startup" behavior)
            if (isFullscreen && !_hasSeenFullscreen && _settings?.SkipFirstSelectionAfterModeSwitch == true)
            {
                _fileLogger?.Info("HandleGameSelected: First time seeing fullscreen mode - resetting skip state");
                _firstSelect = true;
                _skipFirstSelectActive = false;
                _hasSeenFullscreen = true;
            }
            
            if (game == null || _settings?.EnableMusic != true)
            {
                // Use PlayGameMusic(null) to handle fade-out properly
                _fileLogger?.Info("HandleGameSelected: No game or music disabled - fading out playback");
                _playbackService?.PlayGameMusic(null, _settings, false);
                _firstSelect = false;
                _currentGame = null;
                return;
            }
            
            var wasFirstSelect = _firstSelect;
            _currentGame = game;
            
            _fileLogger?.Info($"HandleGameSelected: Game={game.Name}, IsFullscreen={isFullscreen}, WasFirstSelect={wasFirstSelect}, LoginSkipActive={_loginSkipActive}, HasSeenFullscreen={_hasSeenFullscreen}");
            
            // SkipFirstSelectionAfterModeSwitch takes precedence
            if (wasFirstSelect && _settings.SkipFirstSelectionAfterModeSwitch)
            {
                _fileLogger?.Info($"HandleGameSelected: Skipping first selection (Game: {game.Name})");
                _skipFirstSelectActive = true;
                _firstSelect = false;
                return;
            }
            
            // ThemeCompatibleSilentSkip - only if SkipFirstSelectionAfterModeSwitch is disabled
            if (wasFirstSelect && _settings.ThemeCompatibleSilentSkip && isFullscreen && !_settings.SkipFirstSelectionAfterModeSwitch)
            {
                _fileLogger?.Info($"HandleGameSelected: Login skip active (Game: {game.Name})");
                _loginSkipActive = true;
                // Keep _firstSelect true so ShouldPlayMusic() continues to block
                return;
            }
            
            if (_loginSkipActive)
            {
                _fileLogger?.Info("HandleGameSelected: Clearing login skip");
                _loginSkipActive = false;
            }
            
            if (_skipFirstSelectActive)
            {
                _fileLogger?.Info("HandleGameSelected: Clearing skip flag - ready to allow music");
                _skipFirstSelectActive = false;
            }
            
            if (_loginSkipActive || _skipFirstSelectActive)
            {
                _fileLogger?.Info($"HandleGameSelected: Skip flags active - not playing music (LoginSkip: {_loginSkipActive}, SkipFirstSelect: {_skipFirstSelectActive})");
                return;
            }
            
            // Service handles: music file detection, default music fallback, and settings
            _fileLogger?.Info($"HandleGameSelected: Calling PlayGameMusic for {game.Name}");
            _playbackService?.PlayGameMusic(game, _settings, false);
            
            _firstSelect = false;
        }
        
        /// <summary>
        /// Handles login screen dismissal (controller/keyboard input)
        /// </summary>
        public void HandleLoginDismiss()
        {
            if (!_loginSkipActive)
            {
                _fileLogger?.Info("HandleLoginDismiss: Login skip not active, ignoring");
                return;
            }
            
            _fileLogger?.Info("HandleLoginDismiss: Clearing login skip");
            _loginSkipActive = false;
            // Clear skip flag when login is dismissed - we're past the skip window
            _skipFirstSelectActive = false;
            _firstSelect = false;
            
            // Short delay then attempt to play (matches current behavior)
            var timer = new System.Timers.Timer(150) { AutoReset = false };
            timer.Elapsed += (s, args) =>
            {
                timer.Dispose();
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var game = _getSelectedGame();
                    if (game != null && ShouldPlayMusic(game))
                    {
                        _fileLogger?.Info($"HandleLoginDismiss: Playing music for {game.Name}");
                        _playbackService?.PlayGameMusic(game, _settings, false);
                    }
                    else
                    {
                        _fileLogger?.Info($"HandleLoginDismiss: Not playing - game null or ShouldPlayMusic returned false");
                        // If no music to play, allow native music (if suppression is disabled)
                        // This ensures native music plays after login when no game music is available
                        // Note: We need to call this through a callback since coordinator doesn't have direct access
                        // The OnMusicStopped event will handle this when PlayGameMusic(null) is called
                    }
                });
            };
            timer.Start();
        }
        
        /// <summary>
        /// Handles view changes (e.g., user left login screen).
        /// Clears login skip and starts music if appropriate.
        /// </summary>
        public void HandleViewChange()
        {
            if (!_loginSkipActive || !_isFullscreen())
            {
                return;
            }
            
            var game = _getSelectedGame();
            if (game != null && _settings?.EnableMusic == true)
            {
                _fileLogger?.Info("HandleViewChange: Clearing login skip and starting music");
                _loginSkipActive = false;
                _skipFirstSelectActive = false;
                _firstSelect = false;
                
                if (ShouldPlayMusic(game))
                {
                    _playbackService?.PlayGameMusic(game, _settings, false);
                }
                else
                {
                    _fileLogger?.Info("HandleViewChange: Not playing - ShouldPlayMusic returned false");
                }
            }
        }
        
        /// <summary>
        /// Handles video playback state changes.
        /// Pauses music when video starts, resumes or starts music when video stops.
        /// </summary>
        /// <param name="isPlaying">True if video is playing; false if video stopped.</param>
        public void HandleVideoStateChange(bool isPlaying)
        {
            if (isPlaying)
            {
                _fileLogger?.Info("HandleVideoStateChange: Video playing - pausing music");
                if (_playbackService?.IsLoaded == true)
                {
                    _playbackService.Pause();
                }
            }
            else
            {
                _fileLogger?.Info("HandleVideoStateChange: Video stopped - resuming music");
                var game = _getSelectedGame();
                if (game != null && ShouldPlayMusic(game))
                {
                    if (_playbackService?.IsLoaded == true)
                    {
                        _playbackService.Resume();
                    }
                    else
                    {
                        _fileLogger?.Info($"HandleVideoStateChange: Starting music for {game.Name}");
                        _playbackService?.PlayGameMusic(game, _settings, false);
                    }
                }
                else
                {
                    _fileLogger?.Info("HandleVideoStateChange: Not resuming - game null or ShouldPlayMusic returned false");
                }
            }
        }
        
        /// <summary>
        /// Gets whether this is the first game selection.
        /// </summary>
        public bool IsFirstSelect() => _firstSelect;

        /// <summary>
        /// Gets whether login skip is currently active.
        /// </summary>
        public bool IsLoginSkipActive() => _loginSkipActive;
        
        /// <summary>
        /// Resets the first select flag.
        /// </summary>
        public void ResetFirstSelect() => _firstSelect = false;

        /// <summary>
        /// Sets the first select flag.
        /// </summary>
        /// <param name="value">The value to set.</param>
        public void SetFirstSelect(bool value) => _firstSelect = value;

        /// <summary>
        /// Sets the login skip active state.
        /// </summary>
        /// <param name="active">True to activate login skip; false to deactivate.</param>
        public void SetLoginSkipActive(bool active) => _loginSkipActive = active;
        
        /// <summary>
        /// Resets skip state when switching to fullscreen mode.
        /// Used for SkipFirstSelectionAfterModeSwitch feature.
        /// </summary>
        public void ResetSkipStateForModeSwitch()
        {
            if (_settings?.SkipFirstSelectionAfterModeSwitch == true)
            {
                _fileLogger?.Info("ResetSkipStateForModeSwitch: Resetting _firstSelect and _skipFirstSelectActive for mode switch");
                _firstSelect = true;
                _skipFirstSelectActive = false;
                _hasSeenFullscreen = false;
            }
        }

        /// <summary>
        /// Handles SettingsService settings changed events.
        /// Automatically called when settings are updated - no manual UpdateSettings() needed.
        /// </summary>
        private void OnSettingsChanged(object sender, SettingsChangedEventArgs e)
        {
            if (e.NewSettings != null)
            {
                _settings = e.NewSettings;
                _fileLogger?.Info("MusicPlaybackCoordinator: Settings updated automatically via SettingsService");
            }
        }

        /// <summary>
        /// Updates the settings reference.
        /// </summary>
        /// <param name="newSettings">The new settings instance.</param>
        /// <remarks>
        /// DEPRECATED: SettingsService now handles updates automatically via events.
        /// Kept for backward compatibility.
        /// </remarks>
        [Obsolete("SettingsService now handles updates automatically. This method will be removed in a future version.")]
        public void UpdateSettings(UniPlaySongSettings newSettings)
        {
            if (newSettings != null)
            {
                _settings = newSettings;
                _fileLogger?.Info("MusicPlaybackCoordinator: Settings updated (deprecated method)");
            }
        }
    }
}

