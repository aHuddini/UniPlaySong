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
    // High-level service for managing music playback for games
    public class MusicPlaybackService : IMusicPlaybackService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // Static Random instance to avoid allocations and duplicate sequences
        private static readonly Random _random = new Random();

        private readonly IMusicPlayer _musicPlayer;
        private readonly GameMusicFileService _fileService;
        private readonly FileLogger _fileLogger;
        private readonly ErrorHandlerService _errorHandler;
        private readonly Dictionary<string, bool> _primarySongPlayed = new Dictionary<string, bool>();

        // Cached native music path to avoid repeated method calls in IsDefaultMusicPath()
        private readonly string _nativeMusicPath;
        
        public event Action<UniPlaySongSettings> OnMusicStopped; // Fired when music stops (for native music restoration)

        public event Action<UniPlaySongSettings> OnMusicStarted; // Fired when music starts (for native music suppression)

        // Fired when a song reaches its natural end (before looping/randomizing).
        // Used by batch download to queue next random game's music.
        public event Action OnSongEnded;

        // Fired when playback state changes (play/pause/resume). Used by UI controls to update state.
        public event Action OnPlaybackStateChanged;

        // Fired when current game or song count changes. Used by UI for skip button visibility.
        public event Action OnSongCountChanged;

        public event Action<string> OnSongChanged; // Fired when current song changes (for UI song info display)

        // When true, suppresses loop/restart in OnMediaEnded. Set by batch download to take over playback.
        public bool SuppressAutoLoop { get; set; }

        private int _currentGameSongCount = 0;
        public int CurrentGameSongCount => _currentGameSongCount;

        // PNS-style state tracking for default music (single-player approach)
        private bool _isPlayingDefaultMusic = false;
        private string _lastDefaultMusicPath = null;
        private TimeSpan _defaultMusicPausedOnTime = default;

        // Initialization gate to prevent music playback until OnApplicationStarted completes
        // This ensures window state is checked before any music can play
        private bool _initializationComplete = false;

        // Deferred playback request - stores the game/settings to play after initialization
        private Game _deferredGame = null;
        private UniPlaySongSettings _deferredSettings = null;
        private bool _deferredForceReload = false;

        // Preview mode state tracking
        private DateTime _songStartTime = DateTime.MinValue;
        private UniPlaySongSettings _currentSettings;
        private System.Windows.Threading.DispatcherTimer _previewTimer;

        private MusicFader _fader;
        private double _targetVolume = Constants.DefaultTargetVolume;
        private double _volumeMultiplier = 1.0;
        private double _idleVolumeMultiplier = 1.0;
        private double _fadeInDuration = Constants.DefaultFadeInDuration;
        private double _fadeOutDuration = Constants.DefaultFadeOutDuration;

        private string _currentGameId;
        private string _currentSongPathBacking;
        private string _previousSongPath;
        private Game _currentGame;

        // Cached result of IsDefaultMusicPath(_currentSongPath) - updated automatically when _currentSongPath changes
        private bool _isCurrentSongDefaultMusic = false;

        // Gets/sets current song path, fires OnSongChanged when changed
        private string _currentSongPath
        {
            get => _currentSongPathBacking;
            set
            {
                if (_currentSongPathBacking != value)
                {
                    _currentSongPathBacking = value;

                    // Update cached default music check whenever path changes
                    _isCurrentSongDefaultMusic = IsDefaultMusicPath(value, _currentSettings);

                    if (!string.IsNullOrEmpty(value))
                    {
                        OnSongChanged?.Invoke(value);
                    }
                }
            }
        }

        public string CurrentSongPath => _currentSongPath;

        public Game CurrentGame => _currentGame;

        // Multi-source pause tracking (replaces simple bool _isPaused)
        private readonly HashSet<PauseSource> _activePauseSources = new HashSet<PauseSource>();
        private bool _isPaused => _activePauseSources.Count > 0;

        public bool IsPlaying => _musicPlayer?.IsActive ?? false;
        public bool IsPaused => _isPaused;
        public bool IsLoaded => _musicPlayer?.IsLoaded ?? false;
        public TimeSpan? CurrentTime => _musicPlayer?.CurrentTime;
        public bool IsPlayingDefaultMusic => _isPlayingDefaultMusic;
        public bool IsPlayingBundledPreset => _isPlayingDefaultMusic &&
            _currentSettings?.DefaultMusicSourceOption == DefaultMusicSource.BundledPreset;

        public MusicPlaybackService(IMusicPlayer musicPlayer, GameMusicFileService fileService, FileLogger fileLogger = null, ErrorHandlerService errorHandler = null)
        {
            _musicPlayer = musicPlayer ?? throw new ArgumentNullException(nameof(musicPlayer));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _fileLogger = fileLogger;
            _errorHandler = errorHandler;

            // Cache native music path once at service initialization
            _nativeMusicPath = Common.PlayniteThemeHelper.FindBackgroundMusicFile(null);

            _fader = new MusicFader(
                _musicPlayer,
                () => _targetVolume * _volumeMultiplier * _idleVolumeMultiplier,
                () => _fadeInDuration,
                () => _fadeOutDuration,
                _errorHandler,
                _fileLogger
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
                _fileLogger?.Debug($"Pause: {source} (fading out)");
                _fader?.Pause();
                OnPlaybackStateChanged?.Invoke();
            }
        }

        // Atomically replaces one pause source with another without triggering resume/pause.
        // Used for FocusLoss → Manual conversion to avoid audible blip.
        public void ConvertPauseSource(PauseSource from, PauseSource to)
        {
            if (_activePauseSources.Contains(from))
            {
                _activePauseSources.Remove(from);
                _activePauseSources.Add(to);
                _fileLogger?.Debug($"Convert: {from} → {to}");
            }
        }

        /// <summary>
        /// Removes a pause source and resumes playback if all pause sources are cleared.
        /// Only resumes when no other pause sources remain active.
        /// If music isn't loaded but we have a deferred game, triggers deferred playback.
        /// </summary>
        /// <param name="source">The source releasing pause</param>
        public void RemovePauseSource(PauseSource source)
        {
            bool wasPaused = _isPaused;
            _activePauseSources.Remove(source);

            if (wasPaused && !_isPaused)
            {
                if (_musicPlayer?.IsLoaded == true && _musicPlayer.IsActive)
                {
                    // Player is loaded and actively playing (was paused mid-playback) — resume via fader
                    _fileLogger?.Debug($"Resume: {source} removed (no pause sources remaining)");
                    _fader.Resume();
                    OnPlaybackStateChanged?.Invoke();
                }
                else if (_musicPlayer?.IsLoaded == true && !_musicPlayer.IsActive)
                {
                    // Player is loaded but not playing (was loaded during manual pause) — start playback
                    _fileLogger?.Debug($"Resume: {source} removed, starting loaded song with fade-in");
                    _musicPlayer.Volume = 0;
                    _musicPlayer.Play();
                    MarkSongStart();
                    _fader.FadeIn();
                    OnPlaybackStateChanged?.Invoke();
                }
                else if (_deferredGame != null && _initializationComplete)
                {
                    // Music not loaded but we have a deferred game - trigger playback now
                    var game = _deferredGame;
                    var settings = _deferredSettings;
                    var forceReload = _deferredForceReload;

                    _deferredGame = null;
                    _deferredSettings = null;
                    _deferredForceReload = false;

                    _fileLogger?.Debug($"Triggering deferred playback for {game.Name} (pause sources cleared)");
                    PlayGameMusic(game, settings, forceReload);
                }
            }
        }

        // Adds a pause source and pauses playback instantly (no fade).
        // Cancels any in-progress fade to avoid state conflicts.
        public void AddPauseSourceImmediate(PauseSource source)
        {
            bool wasPlaying = !_isPaused;
            _activePauseSources.Add(source);

            if (wasPlaying && _isPaused)
            {
                _fileLogger?.Debug($"Pause (instant): {source}");
                _fader?.CancelFade();
                if (_musicPlayer?.IsLoaded == true && _musicPlayer.IsActive)
                    _musicPlayer.Pause();
                OnPlaybackStateChanged?.Invoke();
            }
        }

        // Removes a pause source and resumes playback instantly (no fade) if all sources cleared.
        public void RemovePauseSourceImmediate(PauseSource source)
        {
            bool wasPaused = _isPaused;
            _activePauseSources.Remove(source);

            if (wasPaused && !_isPaused)
            {
                if (_musicPlayer?.IsLoaded == true && _musicPlayer.IsActive)
                {
                    _fileLogger?.Debug($"Resume (instant): {source} removed (no pause sources remaining)");
                    _musicPlayer.Resume();
                    _musicPlayer.Volume = _targetVolume * _volumeMultiplier;
                    OnPlaybackStateChanged?.Invoke();
                }
                else if (_musicPlayer?.IsLoaded == true && !_musicPlayer.IsActive)
                {
                    _fileLogger?.Debug($"Resume (instant): {source} removed, starting loaded song");
                    _musicPlayer.Volume = _targetVolume * _volumeMultiplier;
                    _musicPlayer.Play();
                    MarkSongStart();
                    OnPlaybackStateChanged?.Invoke();
                }
                else if (_deferredGame != null && _initializationComplete)
                {
                    var game = _deferredGame;
                    var settings = _deferredSettings;
                    var forceReload = _deferredForceReload;
                    _deferredGame = null;
                    _deferredSettings = null;
                    _deferredForceReload = false;
                    _fileLogger?.Debug($"Triggering deferred playback for {game.Name} (pause sources cleared, instant)");
                    PlayGameMusic(game, settings, forceReload);
                }
            }
        }

        /// <summary>
        /// Clears all pause sources EXCEPT window-state sources (FocusLoss, Minimized, SystemTray).
        /// Window-state sources should only be cleared by the window event handlers.
        /// This ensures music stays paused if the window started minimized/in tray/unfocused.
        /// </summary>
        public void ClearAllPauseSources()
        {
            if (_activePauseSources.Count > 0)
            {
                // Preserve window-state and manual pause sources - they should only be cleared by their own handlers
                var preservedSources = new HashSet<PauseSource>();
                if (_activePauseSources.Contains(PauseSource.FocusLoss))
                    preservedSources.Add(PauseSource.FocusLoss);
                if (_activePauseSources.Contains(PauseSource.Minimized))
                    preservedSources.Add(PauseSource.Minimized);
                if (_activePauseSources.Contains(PauseSource.SystemTray))
                    preservedSources.Add(PauseSource.SystemTray);
                if (_activePauseSources.Contains(PauseSource.Manual))
                    preservedSources.Add(PauseSource.Manual);
                if (_activePauseSources.Contains(PauseSource.ExternalAudio))
                    preservedSources.Add(PauseSource.ExternalAudio);
                if (_activePauseSources.Contains(PauseSource.Idle))
                    preservedSources.Add(PauseSource.Idle);

                var clearedCount = _activePauseSources.Count - preservedSources.Count;
                _activePauseSources.Clear();

                // Re-add preserved sources
                foreach (var source in preservedSources)
                {
                    _activePauseSources.Add(source);
                }

                if (clearedCount > 0)
                {
                    _fileLogger?.Debug($"Cleared {clearedCount} pause sources (preserved {preservedSources.Count}: {string.Join(", ", preservedSources)})");
                }

                // Only resume if no pause sources remain (including preserved ones)
                if (_activePauseSources.Count == 0 && _musicPlayer?.IsLoaded == true)
                {
                    _fader.Resume();
                }
                OnPlaybackStateChanged?.Invoke();
            }
        }

        // Checks if any window-state pause sources are active (FocusLoss, Minimized, SystemTray)
        private bool HasWindowStatePauseSources()
        {
            return _activePauseSources.Contains(PauseSource.FocusLoss) ||
                   _activePauseSources.Contains(PauseSource.Minimized) ||
                   _activePauseSources.Contains(PauseSource.SystemTray);
        }

        // Called after CheckInitialWindowState. Processes any deferred playback request.
        public void MarkInitializationComplete()
        {
            _initializationComplete = true;

            // Process deferred playback if we have one
            if (_deferredGame != null)
            {
                var game = _deferredGame;
                var settings = _deferredSettings;
                var forceReload = _deferredForceReload;

                // Clear deferred state
                _deferredGame = null;
                _deferredSettings = null;
                _deferredForceReload = false;

                // Now attempt to play - this will check pause sources
                _fileLogger?.Debug($"Processing deferred playback for {game.Name}");
                PlayGameMusic(game, settings, forceReload);
            }
        }

        #endregion

        // Checks if path is default music (native or custom). The two options are mutually exclusive.
        private bool IsDefaultMusicPath(string path, UniPlaySongSettings settings)
        {
            if (string.IsNullOrWhiteSpace(path) || settings == null || !settings.EnableDefaultMusic)
            {
                return false;
            }

            switch (settings.DefaultMusicSourceOption)
            {
                case DefaultMusicSource.NativeTheme:
                    return !string.IsNullOrWhiteSpace(_nativeMusicPath) &&
                           string.Equals(path, _nativeMusicPath, StringComparison.OrdinalIgnoreCase);

                case DefaultMusicSource.BundledPreset:
                    var presetPath = BundledPresetService.ResolvePresetPath(settings.SelectedBundledPreset);
                    return presetPath != null &&
                           string.Equals(path, presetPath, StringComparison.OrdinalIgnoreCase);

                case DefaultMusicSource.CustomFile:
                default:
                    return !string.IsNullOrWhiteSpace(settings.DefaultMusicPath) &&
                           string.Equals(path, settings.DefaultMusicPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Pauses default music when switching to game music (PNS-style pattern).
        // CRITICAL: Saves position BEFORE pausing so it can be restored later.
        private void PauseDefaultMusic(UniPlaySongSettings settings)
        {
            if (_isPlayingDefaultMusic &&
                _isCurrentSongDefaultMusic &&
                _musicPlayer?.IsLoaded == true)
            {
                _defaultMusicPausedOnTime = _musicPlayer.CurrentTime ?? default;
                _fileLogger?.Info($"Pausing default music at position {_defaultMusicPausedOnTime.TotalSeconds:F2}s: {Path.GetFileName(_currentSongPath)}");
                _isPlayingDefaultMusic = false;
                AddPauseSource(PauseSource.DefaultMusicPreservation);
            }
        }

        // Resumes default music from saved position when switching back from game music (PNS-style pattern).
        // defaultMusicPath can be either native or custom, checked by path equality.
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
                // Clear any deferred playback
                _deferredGame = null;
                _deferredSettings = null;
                FadeOutAndStop();
                // Reset song count when no game
                if (_currentGameSongCount != 0)
                {
                    _currentGameSongCount = 0;
                    OnSongCountChanged?.Invoke();
                }
                // Notify that we stopped music (for native music restoration)
                OnMusicStopped?.Invoke(settings);
                return;
            }

            // Wait for initialization to complete before playing music
            // This ensures CheckInitialWindowState has run and added any necessary pause sources
            if (!_initializationComplete)
            {
                _fileLogger?.Debug($"Deferring playback for {game.Name} (waiting for initialization)");
                _deferredGame = game;
                _deferredSettings = settings;
                _deferredForceReload = forceReload;
                return;
            }

            // Don't start playback if window-state pause sources are active
            // This prevents music from playing when Playnite is minimized/in tray/unfocused
            // Store game info so we can play when pause sources are cleared
            if (HasWindowStatePauseSources())
            {
                _fileLogger?.Debug($"Playback blocked for {game.Name} (window state pause sources active) - storing for later");
                _deferredGame = game;
                _deferredSettings = settings;
                _deferredForceReload = forceReload;
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

                // Skip game-specific music for uninstalled games — fall through to default music
                if (songs.Count > 0 && settings?.MusicOnlyForInstalledGames == true && game.IsInstalled != true)
                {
                    _fileLogger?.Info($"PlayGameMusic: {game.Name} is not installed, skipping game music (MusicOnlyForInstalledGames enabled)");
                    songs.Clear();
                }

                // Default music fallback when no game music found.
                // Three sources: BundledPreset, NativeTheme, or CustomFile (driven by DefaultMusicSourceOption).
                // UseNativeMusicAsDefault kept in sync for backward compatibility.
                if (songs.Count == 0 && settings?.EnableDefaultMusic == true)
                {
                    switch (settings.DefaultMusicSourceOption)
                    {
                        case DefaultMusicSource.NativeTheme:
                            var nativeMusicPath = Common.PlayniteThemeHelper.FindBackgroundMusicFile(null);
                            if (!string.IsNullOrWhiteSpace(nativeMusicPath) && File.Exists(nativeMusicPath))
                            {
                                _fileLogger?.Info($"No game music for {game.Name}, using native theme music: {Path.GetFileName(nativeMusicPath)}");
                                songs.Add(nativeMusicPath);
                            }
                            else
                            {
                                _fileLogger?.Warn($"NativeTheme selected but native music file not found.");
                            }
                            break;

                        case DefaultMusicSource.BundledPreset:
                            var presetPath = BundledPresetService.ResolvePresetPath(settings.SelectedBundledPreset);
                            if (presetPath != null)
                            {
                                _fileLogger?.Info($"No game music for {game.Name}, using bundled preset: {Path.GetFileName(presetPath)}");
                                songs.Add(presetPath);
                            }
                            else
                            {
                                _fileLogger?.Warn($"BundledPreset selected but preset file not found: {settings.SelectedBundledPreset}");
                            }
                            break;

                        case DefaultMusicSource.CustomFile:
                        default:
                            if (!string.IsNullOrWhiteSpace(settings.DefaultMusicPath) &&
                                File.Exists(settings.DefaultMusicPath))
                            {
                                _fileLogger?.Info($"No game music for {game.Name}, using custom default: {Path.GetFileName(settings.DefaultMusicPath)}");
                                songs.Add(settings.DefaultMusicPath);
                            }
                            break;
                    }
                }
                
                _fileLogger?.Debug($"PlayGameMusic: {game.Name} (ID: {gameId}) - Found {songs.Count} songs, CurrentGameId: {_currentGameId ?? "null"}, CurrentSongPath: {_currentSongPath ?? "null"}");

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

                // Update song count and notify UI for skip button visibility
                // Fire event if count changed, new game, or forceReload (after download)
                var newSongCount = songs.Count;
                var songCountChanged = _currentGameSongCount != newSongCount;
                _currentGameSongCount = newSongCount;
                if (songCountChanged || isNewGame || forceReload)
                {
                    _fileLogger?.Debug($"Song count updated: {newSongCount} songs (changed: {songCountChanged}, newGame: {isNewGame}, forceReload: {forceReload})");
                    OnSongCountChanged?.Invoke();
                }
                
                // If switching to new game, clear current song path to force new song selection.
                // EXCEPTION: Don't clear if current song is default music - preserve it when switching between games with no music.
                if (isNewGame && !_isCurrentSongDefaultMusic)
                {
                    _fileLogger?.Debug($"New game detected (previous: {previousGameId ?? "null"}, current: {gameId}), clearing current song path (was: {_currentSongPath ?? "null"})");
                    _currentSongPath = null;
                }
                else if (isNewGame && _isCurrentSongDefaultMusic)
                {
                    _fileLogger?.Debug($"New game detected but current song is default music - preserving to continue playback: {Path.GetFileName(_currentSongPath)}");
                }
                
                // Update settings for game music (important when switching from default music to game music)
                if (settings != null)
                {
                    _fadeInDuration = settings.FadeInDuration;
                    _fadeOutDuration = settings.FadeOutDuration;
                    _targetVolume = settings.MusicVolume / Constants.VolumeDivisor;
                    _fileLogger?.Debug($"PlayGameMusic: Updated settings (TargetVolume: {_targetVolume}, FadeInDuration: {_fadeInDuration}, FadeOutDuration: {_fadeOutDuration})");
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
                    _fileLogger?.Debug($"Playing default music: {Path.GetFileName(songToPlay)} (CurrentSongPath: {_currentSongPath ?? "null"}, IsPlayingDefaultMusic: {_isPlayingDefaultMusic}, IsLoaded: {_musicPlayer?.IsLoaded}, IsActive: {IsPlaying})");
                    
                    // Check if default music is already playing - if so, continue (preserve position).
                    // Handles switching between games with no music (both use default music).
                    // Use case-insensitive path comparison to handle path variations (native music file paths).
                    if (string.Equals(_currentSongPath, songToPlay, StringComparison.OrdinalIgnoreCase) && 
                        _isPlayingDefaultMusic && 
                        _musicPlayer?.IsLoaded == true && 
                        string.Equals(_musicPlayer.Source, songToPlay, StringComparison.OrdinalIgnoreCase) &&
                        IsPlaying)
                    {
                        _fileLogger?.Debug($"Default music already playing - continuing: {Path.GetFileName(songToPlay)}");
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
                        _fileLogger?.Debug($"Default music is paused - resuming from position: {_defaultMusicPausedOnTime.TotalSeconds:F2}s");
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
                            _fileLogger?.Debug($"Switching from game music to default music: {Path.GetFileName(songToPlay)}");
                            
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
                                    _currentSongPath = songToPlay;
                                    ClearAllPauseSources();
                                    _isPlayingDefaultMusic = true;
                                    _lastDefaultMusicPath = songToPlay;
                                    _songStartTime = DateTime.MinValue;

                                    if (_isPaused)
                                    {
                                        _fileLogger?.Debug($"Loaded default music (manual pause active): {Path.GetFileName(songToPlay)}");
                                        OnMusicStarted?.Invoke(settings);
                                        return;
                                    }

                                    _musicPlayer.Play(_defaultMusicPausedOnTime);
                                    _fileLogger?.Debug($"Playing default music at position {_defaultMusicPausedOnTime.TotalSeconds:F2}s");
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
                    _fileLogger?.Debug($"Same song playing for same game, skipping: {Path.GetFileName(songToPlay)}");
                    return;
                }

                // PNS PATTERN: If switching from default music to game music, save position BEFORE fading out.
                bool wasDefaultMusic = _isPlayingDefaultMusic && _isCurrentSongDefaultMusic;
                
                if (isNewGame)
                {
                    _fileLogger?.Debug($"New game detected (previous: {previousGameId ?? "null"}, current: {gameId}), switching to: {Path.GetFileName(songToPlay)}");
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
                        _fileLogger?.Debug($"Switching from default to game music. Saved position: {_defaultMusicPausedOnTime.TotalSeconds:F2}s");
                        _isPlayingDefaultMusic = false;
                    }

                    _fileLogger?.Debug($"Switching to: {Path.GetFileName(newSongPath)}");

                    // Fire song changed early so UI can update immediately (before fade completes)
                    OnSongChanged?.Invoke(newSongPath);

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
                            _musicPlayer.Load(newSongPath);
                            _musicPlayer.Volume = 0; // NAudio Load() creates new VolumeSampleProvider at 1.0 — must reset before Play()
                            _currentSongPath = newSongPath;
                            ClearAllPauseSources();

                            if (_isPaused)
                            {
                                // Manual pause is active — load song for metadata/Now Playing but don't play
                                _fileLogger?.Debug($"Loaded (manual pause active): {Path.GetFileName(newSongPath)}");
                                OnMusicStarted?.Invoke(settings);
                                return;
                            }

                            _musicPlayer.Play();
                            MarkSongStart();
                            _fileLogger?.Debug($"Playing (switched): {Path.GetFileName(newSongPath)}");
                            OnMusicStarted?.Invoke(settings);
                        }
                    );
                }
                else if (_isPaused && wasDefaultMusic)
                {
                    _fileLogger?.Debug($"Switching from paused default to game music: {Path.GetFileName(newSongPath)}");

                    _musicPlayer?.Close();
                    _currentSongPath = null;
                    ClearAllPauseSources();

                    _musicPlayer.Load(newSongPath);
                    _musicPlayer.Volume = 0;
                    _currentSongPath = newSongPath;

                    if (_isPaused)
                    {
                        _fileLogger?.Debug($"Loaded (manual pause active): {Path.GetFileName(newSongPath)}");
                        OnMusicStarted?.Invoke(settings);
                    }
                    else
                    {
                        _musicPlayer.Play();
                        MarkSongStart();
                        _fader.FadeIn();
                        _fileLogger?.Debug($"Playing game music (from paused default): {Path.GetFileName(newSongPath)}");
                        OnMusicStarted?.Invoke(settings);
                    }
                }
                else
                {
                    _fileLogger?.Debug($"Initial playback with fade-in: {Path.GetFileName(newSongPath)}");

                    _currentSongPath = null;

                    _musicPlayer.Load(newSongPath);
                    _musicPlayer.Volume = 0;
                    _currentSongPath = newSongPath;
                    ClearAllPauseSources();

                    if (_isPaused)
                    {
                        _fileLogger?.Debug($"Loaded (manual pause active): {Path.GetFileName(newSongPath)}");
                        OnMusicStarted?.Invoke(settings);
                    }
                    else
                    {
                        _musicPlayer.Play();
                        MarkSongStart();
                        _fader.FadeIn();
                        _fileLogger?.Debug($"Playing (initial): {Path.GetFileName(newSongPath)}");
                        OnMusicStarted?.Invoke(settings);
                    }
                }
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
                bool wasPlaying = !_isPaused;
                StopPreviewTimer();
                _fileLogger?.Debug("Pause: Calling fader.Pause()");
                _fader?.Pause();
                _activePauseSources.Add(PauseSource.Manual);

                // Fire event if state changed from playing to paused
                if (wasPlaying)
                {
                    OnPlaybackStateChanged?.Invoke();
                }
            }
        }

        public void Resume()
        {
            if (_musicPlayer?.IsLoaded == true)
            {
                bool wasPaused = _isPaused;
                _fileLogger?.Debug("Resume: Calling fader.Resume()");
                _activePauseSources.Remove(PauseSource.Manual);

                // Only resume fader and fire event if all pause sources are cleared
                if (wasPaused && !_isPaused)
                {
                    _fader?.Resume();
                    OnPlaybackStateChanged?.Invoke();
                }
            }
        }

        public void PauseImmediate()
        {
            if (_musicPlayer?.IsLoaded == true && _musicPlayer.IsActive)
            {
                _fader?.CancelFade();
                _musicPlayer.Pause();
                _activePauseSources.Add(PauseSource.Manual);
            }
        }

        public void ResumeImmediate()
        {
            if (_musicPlayer?.IsLoaded == true)
            {
                _activePauseSources.Remove(PauseSource.Manual);
                if (!_isPaused)
                {
                    _musicPlayer.Resume();
                    _musicPlayer.Volume = _targetVolume * _volumeMultiplier;
                }
            }
        }

        public List<string> GetAvailableSongs(Game game)
        {
            return _fileService?.GetAvailableSongs(game) ?? new List<string>();
        }

        public void RefreshSongCount()
        {
            if (_currentGame == null) return;

            var songs = _fileService?.GetAvailableSongs(_currentGame) ?? new List<string>();
            var newCount = songs.Count;

            if (_currentGameSongCount != newCount)
            {
                _fileLogger?.Info($"RefreshSongCount: Song count changed from {_currentGameSongCount} to {newCount}");
                _currentGameSongCount = newCount;
                OnSongCountChanged?.Invoke();
            }
        }

        public void SkipToNextSong()
        {
            if (_currentGame == null)
            {
                _fileLogger?.Info("SkipToNextSong: No current game, cannot skip");
                return;
            }

            var songs = _fileService?.GetAvailableSongs(_currentGame) ?? new List<string>();
            if (songs.Count < 2)
            {
                _fileLogger?.Info($"SkipToNextSong: Only {songs.Count} song(s) available, cannot skip");
                return;
            }

            // Select random song, avoiding current song
            string nextSong;
            int attempts = 0;
            do
            {
                nextSong = songs[_random.Next(songs.Count)];
                attempts++;
            }
            while (nextSong == _currentSongPath && attempts < 10);

            _fileLogger?.Info($"SkipToNextSong: Skipping from '{Path.GetFileName(_currentSongPath)}' to '{Path.GetFileName(nextSong)}'");

            _previousSongPath = _currentSongPath;

            // Skipping is an explicit user action — clear manual pause so the new song plays
            bool wasPaused = _isPaused;
            RemovePauseSource(Models.PauseSource.Manual);

            if (wasPaused)
            {
                // Already silent — skip the fade and load immediately
                _musicPlayer?.Load(nextSong);
                _musicPlayer?.Play();
                _fader?.FadeIn();
                MarkSongStart();
                _currentSongPath = nextSong;
                OnPlaybackStateChanged?.Invoke();
            }
            else
            {
                // Fire song changed early so UI (Now Playing text, visualizer) updates immediately
                OnSongChanged?.Invoke(nextSong);

                // Playing — use Switch() for smooth crossfade with preloading
                _fader?.Switch(
                    stopAction: () =>
                    {
                        _musicPlayer?.Close();
                    },
                    preloadAction: () =>
                    {
                        _musicPlayer?.PreLoad(nextSong);
                    },
                    playAction: () =>
                    {
                        _musicPlayer?.Load(nextSong);
                        _musicPlayer?.Play();
                        MarkSongStart();
                        _currentSongPathBacking = nextSong; // Set backing field directly (OnSongChanged already fired)
                        OnPlaybackStateChanged?.Invoke();
                    }
                );
            }
        }

        public void RestartCurrentSong()
        {
            if (_musicPlayer?.IsLoaded != true || string.IsNullOrEmpty(_currentSongPath)) return;

            _fileLogger?.Debug($"RestartCurrentSong: Restarting '{Path.GetFileName(_currentSongPath)}'");

            // Restarting is an explicit user action — clear manual pause so it plays
            RemovePauseSource(Models.PauseSource.Manual);

            _musicPlayer.Play(TimeSpan.Zero);
            MarkSongStart();
        }

        public void SetVolume(double volume)
        {
            _targetVolume = Math.Max(0.0, Math.Min(1.0, volume));

            if (_musicPlayer != null && !_isPaused && _musicPlayer.IsActive)
            {
                _musicPlayer.Volume = _targetVolume * _volumeMultiplier * _idleVolumeMultiplier;
            }
        }

        public double GetVolume() => _targetVolume;

        public void SetVolumeMultiplier(double multiplier)
        {
            _volumeMultiplier = Math.Max(0.0, Math.Min(1.0, multiplier));

            if (_musicPlayer != null && !_isPaused && _musicPlayer.IsActive)
            {
                _musicPlayer.Volume = _targetVolume * _volumeMultiplier * _idleVolumeMultiplier;
            }
        }

        public void SetIdleVolumeMultiplier(double multiplier)
        {
            _idleVolumeMultiplier = Math.Max(0.0, Math.Min(1.0, multiplier));

            if (_musicPlayer != null && !_isPaused && _musicPlayer.IsActive)
            {
                _musicPlayer.Volume = _targetVolume * _volumeMultiplier * _idleVolumeMultiplier;
            }
        }

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

                Logger.Debug($"[PlayPreview] {Path.GetFileName(filePath)} at volume {volume:F2}");

                // Cancel any ongoing fade operation first
                _fader.CancelFade();

                // Stop current playback completely (no fading)
                _musicPlayer.Stop();
                _musicPlayer.Close();

                // Load and play immediately (no fading)
                _musicPlayer.Load(filePath);
                _musicPlayer.Volume = volume;
                _musicPlayer.Play();

                // Update internal state to match what we just set
                _targetVolume = volume;
                ClearAllPauseSources();
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
                    string selected;
                    int attempts = 0;
                    do
                    {
                        selected = songs[_random.Next(songs.Count)];
                        attempts++;
                    }
                    while (selected == _previousSongPath && songs.Count > 1 && attempts < 10);

                    _previousSongPath = selected;
                    _fileLogger?.Debug($"Randomized song selection: {Path.GetFileName(selected)}");
                    return selected;
                }
            }

            var firstSong = songs.FirstOrDefault();
            _previousSongPath = firstSong;
            return firstSong;
        }

        // Marks song start time and starts preview timer if preview mode is enabled for game music
        private void MarkSongStart()
        {
            _songStartTime = DateTime.Now;

            if (_currentSettings?.EnablePreviewMode == true &&
                !_isPlayingDefaultMusic &&
                !_isCurrentSongDefaultMusic)
            {
                _previewTimer.Start();
                _fileLogger?.Debug($"Preview timer started: {Path.GetFileName(_currentSongPath)} ({_currentSettings.PreviewDuration}s)");
            }
        }

        private void StopPreviewTimer()
        {
            if (_previewTimer.IsEnabled)
            {
                _previewTimer.Stop();
                _fileLogger?.Debug("Preview timer stopped");
            }
        }

        // Preview timer tick: checks if preview duration reached, then either randomizes or restarts song
        private void OnPreviewTimerTick(object sender, EventArgs e)
        {
            try
            {
                if (ShouldRestartForPreview() && _musicPlayer?.IsActive == true)
                {
                    _previewTimer.Stop();

                    // Stop after preview ends instead of restarting
                    if (_currentSettings?.StopAfterSongEnds == true)
                    {
                        _fileLogger?.Info($"Preview ended (StopAfterSongEnds): {Path.GetFileName(_currentSongPath)}");
                        FadeOutAndStop();
                        OnMusicStopped?.Invoke(_currentSettings);
                        return;
                    }

                    // Check if we should randomize to a different song
                    string nextSong = _currentSongPath;
                    bool shouldRandomize = _currentSettings?.RandomizeOnMusicEnd == true && _currentGame != null;

                    if (shouldRandomize)
                    {
                        var songs = _fileService.GetAvailableSongs(_currentGame);
                        if (songs.Count > 1)
                        {
                            // Select random song (avoiding immediate repeat, max 10 attempts)
                            int attempts = 0;
                            do
                            {
                                nextSong = songs[_random.Next(songs.Count)];
                                attempts++;
                            }
                            while (nextSong == _currentSongPath && songs.Count > 1 && attempts < 10);

                            _fileLogger?.Debug($"Preview: randomizing to {Path.GetFileName(nextSong)}");
                        }
                        else
                        {
                            _fileLogger?.Debug($"Preview: restarting (single song): {Path.GetFileName(nextSong)}");
                        }
                    }
                    else
                    {
                        _fileLogger?.Debug($"Preview: restarting with fade: {Path.GetFileName(nextSong)}");
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
                            _musicPlayer.Volume = 0; // NAudio Load() creates new VolumeSampleProvider at 1.0 — must reset before Play()
                            _musicPlayer.Play(TimeSpan.Zero);

                            MarkSongStart();

                            _fileLogger?.Debug($"Preview: now playing {Path.GetFileName(songToPlay)}");
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

        // Checks if preview mode is active and song has exceeded preview duration
        private bool ShouldRestartForPreview()
        {
            // Preview mode only applies to game music (not default music)
            if (_isPlayingDefaultMusic || _isCurrentSongDefaultMusic)
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

        // Handles media ended: randomization on song end, preview restart, or normal looping.
        // Threading is handled at the player level (NAudioMusicPlayer/SDL2MusicPlayer).
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

                    // Stop after song ends: fade out and stop instead of looping/randomizing
                    if (_currentSettings?.StopAfterSongEnds == true)
                    {
                        _fileLogger?.Info($"Song ended (StopAfterSongEnds): {Path.GetFileName(_currentSongPath)}");
                        StopPreviewTimer();
                        FadeOutAndStop();
                        OnMusicStopped?.Invoke(_currentSettings);
                        return;
                    }

                    // PNS PATTERN: Check for randomization on song end (similar to PlayniteSound)
                    if (_currentSettings?.RandomizeOnMusicEnd == true && _currentGame != null)
                    {
                        // Only randomize for game music (not default music)
                        if (!_isCurrentSongDefaultMusic)
                        {
                            var songs = _fileService.GetAvailableSongs(_currentGame);
                            if (songs.Count > 1)
                            {
                                // Select random song (avoiding immediate repeat, max 10 attempts)
                                string nextSong;
                                int attempts = 0;
                                do
                                {
                                    nextSong = songs[_random.Next(songs.Count)];
                                    attempts++;
                                }
                                while (nextSong == _currentSongPath && songs.Count > 1 && attempts < 10);

                                _previousSongPath = _currentSongPath;
                                LoadAndPlayFile(nextSong);
                                _currentSongPath = nextSong; // Set after LoadAndPlayFile so IsPlaying is true when OnSongChanged fires
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
