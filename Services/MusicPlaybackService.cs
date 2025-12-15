using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Players;
using UniPlaySong.Services;

namespace UniPlaySong.Services
{
    /// <summary>
    /// High-level service for managing music playback for games
    /// </summary>
    public class MusicPlaybackService : IMusicPlaybackService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IMusicPlayer _musicPlayer;
        private readonly GameMusicFileService _fileService;
        private readonly FileLogger _fileLogger;
        private readonly ErrorHandlerService _errorHandler;
        private readonly Dictionary<string, bool> _primarySongPlayed = new Dictionary<string, bool>();
        
        /// <summary>
        /// Event fired when music stops (for native music restoration)
        /// </summary>
        public event Action<UniPlaySongSettings> OnMusicStopped;
        
        /// <summary>
        /// Event fired when music starts (for native music suppression)
        /// </summary>
        public event Action<UniPlaySongSettings> OnMusicStarted;
        
        // PNS-style state tracking for default music (single-player approach)
        private bool _isPlayingDefaultMusic = false;
        private string _lastDefaultMusicPath = null;
        private TimeSpan _defaultMusicPausedOnTime = default;

        // Preview mode state tracking
        private DateTime _songStartTime = DateTime.MinValue;
        private UniPlaySongSettings _currentSettings;
        private System.Windows.Threading.DispatcherTimer _previewTimer;

        private MusicFader _fader;
        private double _targetVolume = Constants.DefaultTargetVolume;
        private double _fadeInDuration = Constants.DefaultFadeInDuration;
        private double _fadeOutDuration = Constants.DefaultFadeOutDuration;

        private string _currentGameId;
        private string _currentSongPath;
        private string _previousSongPath; // Track last played song to avoid immediate repeats
        private Game _currentGame; // Track current game for randomization on song end
        private bool _isPaused;

        // Single-player approach: only check main player
        public bool IsPlaying => _musicPlayer?.IsActive ?? false;
        public bool IsPaused => _isPaused;
        public bool IsLoaded => _musicPlayer?.IsLoaded ?? false;

        public MusicPlaybackService(IMusicPlayer musicPlayer, GameMusicFileService fileService, FileLogger fileLogger = null, ErrorHandlerService errorHandler = null)
        {
            _musicPlayer = musicPlayer ?? throw new ArgumentNullException(nameof(musicPlayer));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _fileLogger = fileLogger;
            _errorHandler = errorHandler;

            // Initialize fader with separate in/out durations
            _fader = new MusicFader(
                _musicPlayer,
                () => _targetVolume,
                () => _fadeInDuration,
                () => _fadeOutDuration,
                _errorHandler
            );

            _musicPlayer.MediaEnded += OnMediaEnded;

            // Initialize preview timer (checks every second)
            _previewTimer = new System.Windows.Threading.DispatcherTimer();
            _previewTimer.Interval = TimeSpan.FromSeconds(1);
            _previewTimer.Tick += OnPreviewTimerTick;
        }

        /// <summary>
        /// Checks if a given path is the default music (either native or custom)
        /// UseNativeMusicAsDefault and DefaultMusicPath are mutually exclusive at playback time
        /// </summary>
        private bool IsDefaultMusicPath(string path, UniPlaySongSettings settings)
        {
            if (string.IsNullOrWhiteSpace(path) || settings == null || !settings.EnableDefaultMusic)
            {
                return false;
            }

            if (settings.UseNativeMusicAsDefault == true)
            {
                // Check against native music path (get directly, don't use DefaultMusicPath)
                var nativeMusicPath = Common.PlayniteThemeHelper.FindBackgroundMusicFile(null);
                return !string.IsNullOrWhiteSpace(nativeMusicPath) &&
                       string.Equals(path, nativeMusicPath, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // Check against custom default music path
                return !string.IsNullOrWhiteSpace(settings.DefaultMusicPath) &&
                       string.Equals(path, settings.DefaultMusicPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// PNS-style pause for default music when switching to game music
        /// Saves position BEFORE pausing so it can be restored later
        /// Note: This is only called when we want to pause (not when switching to game music)
        /// </summary>
        private void PauseDefaultMusic(UniPlaySongSettings settings)
        {
            // Use helper method to check if current song is default music
            if (_isPlayingDefaultMusic && 
                IsDefaultMusicPath(_currentSongPath, settings) &&
                _musicPlayer?.IsLoaded == true)
            {
                // CRITICAL: Save current position BEFORE pausing/closing
                _defaultMusicPausedOnTime = _musicPlayer.CurrentTime ?? default;
                _fileLogger?.Info($"Pausing default music at position {_defaultMusicPausedOnTime.TotalSeconds:F2}s: {Path.GetFileName(_currentSongPath)}");
                _isPlayingDefaultMusic = false;
                
                // Just pause immediately (no fade needed for pause-only operations)
                _musicPlayer.Pause();
                _isPaused = true;
            }
        }

        /// <summary>
        /// PNS-style resume for default music when switching back from game music
        /// </summary>
        private void ResumeDefaultMusic(string defaultMusicPath, UniPlaySongSettings settings)
        {
            // Check if we can resume (same file, paused, and already loaded)
            // Note: defaultMusicPath can be either native or custom, so we check by path equality
            if (!_isPlayingDefaultMusic && 
                string.Equals(_lastDefaultMusicPath, defaultMusicPath, StringComparison.OrdinalIgnoreCase) &&
                _isPaused &&
                _musicPlayer?.IsLoaded == true &&
                string.Equals(_musicPlayer.Source, defaultMusicPath, StringComparison.OrdinalIgnoreCase))
            {
                // Same file and paused - resume from paused state (position already preserved)
                _fileLogger?.Info($"Resuming paused default music from position {_defaultMusicPausedOnTime.TotalSeconds:F2}s: {Path.GetFileName(defaultMusicPath)}");
                _fader.Resume();
                _isPaused = false;
                _isPlayingDefaultMusic = true;
                // Notify that music started (for native music suppression)
                OnMusicStarted?.Invoke(settings);
            }
            else
            {
                // Different file, first time, or not loaded - load and play from saved position
                _fileLogger?.Info($"Loading and playing default music from position {_defaultMusicPausedOnTime.TotalSeconds:F2}s: {Path.GetFileName(defaultMusicPath)}");
                _lastDefaultMusicPath = defaultMusicPath;
                _isPlayingDefaultMusic = true;
                
                // Stop any current playback first
                if (_musicPlayer?.IsActive == true || _musicPlayer?.IsLoaded == true)
                {
                    _musicPlayer.Stop();
                }
                
                // Load and play with fade-in from saved position (or beginning if no saved position)
                _musicPlayer.Load(defaultMusicPath);
                _musicPlayer.Volume = 0;
                _musicPlayer.Play(_defaultMusicPausedOnTime);
                _fader.FadeIn();
                _isPaused = false;
                // Notify that music started (for native music suppression)
                OnMusicStarted?.Invoke(settings);
            }
        }

        public void PlayGameMusic(Game game) => PlayGameMusic(game, null, false);
        public void PlayGameMusic(Game game, UniPlaySongSettings settings) => PlayGameMusic(game, settings, false);

        public void PlayGameMusic(Game game, UniPlaySongSettings settings, bool forceReload)
        {
            if (game == null)
            {
                FadeOutAndStop();
                // Notify that we stopped music (for native music restoration)
                OnMusicStopped?.Invoke(settings);
                return;
            }

            // Update settings
            if (settings != null)
            {
                _fadeInDuration = settings.FadeInDuration;
                _fadeOutDuration = settings.FadeOutDuration;
                _targetVolume = settings.MusicVolume / Constants.VolumeDivisor;
                _currentSettings = settings; // Store for preview mode checks

                // Note: We don't check EnableMusic here for default music fallback
                // Default music should work even if EnableMusic is false (it's a fallback)
                // But we still respect it for regular game music
            }

            try
            {
                var gameId = game.Id.ToString();
                var songs = _fileService.GetAvailableSongs(game);
                
                // PNS PATTERN: Add default music to songs list if no game music found
                // UseNativeMusicAsDefault and DefaultMusicPath are mutually exclusive at playback time
                // DefaultMusicPath is NEVER modified - it remains separate and untouched
                if (songs.Count == 0 && settings?.EnableDefaultMusic == true)
                {
                    if (settings.UseNativeMusicAsDefault == true)
                    {
                        // Using native music as default - get path directly from Playnite theme
                        // DO NOT use DefaultMusicPath - it remains untouched for custom music
                        var nativeMusicPath = Common.PlayniteThemeHelper.FindBackgroundMusicFile(null);
                        if (!string.IsNullOrWhiteSpace(nativeMusicPath) && File.Exists(nativeMusicPath))
                        {
                            _fileLogger?.Info($"No game music found for {game.Name}, using native Playnite music as default: {Path.GetFileName(nativeMusicPath)}");
                            songs.Add(nativeMusicPath);
                        }
                        else
                        {
                            _fileLogger?.Warn($"UseNativeMusicAsDefault is enabled but native music file not found at expected location.");
                        }
                    }
                    else
                    {
                        // Using custom default music file - use DefaultMusicPath (unchanged)
                        if (!string.IsNullOrWhiteSpace(settings.DefaultMusicPath) &&
                            File.Exists(settings.DefaultMusicPath))
                        {
                            _fileLogger?.Info($"No game music found for {game.Name}, adding custom default music to songs list: {Path.GetFileName(settings.DefaultMusicPath)}");
                            songs.Add(settings.DefaultMusicPath);
                        }
                    }
                }
                
                _fileLogger?.Info($"PlayGameMusic: {game.Name} (ID: {gameId}) - Found {songs.Count} songs, CurrentGameId: {_currentGameId ?? "null"}, CurrentSongPath: {_currentSongPath ?? "null"}");

                if (songs.Count == 0)
                {
                    // No game music and no default music - stop playback
                    _fileLogger?.Info($"No game music or default music available, stopping playback");
                    _currentGameId = gameId;
                    _currentGame = null; // Clear current game when stopping
                    FadeOutAndStop();
                    // Notify that we stopped music (for native music restoration)
                    OnMusicStopped?.Invoke(settings);
                    return;
                }
                
                // For regular game music, check EnableMusic
                if (settings != null && !settings.EnableMusic)
                {
                    // Update game ID even if music is disabled (for state tracking)
                    _currentGameId = gameId;
                    _currentGame = null; // Clear current game when stopping
                    FadeOutAndStop();
                    // Notify that we stopped music (for native music restoration)
                    OnMusicStopped?.Invoke(settings);
                    return;
                }
                
                // Calculate isNewGame BEFORE updating _currentGameId
                // This is critical - we need to compare with the PREVIOUS game ID
                var previousGameId = _currentGameId;
                var isNewGame = previousGameId == null || previousGameId != gameId;
                
                // Update game ID now (before song selection, so SelectSongToPlay can use it)
                _currentGameId = gameId;
                _currentGame = game; // Track current game for randomization on song end
                
                // If switching to a new game, clear current song path to force new song selection
                // EXCEPTION: Don't clear if current song is default music - we want to preserve it when switching between games with no music
                bool isCurrentDefaultMusic = IsDefaultMusicPath(_currentSongPath, settings);
                if (isNewGame && !isCurrentDefaultMusic)
                {
                    _fileLogger?.Info($"New game detected (previous: {previousGameId ?? "null"}, current: {gameId}), clearing current song path (was: {_currentSongPath ?? "null"})");
                    _currentSongPath = null;
                }
                else if (isNewGame && isCurrentDefaultMusic)
                {
                    _fileLogger?.Info($"New game detected but current song is default music - preserving to continue playback: {Path.GetFileName(_currentSongPath)}");
                }
                
                // Ensure settings are updated for game music (volume, fade durations)
                // This is important when switching from default music to game music
                if (settings != null)
                {
                    _fadeInDuration = settings.FadeInDuration;
                    _fadeOutDuration = settings.FadeOutDuration;
                    _targetVolume = settings.MusicVolume / Constants.VolumeDivisor;
                    _fileLogger?.Info($"PlayGameMusic: Updated settings (TargetVolume: {_targetVolume}, FadeInDuration: {_fadeInDuration}, FadeOutDuration: {_fadeOutDuration})");
                }

                string songToPlay = SelectSongToPlay(game, songs, isNewGame);
                
                if (string.IsNullOrWhiteSpace(songToPlay) || !File.Exists(songToPlay))
                {
                    _fileLogger?.Warn($"Song not found: {songToPlay}");
                    FadeOutAndStop();
                    return;
                }

                // PNS PATTERN: Check if this is default music (native or custom)
                // UseNativeMusicAsDefault and DefaultMusicPath are mutually exclusive at playback time
                bool isDefaultMusic = IsDefaultMusicPath(songToPlay, settings);
                
                // If this is default music, use single-player approach
                if (isDefaultMusic)
                {
                    _fileLogger?.Info($"Playing default music (single-player): {Path.GetFileName(songToPlay)}");
                    _fileLogger?.Info($"  CurrentSongPath: {_currentSongPath ?? "null"}, IsPlayingDefaultMusic: {_isPlayingDefaultMusic}, IsLoaded: {_musicPlayer?.IsLoaded}, IsActive: {IsPlaying}, Source: {_musicPlayer?.Source ?? "null"}");
                    
                    // Check if default music is already playing - if so, just continue (preserve position)
                    // This handles switching between games with no music (both use default music)
                    // Use case-insensitive path comparison to handle path variations (native music file paths)
                    if (string.Equals(_currentSongPath, songToPlay, StringComparison.OrdinalIgnoreCase) && 
                        _isPlayingDefaultMusic && 
                        _musicPlayer?.IsLoaded == true && 
                        string.Equals(_musicPlayer.Source, songToPlay, StringComparison.OrdinalIgnoreCase) &&
                        IsPlaying)
                    {
                        _fileLogger?.Info($"Default music already playing - continuing playback (position preserved): {Path.GetFileName(songToPlay)}");
                        return; // Already playing, don't restart
                    }
                    
                    // Also check if default music is paused (from switching to game music)
                    // In this case, we want to resume from saved position
                    // Use case-insensitive path comparison to handle path variations (native music file paths)
                    if (string.Equals(_currentSongPath, songToPlay, StringComparison.OrdinalIgnoreCase) && 
                        _isPaused && 
                        string.Equals(_lastDefaultMusicPath, songToPlay, StringComparison.OrdinalIgnoreCase) &&
                        _musicPlayer?.IsLoaded == true &&
                        string.Equals(_musicPlayer.Source, songToPlay, StringComparison.OrdinalIgnoreCase))
                    {
                        _fileLogger?.Info($"Default music is paused - resuming from saved position: {_defaultMusicPausedOnTime.TotalSeconds:F2}s");
                        _fader.Resume();
                        _isPaused = false;
                        _isPlayingDefaultMusic = true;
                        return; // Resumed, don't reload
                    }
                    
                    // Switching from game music to default music - use Switch() for proper fade-out
                    if (_musicPlayer?.IsActive == true || _musicPlayer?.IsLoaded == true)
                    {
                        // Only fade out if it's not already the default music
                        // Use case-insensitive path comparison to handle path variations (native music file paths)
                        if (!string.Equals(_currentSongPath, songToPlay, StringComparison.OrdinalIgnoreCase))
                        {
                            _fileLogger?.Info($"Switching from game music to default music with fade-out: {Path.GetFileName(songToPlay)}");
                            
                            // Use Switch() to fade out game music, then fade in default music (matching game music switching)
                            _fader.Switch(
                                stopAction: () =>
                                {
                                    // Close game music player
                                    StopPreviewTimer();
                                    _musicPlayer.Close();
                                    _currentSongPath = null;
                                    _isPaused = false;
                                },
                                preloadAction: () =>
                                {
                                    // Preload default music during fade-out
                                    _musicPlayer.PreLoad(songToPlay);
                                },
                                playAction: () =>
                                {
                                    // Load and play default music from saved position (or beginning)
                                    _musicPlayer.Load(songToPlay);
                                    _musicPlayer.Volume = 0; // Fader will control volume
                                    _musicPlayer.Play(_defaultMusicPausedOnTime);
                                    _currentSongPath = songToPlay;
                                    _isPaused = false;
                                    _isPlayingDefaultMusic = true;
                                    _lastDefaultMusicPath = songToPlay;
                                    _songStartTime = DateTime.MinValue; // Not tracking for default music
                                    _fileLogger?.Info($"Playing default music (switched from game music) at position {_defaultMusicPausedOnTime.TotalSeconds:F2}s");
                                    // Notify that music started (for native music suppression)
                                    OnMusicStarted?.Invoke(settings);
                                }
                            );
                            return;
                        }
                    }
                    
                    // Use PNS-style resume helper (for when default music is already loaded/paused)
                    ResumeDefaultMusic(songToPlay, settings);
                    _currentSongPath = songToPlay;
                    return;
                }

                // Skip if same song already playing for the same game
                // But always switch if it's a new game (even if same song path)
                // Use case-insensitive path comparison to handle path variations (native music file paths)
                if (!forceReload && !isNewGame && string.Equals(_currentSongPath, songToPlay, StringComparison.OrdinalIgnoreCase) && IsPlaying)
                {
                    _fileLogger?.Info($"Same song playing for same game, skipping: {Path.GetFileName(songToPlay)}");
                    return;
                }

                // PNS PATTERN: If switching from default music to game music, save position and use Switch() for fade-out
                // CRITICAL: Check this BEFORE we start fading out, so we can save position
                bool wasDefaultMusic = _isPlayingDefaultMusic && IsDefaultMusicPath(_currentSongPath, settings);
                
                // Log game switch for debugging
                if (isNewGame)
                {
                    _fileLogger?.Info($"New game detected (previous: {previousGameId ?? "null"}, current: {gameId}), switching to: {Path.GetFileName(songToPlay)}");
                }

                var newSongPath = songToPlay;
                
                // Single-player approach: check main player for game music
                bool shouldFadeOut = (_musicPlayer?.IsActive == true || _musicPlayer?.IsLoaded == true) && 
                                     _currentSongPath != newSongPath;
                
                if (shouldFadeOut)
                {
                    // Save default music position if switching from default to game music
                    if (wasDefaultMusic && !isDefaultMusic)
                    {
                        _defaultMusicPausedOnTime = _musicPlayer.CurrentTime ?? default;
                        _fileLogger?.Info($"Switching from default music to game music with fade-out. Saved position: {_defaultMusicPausedOnTime.TotalSeconds:F2}s");
                        _isPlayingDefaultMusic = false;
                    }
                    
                    // Use Switch() for smooth fade-out/fade-in transition (works for both game→game and default→game)
                    _fileLogger?.Info($"Switching to: {Path.GetFileName(newSongPath)}");
                    
                    _fader.Switch(
                        stopAction: () =>
                        {
                            // Close current player
                            StopPreviewTimer();
                            _musicPlayer.Close();
                            _currentSongPath = null;
                            _isPaused = false; // Reset pause state when closing
                        },
                        preloadAction: () =>
                        {
                            // Preload new music during fade-out
                            _musicPlayer.PreLoad(newSongPath);
                        },
                        playAction: () =>
                        {
                            // PNS pattern: Fader sets volume to 0 before calling this
                            // Just load and play - don't set volume (fader controls it)
                            _musicPlayer.Load(newSongPath);
                            _musicPlayer.Play();
                            _currentSongPath = newSongPath;
                            _isPaused = false;
                            MarkSongStart(); // Track start time for preview mode
                            _fileLogger?.Info($"Playing (switched): {Path.GetFileName(newSongPath)}");
                            // Notify that music started (for native music suppression)
                            OnMusicStarted?.Invoke(settings);
                        }
                    );
                }
                else if (_isPaused && wasDefaultMusic)
                {
                    // Default music was paused (not playing), now switching to game music
                    // Close the paused default music player and start game music
                    _fileLogger?.Info($"Switching from paused default music to game music: {Path.GetFileName(newSongPath)}");
                    
                    _musicPlayer?.Close();
                    _currentSongPath = null;
                    _isPaused = false;
                    
                    // Load and play game music with fade-in
                    _musicPlayer.Load(newSongPath);
                    _musicPlayer.Volume = 0;
                    _musicPlayer.Play();
                    _currentSongPath = newSongPath;
                    MarkSongStart(); // Track start time for preview mode
                    _fader.FadeIn();
                    _fileLogger?.Info($"Playing game music (from paused default): {Path.GetFileName(newSongPath)}");
                    // Notify that music started (for native music suppression)
                    OnMusicStarted?.Invoke(settings);
                }
                else
                {
                    // Initial playback - use fader for smooth fade-in (SDL2 advantage)
                    _fileLogger?.Info($"Initial playback with fade-in: {Path.GetFileName(newSongPath)}");
                    
                    _currentSongPath = null;
                    
                    // Load and play at volume 0, then fade in
                    _musicPlayer.Load(newSongPath);
                    _musicPlayer.Volume = 0; // Start at 0 for fade-in
                    _musicPlayer.Play();
                    _currentSongPath = newSongPath;
                    _isPaused = false;
                    MarkSongStart(); // Track start time for preview mode

                    // Start fade-in
                    _fader.FadeIn();

                    _fileLogger?.Info($"Playing (initial with fade-in): {Path.GetFileName(newSongPath)}, starting at volume 0");
                    // Notify that music started (for native music suppression)
                    OnMusicStarted?.Invoke(settings);
                }

                Logger.Info($"Playing music for '{game.Name}': {Path.GetFileName(songToPlay)}");
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: $"playing music for game '{game?.Name}'",
                    showUserMessage: false
                );
                // Also log to file logger for detailed tracking
                _fileLogger?.Error($"Error: {ex.Message}", ex);
            }
        }

        public void Stop()
        {
            try
            {
                StopPreviewTimer();
                _musicPlayer?.Stop();
                _musicPlayer?.Close();
                _currentSongPath = null;
                _currentGameId = null;
                _currentGame = null; // Clear current game tracking
                _isPaused = false;

                // Reset default music state
                _isPlayingDefaultMusic = false;
                _lastDefaultMusicPath = null;
                _defaultMusicPausedOnTime = default;

                // Reset preview mode state
                _songStartTime = DateTime.MinValue;
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "stopping music playback",
                    showUserMessage: false
                );
            }
        }

        private void FadeOutAndStop()
        {
            // Single-player approach: only check main player
            bool playerActive = (_musicPlayer?.IsLoaded ?? false) || (_musicPlayer?.IsActive ?? false);

            if (playerActive)
            {
                StopPreviewTimer();
                _fader.FadeOutAndStop(() =>
                {
                    _musicPlayer?.Close();
                    _currentSongPath = null;
                    _currentGameId = null;
                    _currentGame = null; // Clear current game tracking
                    _isPaused = false;

                    // Reset default music state
                    _isPlayingDefaultMusic = false;
                    _lastDefaultMusicPath = null;
                    _defaultMusicPausedOnTime = default;

                    // Reset preview mode state
                    _songStartTime = DateTime.MinValue;
                });
            }
            else
            {
                Stop();
            }
        }

        public void Pause()
        {
            // Single-player approach: pause main player
            if (_musicPlayer?.IsLoaded == true)
            {
                StopPreviewTimer();
                _fader.Pause();
                _isPaused = true;
            }
        }

        public void Resume()
        {
            // Single-player approach: resume main player
            if (_isPaused && _musicPlayer?.IsLoaded == true)
            {
                _fader.Resume();
                _isPaused = false;
            }
        }

        public List<string> GetAvailableSongs(Game game)
        {
            return _fileService?.GetAvailableSongs(game) ?? new List<string>();
        }

        public void SetVolume(double volume)
        {
            _targetVolume = Math.Max(0.0, Math.Min(1.0, volume));
            
            // Single-player approach: update volume for main player
            if (_musicPlayer != null && !_isPaused && _musicPlayer.IsActive)
            {
                _musicPlayer.Volume = _targetVolume;
            }
        }

        public double GetVolume() => _targetVolume;

        public void SetFadeInDuration(double seconds)
        {
            _fadeInDuration = Math.Max(0.05, Math.Min(2.0, seconds));
        }

        public void SetFadeOutDuration(double seconds)
        {
            _fadeOutDuration = Math.Max(0.05, Math.Min(2.0, seconds));
        }

        public void LoadAndPlayFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                _fileLogger?.Warn($"Cannot load file: {filePath ?? "null"}");
                return;
            }

            try
            {
                _fileLogger?.Info($"LoadAndPlayFile: {filePath}");
                _musicPlayer.Load(filePath);
                _musicPlayer.Volume = 0;
                _musicPlayer.Play();
                _fader.FadeIn();
                _isPaused = false;
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "loading and playing file",
                    showUserMessage: false
                );
            }
        }

        private string SelectSongToPlay(Game game, List<string> songs, bool isNewGame)
        {
            if (songs.Count == 0) return null;

            // PNS PATTERN: Check if current song is still valid (same directory or same default music)
            // This allows default music to continue playing when switching between games with no music
            // Also allows resuming paused default music (check happens in caller)
            if (!string.IsNullOrWhiteSpace(_currentSongPath) && songs.Contains(_currentSongPath))
            {
                return _currentSongPath;
            }

            // Check for primary song (only for game music, not default music)
            var gameDirectory = _fileService.GetGameMusicDirectory(game);
            if (!string.IsNullOrEmpty(gameDirectory))
            {
                var primarySong = PrimarySongManager.GetPrimarySong(gameDirectory, null);
                // Primary song plays once on first selection
                if (isNewGame && primarySong != null && songs.Contains(primarySong))
                {
                    var primaryPlayed = _primarySongPlayed.ContainsKey(gameDirectory) && _primarySongPlayed[gameDirectory];
                    if (!primaryPlayed)
                    {
                        _primarySongPlayed[gameDirectory] = true;
                        _previousSongPath = primarySong; // Track for randomization
                        return primarySong;
                    }
                }
            }

            // PNS PATTERN: Randomization logic (similar to PlayniteSound)
            if (songs.Count > 1 && _currentSettings != null)
            {
                bool shouldRandomize = false;

                // Randomize on every select (when game changes)
                if (isNewGame && _currentSettings.RandomizeOnEverySelect)
                {
                    shouldRandomize = true;
                }

                if (shouldRandomize)
                {
                    // Select random song, avoiding immediate repeat
                    var random = new Random();
                    string selected;
                    int attempts = 0;
                    do
                    {
                        selected = songs[random.Next(songs.Count)];
                        attempts++;
                    }
                    while (selected == _previousSongPath && songs.Count > 1 && attempts < 10); // Prevent infinite loops

                    _previousSongPath = selected;
                    _fileLogger?.Info($"Randomized song selection: {Path.GetFileName(selected)} (RandomizeOnEverySelect)");
                    return selected;
                }
            }

            // Default: Return first available song (could be game music or default music)
            var firstSong = songs.FirstOrDefault();
            _previousSongPath = firstSong;
            return firstSong;
        }

        /// <summary>
        /// Marks the start time for preview mode tracking
        /// </summary>
        private void MarkSongStart()
        {
            _songStartTime = DateTime.Now;

            // Start preview timer if preview mode is enabled for game music
            if (_currentSettings?.EnablePreviewMode == true &&
                !_isPlayingDefaultMusic &&
                !IsDefaultMusicPath(_currentSongPath, _currentSettings))
            {
                _previewTimer.Start();
                _fileLogger?.Info($"Preview timer started for: {Path.GetFileName(_currentSongPath)} (duration: {_currentSettings.PreviewDuration}s)");
            }
        }

        /// <summary>
        /// Stops the preview timer
        /// </summary>
        private void StopPreviewTimer()
        {
            if (_previewTimer.IsEnabled)
            {
                _previewTimer.Stop();
                _fileLogger?.Info("Preview timer stopped");
            }
        }

        /// <summary>
        /// Timer tick handler for preview mode - checks if we should restart the song
        /// </summary>
        private void OnPreviewTimerTick(object sender, EventArgs e)
        {
            try
            {
                // Check if we should restart for preview
                if (ShouldRestartForPreview() && _musicPlayer?.IsActive == true)
                {
                    _fileLogger?.Info($"Preview duration reached ({_currentSettings.PreviewDuration}s), restarting with fade: {Path.GetFileName(_currentSongPath)}");

                    // Stop timer before restart
                    _previewTimer.Stop();

                    // Store the current song path for the restart
                    var songToRestart = _currentSongPath;

                    // Use fader to create smooth fade-out/fade-in transition
                    _fader.Switch(
                        stopAction: () =>
                        {
                            // Stop current playback
                            _musicPlayer.Stop();
                        },
                        preloadAction: () =>
                        {
                            // Preload the same song (for seamless restart)
                            _musicPlayer.PreLoad(songToRestart);
                        },
                        playAction: () =>
                        {
                            // Load and restart from beginning
                            _musicPlayer.Load(songToRestart);
                            _musicPlayer.Play(TimeSpan.Zero);

                            // Reset start time and restart timer for next cycle
                            MarkSongStart();

                            _fileLogger?.Info($"Preview mode: Restarted from beginning with fade: {Path.GetFileName(songToRestart)}");
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "preview timer tick",
                    showUserMessage: false
                );
            }
        }

        /// <summary>
        /// Checks if preview mode should apply and if the song should restart
        /// </summary>
        private bool ShouldRestartForPreview()
        {
            // Preview mode only applies to game music (not default music)
            if (_isPlayingDefaultMusic || IsDefaultMusicPath(_currentSongPath, _currentSettings))
            {
                return false;
            }

            // Check if preview mode is enabled
            if (_currentSettings == null || !_currentSettings.EnablePreviewMode)
            {
                return false;
            }

            // Check if we've been playing long enough to restart
            if (_songStartTime == DateTime.MinValue)
            {
                return false; // No start time recorded
            }

            var elapsed = DateTime.Now - _songStartTime;
            var previewDuration = TimeSpan.FromSeconds(_currentSettings.PreviewDuration);

            return elapsed >= previewDuration;
        }

        private void OnMediaEnded(object sender, EventArgs e)
        {
            // Handle song end (works for both game music and default music)
            // Single-player approach: only main player exists
            if (sender == _musicPlayer && !string.IsNullOrWhiteSpace(_currentSongPath) && File.Exists(_currentSongPath))
            {
                try
                {
                    // PNS PATTERN: Check for randomization on song end (similar to PlayniteSound)
                    if (_currentSettings?.RandomizeOnMusicEnd == true && _currentGame != null)
                    {
                        // Only randomize for game music (not default music)
                        bool isDefaultMusic = IsDefaultMusicPath(_currentSongPath, _currentSettings);
                        if (!isDefaultMusic)
                        {
                            // Get available songs for current game
                            var songs = _fileService.GetAvailableSongs(_currentGame);
                            if (songs.Count > 1)
                            {
                                // Select random song (avoiding immediate repeat)
                                var random = new Random();
                                string nextSong;
                                int attempts = 0;
                                do
                                {
                                    nextSong = songs[random.Next(songs.Count)];
                                    attempts++;
                                }
                                while (nextSong == _currentSongPath && songs.Count > 1 && attempts < 10);

                                // Play the new random song with fade-in
                                _previousSongPath = _currentSongPath; // Track for future randomization
                                _currentSongPath = nextSong;
                                LoadAndPlayFile(nextSong);
                                _fileLogger?.Info($"Randomized to next song on end: {Path.GetFileName(nextSong)} (RandomizeOnMusicEnd)");
                                return;
                            }
                        }
                    }

                    // Check if preview mode should restart the song
                    if (ShouldRestartForPreview())
                    {
                        // Preview mode: Song ended after preview duration - restart from beginning
                        // Just restart immediately (no fade needed since song already ended naturally)
                        _musicPlayer.Play(TimeSpan.Zero);
                        MarkSongStart(); // Reset timer
                        _fileLogger?.Info($"Preview mode: Song ended, restarting from beginning: {Path.GetFileName(_currentSongPath)}");
                    }
                    else
                    {
                        // Normal loop behavior - continue playing from beginning
                        _musicPlayer.Play();
                        _fileLogger?.Info($"Looping music: {Path.GetFileName(_currentSongPath)}");
                    }
                }
                catch (Exception ex)
                {
                    _errorHandler?.HandleError(
                        ex,
                        context: "handling song end",
                        showUserMessage: false
                    );
                }
            }
        }
    }
}
