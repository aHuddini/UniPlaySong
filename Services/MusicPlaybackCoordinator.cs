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
        
        // State - centralized here
        private bool _firstSelect = true;
        private bool _loginSkipActive = false;
        private bool _skipFirstSelectActive = false; // Tracks if we're in the "skip first select" window
        private bool _hasSeenFullscreen = false; // Track if we've seen fullscreen mode yet
        private Game _currentGame;
        
        // Dependencies (using Func delegates for testability)
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
            
            // Subscribe to automatic settings updates (Phase 3)
            _settingsService.SettingsChanged += OnSettingsChanged;
            _fileLogger?.Info("MusicPlaybackCoordinator: Subscribed to SettingsService");
        }
        
        /// <summary>
        /// Central gatekeeper - ALL music playback decisions go through here
        /// </summary>
        public bool ShouldPlayMusic(Game game)
        {
            // Basic checks
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
            
            // Login skip check
            if (_loginSkipActive)
            {
                _fileLogger?.Info("ShouldPlayMusic: Returning false - login skip active");
                return false;
            }
            
            // First select skip check - CENTRAL GATEKEEPER
            // Check both _firstSelect (for initial state) and _skipFirstSelectActive (for skip window)
            if ((_firstSelect || _skipFirstSelectActive) && _settings.SkipFirstSelectionAfterModeSwitch)
            {
                _fileLogger?.Info($"ShouldPlayMusic: Returning false - first select skip enabled (FirstSelect: {_firstSelect}, SkipActive: {_skipFirstSelectActive})");
                return false;
            }
            
            // Mode-based checks
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
        /// Handles game selection - coordinates skip logic and playback
        /// </summary>
        public void HandleGameSelected(Game game, bool isFullscreen)
        {
            // Reset skip state when entering fullscreen for the first time (for SkipFirstSelectionAfterModeSwitch)
            // This matches PNS's "Skip on startup" behavior - skip the first selection after mode switch
            if (isFullscreen && !_hasSeenFullscreen && _settings?.SkipFirstSelectionAfterModeSwitch == true)
            {
                _fileLogger?.Info("HandleGameSelected: First time seeing fullscreen mode - resetting skip state");
                _firstSelect = true;
                _skipFirstSelectActive = false;
                _hasSeenFullscreen = true;
            }
            
            if (game == null || _settings?.EnableMusic != true)
            {
                // Use PlayGameMusic(null) to handle fade-out properly (it calls FadeOutAndStop internally)
                // This ensures smooth fade-out when switching to games with no music
                _fileLogger?.Info("HandleGameSelected: No game or music disabled - fading out playback");
                _playbackService?.PlayGameMusic(null, _settings, false);
                _firstSelect = false;
                _currentGame = null;
                return;
            }
            
            var wasFirstSelect = _firstSelect;
            _currentGame = game;
            
            _fileLogger?.Info($"HandleGameSelected: Game={game.Name}, IsFullscreen={isFullscreen}, WasFirstSelect={wasFirstSelect}, LoginSkipActive={_loginSkipActive}, HasSeenFullscreen={_hasSeenFullscreen}");
            
            // SkipFirstSelectionAfterModeSwitch - takes precedence
            if (wasFirstSelect && _settings.SkipFirstSelectionAfterModeSwitch)
            {
                _fileLogger?.Info($"HandleGameSelected: Skipping first selection (Game: {game.Name})");
                // Set skip flag to block music from all paths (HandleVideoStateChange, etc.)
                _skipFirstSelectActive = true;
                // Clear _firstSelect so next selection won't skip again
                _firstSelect = false;
                return;
            }
            
            // ThemeCompatibleSilentSkip - only if SkipFirstSelectionAfterModeSwitch is disabled
            if (wasFirstSelect && _settings.ThemeCompatibleSilentSkip && isFullscreen && !_settings.SkipFirstSelectionAfterModeSwitch)
            {
                _fileLogger?.Info($"HandleGameSelected: Login skip active (Game: {game.Name})");
                _loginSkipActive = true;
                // DON'T clear _firstSelect here - keep it true so ShouldPlayMusic() continues to block
                // We'll clear it when login is dismissed or when we actually play music
                return;
            }
            
            // Clear login skip if active
            if (_loginSkipActive)
            {
                _fileLogger?.Info("HandleGameSelected: Clearing login skip");
                _loginSkipActive = false;
            }
            
            // Clear skip flag when we get to a selection that should play (second selection or later)
            if (_skipFirstSelectActive)
            {
                _fileLogger?.Info("HandleGameSelected: Clearing skip flag - ready to allow music");
                _skipFirstSelectActive = false;
            }
            
            // Check if skip flags are active - if so, don't play anything
            if (_loginSkipActive || _skipFirstSelectActive)
            {
                _fileLogger?.Info($"HandleGameSelected: Skip flags active - not playing music (LoginSkip: {_loginSkipActive}, SkipFirstSelect: {_skipFirstSelectActive})");
                return;
            }
            
            // Always call PlayGameMusic - let the service handle the logic
            // The service will:
            // 1. Check if game has music files
            // 2. If no game music, check for default music fallback
            // 3. Respect EnableMusic and other settings
            // This ensures default music plays when a game has no music, regardless of MusicState
            _fileLogger?.Info($"HandleGameSelected: Calling PlayGameMusic for {game.Name}");
            _playbackService?.PlayGameMusic(game, _settings, false);
            
            // Clear _firstSelect after processing (PNS pattern)
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
        /// Handles view changes (user left login screen)
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
                // Clear skip flag when view changes - we're past the skip window
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
        /// Handles video state changes (pause/resume)
        /// </summary>
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
                // Resume only if we should play
                var game = _getSelectedGame();
                if (game != null && ShouldPlayMusic(game))
                {
                    if (_playbackService?.IsLoaded == true)
                    {
                        _playbackService.Resume();
                    }
                    else
                    {
                        // Start playing if not loaded
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
        
        // State queries
        public bool IsFirstSelect() => _firstSelect;
        public bool IsLoginSkipActive() => _loginSkipActive;
        
        // State management
        public void ResetFirstSelect() => _firstSelect = false;
        public void SetFirstSelect(bool value) => _firstSelect = value;
        public void SetLoginSkipActive(bool active) => _loginSkipActive = active;
        
        /// <summary>
        /// Resets skip state when switching modes (for SkipFirstSelectionAfterModeSwitch)
        /// Should be called when switching to fullscreen mode
        /// </summary>
        public void ResetSkipStateForModeSwitch()
        {
            if (_settings?.SkipFirstSelectionAfterModeSwitch == true)
            {
                _fileLogger?.Info("ResetSkipStateForModeSwitch: Resetting _firstSelect and _skipFirstSelectActive for mode switch");
                _firstSelect = true;
                _skipFirstSelectActive = false;
                _hasSeenFullscreen = false; // Reset so we detect the next fullscreen entry
            }
        }

        /// <summary>
        /// Handles SettingsService settings changed event (Phase 3)
        /// Automatically called when settings are updated - no manual UpdateSettings() needed
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
        /// Updates the settings reference (called when settings are saved)
        /// DEPRECATED: SettingsService now handles this automatically via events
        /// Kept for backward compatibility during migration
        /// </summary>
        [Obsolete("SettingsService now handles updates automatically. This method will be removed in Phase 6.")]
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

