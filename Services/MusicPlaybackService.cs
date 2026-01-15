using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Models;
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

        /// <summary>
        /// Event fired when a song reaches its natural end (before looping/randomizing).
        /// Used by batch download to queue next random game's music.
        /// </summary>
        public event Action OnSongEnded;

        /// <summary>
        /// Event fired when playback state changes (play/pause/resume).
        /// Used by UI controls (like top panel) to update their state.
        /// </summary>
        public event Action OnPlaybackStateChanged;

        /// <summary>
        /// When true, suppresses the default loop/restart behavior in OnMediaEnded.
        /// Set by external handlers (like batch download) that want to take over playback.
        /// </summary>
        public bool SuppressAutoLoop { get; set; }

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
        private string _previousSongPath;
        private Game _currentGame;

        // Multi-source pause tracking (replaces simple bool _isPaused)
        private readonly HashSet<PauseSource> _activePauseSources = new HashSet<PauseSource>();
        private bool _isPaused => _activePauseSources.Count > 0;

        public bool IsPlaying => _musicPlayer?.IsActive ?? false;
        public bool IsPaused => _isPaused;
        public bool IsLoaded => _musicPlayer?.IsLoaded ?? false;

        public MusicPlaybackService(IMusicPlayer musicPlayer, GameMusicFileService fileService, FileLogger fileLogger = null, ErrorHandlerService errorHandler = null)
        {
            _musicPlayer = musicPlayer ?? throw new ArgumentNullException(nameof(musicPlayer));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _fileLogger = fileLogger;
            _errorHandler = errorHandler;

            _fader = new MusicFader(
                _musicPlayer,
                () => _targetVolume,
                () => _fadeInDuration,
                () => _fadeOutDuration,
                _errorHandler
            );

            _musicPlayer.MediaEnded += OnMediaEnded;

            _previewTimer = new System.Windows.Threading.DispatcherTimer();
            _previewTimer.Interval = TimeSpan.FromSeconds(1);
            _previewTimer.Tick += OnPreviewTimerTick;
        }

        #region Multi-Source Pause Management

        /// <summary>
        /// Adds a pause source and pauses playback if this is the first pause source.
        /// Multiple sources can pause independently without conflicts.
        /// Uses fader for smooth fade-out before pausing.
        /// </summary>
        /// <param name="source">The source requesting pause</param>
        public void AddPauseSource(PauseSource source)
        {
            bool wasPlaying = !_isPaused;
            _activePauseSources.Add(source);

            if (wasPlaying && _isPaused)
            {
                // First pause source added - fade out then pause
                _fileLogger?.Info($"Pause added: {source} (total sources: {_activePauseSources.Count}) - fading out");
                _fader?.Pause();
                OnPlaybackStateChanged?.Invoke();
            }
            else if (_activePauseSources.Contains(source))
            {
                _fileLogger?.Debug($"Pause source already active: {source}");
            }
        }

        /// <summary>
        /// Removes a pause source and resumes playback if all pause sources are cleared.
        /// Only resumes when no other pause sources remain active.
        /// </summary>
        /// <param name="source">The source releasing pause</param>
        public void RemovePauseSource(PauseSource source)
        {
            bool wasPaused = _isPaused;
            _activePauseSources.Remove(source);

            if (wasPaused && !_isPaused && _musicPlayer?.IsLoaded == true)
            {
                // Last pause source removed - resume playback
                _fileLogger?.Info($"Pause removed: {source} - resuming playback (no pause sources remaining)");
                _fader.Resume();
                OnPlaybackStateChanged?.Invoke();
            }
            else if (wasPaused)
            {
                _fileLogger?.Debug($"Pause removed: {source} (still paused by {_activePauseSources.Count} sources)");
            }
        }

        /// <summary>
        /// Clears all pause sources and resumes playback.
        /// Use cautiously - only when you're certain all pause reasons are resolved.
        /// </summary>
        public void ClearAllPauseSources()
        {
            if (_activePauseSources.Count > 0)
            {
                _fileLogger?.Info($"Clearing all pause sources ({_activePauseSources.Count} active)");
                _activePauseSources.Clear();

                if (_musicPlayer?.IsLoaded == true)
                {
                    _fader.Resume();
                }
                OnPlaybackStateChanged?.Invoke();
            }
        }

        #endregion

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
                var nativeMusicPath = Common.PlayniteThemeHelper.FindBackgroundMusicFile(null);
                return !string.IsNullOrWhiteSpace(nativeMusicPath) &&
                       string.Equals(path, nativeMusicPath, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return !string.IsNullOrWhiteSpace(settings.DefaultMusicPath) &&
                       string.Equals(path, settings.DefaultMusicPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Pauses default music when switching to game music (PNS-style pattern).
        /// Saves position BEFORE pausing so it can be restored later.
        /// </summary>
        /// <remarks>
        /// CRITICAL: Position must be saved before pausing/closing.
        /// This is only called when we want to pause (not when switching to game music).
        /// </remarks>
        private void PauseDefaultMusic(UniPlaySongSettings settings)
        {
            if (_isPlayingDefaultMusic &&
                IsDefaultMusicPath(_currentSongPath, settings) &&
                _musicPlayer?.IsLoaded == true)
            {
                _defaultMusicPausedOnTime = _musicPlayer.CurrentTime ?? default;
                _fileLogger?.Info($"Pausing default music at position {_defaultMusicPausedOnTime.TotalSeconds:F2}s: {Path.GetFileName(_currentSongPath)}");
                _isPlayingDefaultMusic = false;
                AddPauseSource(PauseSource.DefaultMusicPreservation);
            }
        }

        /// <summary>
        /// Resumes default music when switching back from game music (PNS-style pattern).
        /// Resumes from saved position if same file is paused, otherwise loads and plays from saved position.
        /// </summary>
        /// <remarks>
        /// Note: defaultMusicPath can be either native or custom, so we check by path equality.
        /// </remarks>
        private void ResumeDefaultMusic(string defaultMusicPath, UniPlaySongSettings settings)
        {
            if (!_isPlayingDefaultMusic &&
                string.Equals(_lastDefaultMusicPath, defaultMusicPath, StringComparison.OrdinalIgnoreCase) &&
                _isPaused &&
                _musicPlayer?.IsLoaded == true &&
                string.Equals(_musicPlayer.Source, defaultMusicPath, StringComparison.OrdinalIgnoreCase))
            {
                _fileLogger?.Info($"Resuming paused default music from position {_defaultMusicPausedOnTime.TotalSeconds:F2}s: {Path.GetFileName(defaultMusicPath)}");
                RemovePauseSource(PauseSource.DefaultMusicPreservation);
                _isPlayingDefaultMusic = true;
                OnMusicStarted?.Invoke(settings);
            }
            else
            {
                _fileLogger?.Info($"Loading and playing default music from position {_defaultMusicPausedOnTime.TotalSeconds:F2}s: {Path.GetFileName(defaultMusicPath)}");
                _lastDefaultMusicPath = defaultMusicPath;
                _isPlayingDefaultMusic = true;

                if (_musicPlayer?.IsActive == true || _musicPlayer?.IsLoaded == true)
                {
                    _musicPlayer.Stop();
                }

                _musicPlayer.Load(defaultMusicPath);
                _musicPlayer.Volume = 0;
                _musicPlayer.Play(_defaultMusicPausedOnTime);
                _fader.FadeIn();
                RemovePauseSource(PauseSource.DefaultMusicPreservation);
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

            if (settings != null)
            {
                _fadeInDuration = settings.FadeInDuration;
                _fadeOutDuration = settings.FadeOutDuration;
                _targetVolume = settings.MusicVolume / Constants.VolumeDivisor;
                _currentSettings = settings;

                // Note: We don't check EnableMusic here for default music fallback.
                // Default music should work even if EnableMusic is false (it's a fallback).
                // But we still respect it for regular game music.
            }

            try
            {
                var gameId = game.Id.ToString();
                var songs = _fileService.GetAvailableSongs(game);
                
                // PNS PATTERN: Add default music to songs list if no game music found.
                // UseNativeMusicAsDefault and DefaultMusicPath are mutually exclusive at playback time.
                // DefaultMusicPath is NEVER modified - it remains separate and untouched.
                if (songs.Count == 0 && settings?.EnableDefaultMusic == true)
                {
                    if (settings.UseNativeMusicAsDefault == true)
                    {
                        // Using native music as default - get path directly from Playnite theme.
                        // DO NOT use DefaultMusicPath - it remains untouched for custom music.
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
                    _fileLogger?.Info($"No game music or default music available, stopping playback");
                    _currentGameId = gameId;
                    _currentGame = null;
                    FadeOutAndStop();
                    OnMusicStopped?.Invoke(settings);
                    return;
                }
                
                // Check if we have default music in the songs list
                bool hasDefaultMusic = settings?.EnableDefaultMusic == true && 
                    (songs.Any(s => IsDefaultMusicPath(s, settings)));
                
                // EnableMusic check: Only apply to game music, not default music (default music is a fallback)
                // Default music should work even if EnableMusic is false
                if (settings != null && !settings.EnableMusic && !hasDefaultMusic)
                {
                    _fileLogger?.Info($"EnableMusic is false and no default music - stopping playback");
                    _currentGameId = gameId;
                    _currentGame = null;
                    FadeOutAndStop();
                    OnMusicStopped?.Invoke(settings);
                    return;
                }
                
                if (settings != null && !settings.EnableMusic && hasDefaultMusic)
                {
                    _fileLogger?.Info($"EnableMusic is false but default music is available - allowing default music to play");
                }
                
                // Calculate isNewGame BEFORE updating _currentGameId (critical - compare with PREVIOUS game ID)
                var previousGameId = _currentGameId;
                var isNewGame = previousGameId == null || previousGameId != gameId;
                
                _currentGameId = gameId;
                _currentGame = game;
                
                // If switching to new game, clear current song path to force new song selection.
                // EXCEPTION: Don't clear if current song is default music - preserve it when switching between games with no music.
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
                
                // Update settings for game music (important when switching from default music to game music)
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

                // PNS PATTERN: Check if this is default music (native or custom).
                // UseNativeMusicAsDefault and DefaultMusicPath are mutually exclusive at playback time.
                bool isDefaultMusic = IsDefaultMusicPath(songToPlay, settings);
                
                if (isDefaultMusic)
                {
                    _fileLogger?.Info($"Playing default music (single-player): {Path.GetFileName(songToPlay)}");
                    _fileLogger?.Info($"  CurrentSongPath: {_currentSongPath ?? "null"}, IsPlayingDefaultMusic: {_isPlayingDefaultMusic}, IsLoaded: {_musicPlayer?.IsLoaded}, IsActive: {IsPlaying}, Source: {_musicPlayer?.Source ?? "null"}");
                    
                    // Check if default music is already playing - if so, continue (preserve position).
                    // Handles switching between games with no music (both use default music).
                    // Use case-insensitive path comparison to handle path variations (native music file paths).
                    if (string.Equals(_currentSongPath, songToPlay, StringComparison.OrdinalIgnoreCase) && 
                        _isPlayingDefaultMusic && 
                        _musicPlayer?.IsLoaded == true && 
                        string.Equals(_musicPlayer.Source, songToPlay, StringComparison.OrdinalIgnoreCase) &&
                        IsPlaying)
                    {
                        _fileLogger?.Info($"Default music already playing - continuing playback (position preserved): {Path.GetFileName(songToPlay)}");
                        return;
                    }
                    
                    // Check if default music is paused (from switching to game music) - resume from saved position.
                    // Use case-insensitive path comparison to handle path variations (native music file paths).
                    if (string.Equals(_currentSongPath, songToPlay, StringComparison.OrdinalIgnoreCase) && 
                        _isPaused && 
                        string.Equals(_lastDefaultMusicPath, songToPlay, StringComparison.OrdinalIgnoreCase) &&
                        _musicPlayer?.IsLoaded == true &&
                        string.Equals(_musicPlayer.Source, songToPlay, StringComparison.OrdinalIgnoreCase))
                    {
                        _fileLogger?.Info($"Default music is paused - resuming from saved position: {_defaultMusicPausedOnTime.TotalSeconds:F2}s");
                        RemovePauseSource(PauseSource.DefaultMusicPreservation);
                        _isPlayingDefaultMusic = true;
                        return;
                    }
                    
                    // Switching from game music to default music - use Switch() for proper fade-out
                    if (_musicPlayer?.IsActive == true || _musicPlayer?.IsLoaded == true)
                    {
                        // Only fade out if it's not already the default music
                        // Use case-insensitive path comparison to handle path variations (native music file paths)
                        if (!string.Equals(_currentSongPath, songToPlay, StringComparison.OrdinalIgnoreCase))
                        {
                            _fileLogger?.Info($"Switching from game music to default music with fade-out: {Path.GetFileName(songToPlay)}");
                            
                            _fader.Switch(
                                stopAction: () =>
                                {
                                    StopPreviewTimer();
                                    _musicPlayer.Close();
                                    _currentSongPath = null;
                                    ClearAllPauseSources();
                                },
                                preloadAction: () =>
                                {
                                    _musicPlayer.PreLoad(songToPlay);
                                },
                                playAction: () =>
                                {
                                    _musicPlayer.Load(songToPlay);
                                    _musicPlayer.Volume = 0;
                                    _musicPlayer.Play(_defaultMusicPausedOnTime);
                                    _currentSongPath = songToPlay;
                                    ClearAllPauseSources();
                                    _isPlayingDefaultMusic = true;
                                    _lastDefaultMusicPath = songToPlay;
                                    _songStartTime = DateTime.MinValue;
                                    _fileLogger?.Info($"Playing default music (switched from game music) at position {_defaultMusicPausedOnTime.TotalSeconds:F2}s");
                                    OnMusicStarted?.Invoke(settings);
                                }
                            );
                            return;
                        }
                    }
                    
                    ResumeDefaultMusic(songToPlay, settings);
                    _currentSongPath = songToPlay;
                    return;
                }

                // Skip if same song already playing for same game (but always switch if it's a new game).
                // Use case-insensitive path comparison to handle path variations (native music file paths).
                if (!forceReload && !isNewGame && string.Equals(_currentSongPath, songToPlay, StringComparison.OrdinalIgnoreCase) && IsPlaying)
                {
                    _fileLogger?.Info($"Same song playing for same game, skipping: {Path.GetFileName(songToPlay)}");
                    return;
                }

                // PNS PATTERN: If switching from default music to game music, save position BEFORE fading out.
                bool wasDefaultMusic = _isPlayingDefaultMusic && IsDefaultMusicPath(_currentSongPath, settings);
                
                if (isNewGame)
                {
                    _fileLogger?.Info($"New game detected (previous: {previousGameId ?? "null"}, current: {gameId}), switching to: {Path.GetFileName(songToPlay)}");
                }

                var newSongPath = songToPlay;
                
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
                    
                    _fileLogger?.Info($"Switching to: {Path.GetFileName(newSongPath)}");
                    
                    _fader.Switch(
                        stopAction: () =>
                        {
                            StopPreviewTimer();
                            _musicPlayer.Close();
                            _currentSongPath = null;
                            ClearAllPauseSources();
                        },
                        preloadAction: () =>
                        {
                            _musicPlayer.PreLoad(newSongPath);
                        },
                        playAction: () =>
                        {
                            // PNS pattern: Fader sets volume to 0 before calling this - don't set volume (fader controls it)
                            _musicPlayer.Load(newSongPath);
                            _musicPlayer.Play();
                            _currentSongPath = newSongPath;
                            ClearAllPauseSources();
                            MarkSongStart();
                            _fileLogger?.Info($"Playing (switched): {Path.GetFileName(newSongPath)}");
                            OnMusicStarted?.Invoke(settings);
                        }
                    );
                }
                else if (_isPaused && wasDefaultMusic)
                {
                    _fileLogger?.Info($"Switching from paused default music to game music: {Path.GetFileName(newSongPath)}");
                    
                    _musicPlayer?.Close();
                    _currentSongPath = null;
                    ClearAllPauseSources();
                    
                    _musicPlayer.Load(newSongPath);
                    _musicPlayer.Volume = 0;
                    _musicPlayer.Play();
                    _currentSongPath = newSongPath;
                    MarkSongStart();
                    _fader.FadeIn();
                    _fileLogger?.Info($"Playing game music (from paused default): {Path.GetFileName(newSongPath)}");
                    OnMusicStarted?.Invoke(settings);
                }
                else
                {
                    _fileLogger?.Info($"Initial playback with fade-in: {Path.GetFileName(newSongPath)}");
                    
                    _currentSongPath = null;
                    
                    _musicPlayer.Load(newSongPath);
                    _musicPlayer.Volume = 0;
                    _musicPlayer.Play();
                    _currentSongPath = newSongPath;
                    ClearAllPauseSources();
                    MarkSongStart();

                    _fader.FadeIn();

                    _fileLogger?.Info($"Playing (initial with fade-in): {Path.GetFileName(newSongPath)}, starting at volume 0");
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
                _currentGame = null;
                ClearAllPauseSources();

                _isPlayingDefaultMusic = false;
                _lastDefaultMusicPath = null;
                _defaultMusicPausedOnTime = default;

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
            bool playerActive = (_musicPlayer?.IsLoaded ?? false) || (_musicPlayer?.IsActive ?? false);

            if (playerActive)
            {
                StopPreviewTimer();
                _fader.FadeOutAndStop(() =>
                {
                    _musicPlayer?.Close();
                    _currentSongPath = null;
                    _currentGameId = null;
                    _currentGame = null;
                    ClearAllPauseSources();

                    _isPlayingDefaultMusic = false;
                    _lastDefaultMusicPath = null;
                    _defaultMusicPausedOnTime = default;

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
            if (_musicPlayer?.IsLoaded == true)
            {
                StopPreviewTimer();
                _fileLogger?.Debug("Pause: Calling fader.Pause()");
                _fader?.Pause();
                _activePauseSources.Add(PauseSource.Manual);
            }
        }

        public void Resume()
        {
            if (_musicPlayer?.IsLoaded == true)
            {
                _fileLogger?.Debug("Resume: Calling fader.Resume()");
                _fader?.Resume();
                _activePauseSources.Remove(PauseSource.Manual);
            }
        }

        public List<string> GetAvailableSongs(Game game)
        {
            return _fileService?.GetAvailableSongs(game) ?? new List<string>();
        }

        public void SetVolume(double volume)
        {
            _targetVolume = Math.Max(0.0, Math.Min(1.0, volume));
            
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
            LoadAndPlayFileFrom(filePath, TimeSpan.Zero);
        }

        public void LoadAndPlayFileFrom(string filePath, TimeSpan startFrom)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                _fileLogger?.Warn($"Cannot load file: {filePath ?? "null"}");
                return;
            }

            try
            {
                _fileLogger?.Info($"LoadAndPlayFileFrom: {filePath} at {startFrom:mm\\:ss\\.fff}");
                _musicPlayer.Load(filePath);
                _musicPlayer.Volume = 0;
                _musicPlayer.Play(startFrom);
                _fader.FadeIn();
                ClearAllPauseSources();
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

        public void PlayPreview(string filePath, double volume)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Logger.Warn($"[PlayPreview] Cannot load file: {filePath ?? "null"}");
                return;
            }

            try
            {
                // Clamp volume to valid range
                volume = Math.Max(0.0, Math.Min(1.0, volume));

                Logger.Info($"[PlayPreview] START: {Path.GetFileName(filePath)} at volume {volume:F2}");

                // CRITICAL: Cancel any ongoing fade operation first
                // This prevents the fader from overriding our volume setting
                _fader.CancelFade();
                Logger.Info($"[PlayPreview] Fader cancelled");

                // Stop current playback completely (no fading)
                _musicPlayer.Stop();
                _musicPlayer.Close();
                Logger.Info($"[PlayPreview] Player stopped and closed");

                // Load the file
                _musicPlayer.Load(filePath);
                Logger.Info($"[PlayPreview] File loaded");

                // Set volume directly on the player (same SDL2 backend, same volume scale)
                _musicPlayer.Volume = volume;
                Logger.Info($"[PlayPreview] Volume set to {volume:F2}, player reports {_musicPlayer.Volume:F2}");

                // Play immediately (no fading)
                _musicPlayer.Play();
                Logger.Info($"[PlayPreview] Play called, volume now {_musicPlayer.Volume:F2}");

                // Update internal state to match what we just set
                _targetVolume = volume;
                ClearAllPauseSources();

                Logger.Info($"[PlayPreview] COMPLETE: targetVolume={_targetVolume:F2}");
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "playing preview",
                    showUserMessage: false
                );
            }
        }

        /// <summary>
        /// Selects which song to play for the given game.
        /// Handles primary song selection, randomization, and default music continuation.
        /// </summary>
        /// <param name="game">The game to select a song for.</param>
        /// <param name="songs">List of available songs for the game.</param>
        /// <param name="isNewGame">Whether this is a new game selection.</param>
        /// <returns>The path to the song to play, or null if no song available.</returns>
        private string SelectSongToPlay(Game game, List<string> songs, bool isNewGame)
        {
            if (songs.Count == 0) return null;

            // PNS PATTERN: Check if current song is still valid (same directory or same default music).
            // Allows default music to continue playing when switching between games with no music.
            // Also allows resuming paused default music (check happens in caller).
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
                        _previousSongPath = primarySong;
                        return primarySong;
                    }
                }
            }

            // PNS PATTERN: Randomization logic (similar to PlayniteSound)
            if (songs.Count > 1 && _currentSettings != null)
            {
                bool shouldRandomize = false;

                if (isNewGame && _currentSettings.RandomizeOnEverySelect)
                {
                    shouldRandomize = true;
                }

                if (shouldRandomize)
                {
                    // Select random song, avoiding immediate repeat (max 10 attempts to prevent infinite loops)
                    var random = new Random();
                    string selected;
                    int attempts = 0;
                    do
                    {
                        selected = songs[random.Next(songs.Count)];
                        attempts++;
                    }
                    while (selected == _previousSongPath && songs.Count > 1 && attempts < 10);

                    _previousSongPath = selected;
                    _fileLogger?.Info($"Randomized song selection: {Path.GetFileName(selected)} (RandomizeOnEverySelect)");
                    return selected;
                }
            }

            var firstSong = songs.FirstOrDefault();
            _previousSongPath = firstSong;
            return firstSong;
        }

        /// <summary>
        /// Marks the start time for preview mode tracking.
        /// Starts preview timer if preview mode is enabled for game music.
        /// </summary>
        private void MarkSongStart()
        {
            _songStartTime = DateTime.Now;

            if (_currentSettings?.EnablePreviewMode == true &&
                !_isPlayingDefaultMusic &&
                !IsDefaultMusicPath(_currentSongPath, _currentSettings))
            {
                _previewTimer.Start();
                _fileLogger?.Info($"Preview timer started for: {Path.GetFileName(_currentSongPath)} (duration: {_currentSettings.PreviewDuration}s)");
            }
        }

        /// <summary>
        /// Stops the preview timer.
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
        /// Timer tick handler for preview mode.
        /// Checks if preview duration has been reached and handles song transition.
        /// If RandomizeOnMusicEnd is enabled, switches to a different random song.
        /// Otherwise, restarts the same song from the beginning.
        /// </summary>
        private void OnPreviewTimerTick(object sender, EventArgs e)
        {
            try
            {
                if (ShouldRestartForPreview() && _musicPlayer?.IsActive == true)
                {
                    _previewTimer.Stop();

                    // Check if we should randomize to a different song
                    string nextSong = _currentSongPath;
                    bool shouldRandomize = _currentSettings?.RandomizeOnMusicEnd == true && _currentGame != null;

                    if (shouldRandomize)
                    {
                        var songs = _fileService.GetAvailableSongs(_currentGame);
                        if (songs.Count > 1)
                        {
                            // Select random song (avoiding immediate repeat, max 10 attempts)
                            var random = new Random();
                            int attempts = 0;
                            do
                            {
                                nextSong = songs[random.Next(songs.Count)];
                                attempts++;
                            }
                            while (nextSong == _currentSongPath && songs.Count > 1 && attempts < 10);

                            _fileLogger?.Info($"Preview duration reached ({_currentSettings.PreviewDuration}s), randomizing to: {Path.GetFileName(nextSong)}");
                        }
                        else
                        {
                            _fileLogger?.Info($"Preview duration reached ({_currentSettings.PreviewDuration}s), only one song available, restarting: {Path.GetFileName(nextSong)}");
                        }
                    }
                    else
                    {
                        _fileLogger?.Info($"Preview duration reached ({_currentSettings.PreviewDuration}s), restarting with fade: {Path.GetFileName(nextSong)}");
                    }

                    var songToPlay = nextSong;

                    // Use fader to create smooth fade-out/fade-in transition
                    _fader.Switch(
                        stopAction: () =>
                        {
                            _musicPlayer.Stop();
                        },
                        preloadAction: () =>
                        {
                            _musicPlayer.PreLoad(songToPlay);
                        },
                        playAction: () =>
                        {
                            _previousSongPath = _currentSongPath;
                            _currentSongPath = songToPlay;

                            _musicPlayer.Load(songToPlay);
                            _musicPlayer.Play(TimeSpan.Zero);

                            MarkSongStart();

                            _fileLogger?.Info($"Preview mode: Now playing with fade: {Path.GetFileName(songToPlay)}");
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

        /// <summary>
        /// Handles media ended events from the music player.
        /// Handles randomization on song end, preview mode restart, and normal looping.
        /// Note: Threading is handled at the player level (NAudioMusicPlayer/SDL2MusicPlayer).
        /// </summary>
        private void OnMediaEnded(object sender, EventArgs e)
        {
            if (sender == _musicPlayer && !string.IsNullOrWhiteSpace(_currentSongPath) && File.Exists(_currentSongPath))
            {
                try
                {
                    // Fire OnSongEnded event before handling looping/randomization
                    OnSongEnded?.Invoke();

                    // If SuppressAutoLoop is set, an external handler manages playback
                    if (SuppressAutoLoop)
                    {
                        return;
                    }

                    // PNS PATTERN: Check for randomization on song end (similar to PlayniteSound)
                    if (_currentSettings?.RandomizeOnMusicEnd == true && _currentGame != null)
                    {
                        // Only randomize for game music (not default music)
                        bool isDefaultMusic = IsDefaultMusicPath(_currentSongPath, _currentSettings);
                        if (!isDefaultMusic)
                        {
                            var songs = _fileService.GetAvailableSongs(_currentGame);
                            if (songs.Count > 1)
                            {
                                // Select random song (avoiding immediate repeat, max 10 attempts)
                                var random = new Random();
                                string nextSong;
                                int attempts = 0;
                                do
                                {
                                    nextSong = songs[random.Next(songs.Count)];
                                    attempts++;
                                }
                                while (nextSong == _currentSongPath && songs.Count > 1 && attempts < 10);

                                _previousSongPath = _currentSongPath;
                                _currentSongPath = nextSong;
                                LoadAndPlayFile(nextSong);
                                _fileLogger?.Info($"Randomized to next song on end: {Path.GetFileName(nextSong)} (RandomizeOnMusicEnd)");
                                return;
                            }
                        }
                    }

                    if (ShouldRestartForPreview())
                    {
                        // Preview mode: Song ended after preview duration - restart from beginning
                        // No fade needed since song already ended naturally
                        _musicPlayer.Play(TimeSpan.Zero);
                        MarkSongStart();
                        _fileLogger?.Info($"Preview mode: Song ended, restarting from beginning: {Path.GetFileName(_currentSongPath)}");
                    }
                    else
                    {
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
