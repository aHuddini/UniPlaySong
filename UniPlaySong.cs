using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HtmlAgilityPack;
using UniPlaySong.Downloaders;
using UniPlaySong.Monitors;
using UniPlaySong.Services;
using UniPlaySong.Common;
using UniPlaySong.Menus;
using UniPlaySong.Models;

namespace UniPlaySong
{
    /// <summary>
    /// SDL2_mixer wrapper for suppressing Playnite's native background music
    /// </summary>
    static class SDL_mixer
    {
        const string nativeLibName = "SDL2_mixer";

        [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Mix_HaltMusic();
        
        [DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Mix_FreeMusic(IntPtr music);
    }

    /// <summary>
    /// UniPlaySong - Console-like game music preview extension for Playnite
    /// Plays game-specific music when browsing your library
    /// </summary>
    public class UniPlaySong : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI _api;
        private readonly FileLogger _fileLogger;
        
        // Xbox controller monitoring for login bypass
        private CancellationTokenSource _controllerLoginMonitoringCancellation;
        private bool _isControllerLoginMonitoring = false;
        
        // Native music suppression monitoring
        private System.Windows.Threading.DispatcherTimer _nativeMusicSuppressionTimer;
        private bool _isNativeMusicSuppressionActive = false;
        private bool _hasLoggedSuppression = false;
        
        // Assembly resolution handler for Material Design and other dependencies
        static UniPlaySong()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    string assemblyName = new AssemblyName(args.Name).Name;
                    string extensionPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    
                    if (string.IsNullOrEmpty(extensionPath))
                    {
                        return null;
                    }
                    
                    // Try to load from extension directory
                    string dllPath = Path.Combine(extensionPath, $"{assemblyName}.dll");
                    if (File.Exists(dllPath))
                    {
                        logger?.Info($"Resolving assembly {assemblyName} from {dllPath}");
                        return Assembly.LoadFrom(dllPath);
                    }
                    
                    // Also try with .exe extension (for some dependencies)
                    string exePath = Path.Combine(extensionPath, $"{assemblyName}.exe");
                    if (File.Exists(exePath))
                    {
                        logger?.Info($"Resolving assembly {assemblyName} from {exePath}");
                        return Assembly.LoadFrom(exePath);
                    }
                }
                catch (Exception ex)
                {
                    logger?.Error(ex, $"Error resolving assembly {args.Name}");
                }
                
                return null;
            };
        }
        
        // Services
        private IMusicPlaybackService _playbackService;
        private IDownloadManager _downloadManager;
        private GameMusicFileService _fileService;
        private ErrorHandlerService _errorHandler;
        private GameMenuHandler _gameMenuHandler;
        private MainMenuHandler _mainMenuHandler;
        private DownloadDialogService _downloadDialogService;
        private SettingsService _settingsService;
        private SearchCacheService _cacheService;
        private Services.INormalizationService _normalizationService;
        
        // Coordinator (Phase 1: Created but not used yet - existing code remains intact)
        private IMusicPlaybackCoordinator _coordinator;
        
        // Shared settings instance (Phase 2: Compatibility property, will be removed in Phase 6)
        private UniPlaySongSettings _settings => _settingsService?.Current;
        

        // HTTP client for downloads
        private readonly HttpClient _httpClient;
        private readonly HtmlWeb _htmlWeb;

        public override Guid Id { get; } = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        
        // Direct API access (preferred over properties for simplicity)
        private IEnumerable<Game> SelectedGames => _api.MainView.SelectedGames;
        private bool IsFullscreen => _api.ApplicationInfo.Mode == ApplicationMode.Fullscreen;
        private bool IsDesktop => _api.ApplicationInfo.Mode == ApplicationMode.Desktop;

        public UniPlaySong(IPlayniteAPI api) : base(api)
        {
            _api = api;
            _httpClient = new HttpClient();
            _htmlWeb = new HtmlWeb();

            // Initialize file logger
            try
            {
                var extensionPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                _fileLogger = new FileLogger(extensionPath);
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                _fileLogger.Info($"=== UniPlaySong v{version} Starting ===");
            }
            catch
            {
                // Continue without file logger if it fails
            }

            Properties = new GenericPluginProperties { HasSettings = true };

            // Initialize SettingsService (Phase 2: Now using it for settings management)
            _settingsService = new SettingsService(_api, logger, _fileLogger, this);
            
            // Subscribe to settings changes for automatic updates
            _settingsService.SettingsChanged += OnSettingsServiceChanged;

            InitializeServices();
            InitializeMenuHandlers();
            
            // Attach window monitor for theme support
            WindowMonitor.Attach(_playbackService, _errorHandler);
            
            // Subscribe to settings changes for video pause/resume
            if (_settings != null)
            {
                _settings.PropertyChanged += OnSettingsChanged;
                
                // Attach media monitor if pause on trailer is enabled
                if (_settings.PauseOnTrailer)
                {
                    MediaElementsMonitor.Attach(_api, _settings);
                }
            }
            
            // Subscribe to SettingsService property changes
            _settingsService.SettingPropertyChanged += OnSettingsServicePropertyChanged;
            
            // Subscribe to main model changes for view transitions
            SubscribeToMainModel();
            
            // Try to suppress native music early if in fullscreen (before OnApplicationStarted)
            // This helps catch native music that starts very early in the initialization
            if (IsFullscreen)
            {
                bool shouldSuppress = _settings?.SuppressPlayniteBackgroundMusic == true ||
                                     (_settings?.EnableDefaultMusic == true && _settings?.UseNativeMusicAsDefault == true);
                
                if (shouldSuppress)
                {
                    // Try immediate suppression (may not work yet if audio not initialized, but no harm)
                    try
                    {
                        SuppressNativeMusic();
                    }
                    catch
                    {
                        // Silently ignore - audio system may not be ready yet
                    }
                }
            }

            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            logger.Info($"UniPlaySong v{assemblyVersion} loaded");
        }

        #region Playnite Events

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            // All game selection logic is handled by the coordinator
            var game = args?.NewValue?.FirstOrDefault();
            _coordinator.HandleGameSelected(game, IsFullscreen);
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            _fileLogger?.Info($"Application started - Mode: {_api.ApplicationInfo.Mode}");
            
            // Ensure settings are loaded
            if (_settings == null)
            {
                _fileLogger?.Warn("OnApplicationStarted: Settings are null, reloading...");
                _settingsService?.LoadSettings();
            }
            
            // Reset skip state when starting in fullscreen mode (for SkipFirstSelectionAfterModeSwitch)
            if (IsFullscreen && _settings?.SkipFirstSelectionAfterModeSwitch == true)
            {
                _coordinator.ResetSkipStateForModeSwitch();
            }
            
            // Attach input handler for login screen detection
            if (_settings?.ThemeCompatibleSilentSkip == true && IsFullscreen)
            {
                AttachLoginInputHandler();
            }
            
            // Suppress native music on startup if:
            // 1. SuppressPlayniteBackgroundMusic is explicitly enabled, OR
            // 2. UseNativeMusicAsDefault is enabled (we play the same file ourselves, so suppress Playnite's duplicate playback)
            bool shouldSuppressOnStartup = _settings?.SuppressPlayniteBackgroundMusic == true ||
                                          (_settings?.EnableDefaultMusic == true && _settings?.UseNativeMusicAsDefault == true);
            
            if (IsFullscreen && shouldSuppressOnStartup)
            {
                _fileLogger?.Info($"OnApplicationStarted: Starting native music suppression (SuppressPlayniteBackgroundMusic={_settings?.SuppressPlayniteBackgroundMusic}, UseNativeMusicAsDefault={_settings?.UseNativeMusicAsDefault})");
                // Suppress immediately (in case audio is already initialized)
                SuppressNativeMusic();
                // Start continuous monitoring to catch music that starts at different times
                StartNativeMusicSuppression();
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            _playbackService?.Stop();
            _downloadManager?.Cleanup();
            _httpClient?.Dispose();
            
            // Stop monitoring systems
            StopControllerLoginMonitoring();
            StopNativeMusicSuppression();
            
            _fileLogger?.Info("Application stopped");
        }

        #endregion

        #region Music Playback

        /// <summary>
        /// Central gatekeeper - delegates to coordinator for all playback decisions
        /// </summary>
        private bool ShouldPlayMusic()
        {
            // Phase 2: Use coordinator for state - get current game for check
            var game = SelectedGames?.FirstOrDefault();
            return _coordinator.ShouldPlayMusic(game);
        }

        #endregion

        #region Event Handlers

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UniPlaySongSettings.VideoIsPlaying))
            {
                _coordinator.HandleVideoStateChange(_settings.VideoIsPlaying);
            }
        }

        /// <summary>
        /// Handles SettingsService property changes (Phase 2)
        /// </summary>
        private void OnSettingsServicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Handle video state changes for coordinator
            if (e.PropertyName == nameof(UniPlaySongSettings.VideoIsPlaying))
            {
                _coordinator?.HandleVideoStateChange(_settings.VideoIsPlaying);
            }
        }

        /// <summary>
        /// Handles SettingsService settings changed event (Phase 6)
        /// Coordinator subscribes directly to SettingsService, so no manual UpdateSettings() needed
        /// This handler manages plugin-level concerns (volume, media monitor, PropertyChanged subscription)
        /// </summary>
        private void OnSettingsServiceChanged(object sender, SettingsChangedEventArgs e)
        {
            try
            {
                // Coordinator subscribes directly to SettingsService - no manual update needed
                
                // Update playback service volume if needed
                if (_playbackService != null && e.NewSettings != null)
                {
                    _playbackService.SetVolume(e.NewSettings.MusicVolume / Constants.VolumeDivisor);
                }
                
                // Update media monitor if pause on trailer setting changed
                if (e.OldSettings?.PauseOnTrailer != e.NewSettings?.PauseOnTrailer)
                {
                    if (e.NewSettings?.PauseOnTrailer == true)
                    {
                        MediaElementsMonitor.Attach(_api, e.NewSettings);
                    }
                    // Note: MediaElementsMonitor doesn't have a Detach method - it stops automatically
                    // when there are no media elements (timer stops when mediaElementPositions.Count == 0)
                }
                
                // Re-subscribe to PropertyChanged for backward compatibility
                if (e.OldSettings != null)
                {
                    e.OldSettings.PropertyChanged -= OnSettingsChanged;
                }
                if (e.NewSettings != null)
                {
                    e.NewSettings.PropertyChanged += OnSettingsChanged;
                }
                
                logger.Info("SettingsService: Settings updated and services notified");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error handling settings change");
            }
        }

        /// <summary>
        /// Called when settings are saved - reloads settings via SettingsService
        /// SettingsService automatically notifies all subscribers (Phase 2)
        /// </summary>
        public void OnSettingsSaved()
        {
            try
            {
                // Reload settings from disk via SettingsService
                // SettingsService will automatically notify all subscribers via SettingsChanged event
                _settingsService.LoadSettings();
                
                logger.Info("Settings saved and reloaded via SettingsService");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error reloading settings");
            }
        }

        private void OnMainModelChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "ActiveView") return;
            _coordinator.HandleViewChange();
        }

        private void OnLoginDismissKeyPress(object sender, KeyEventArgs e)
        {
            // Keys that typically dismiss login screens
            if (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Escape || e.Key == Key.Return)
            {
                _coordinator.HandleLoginDismiss();
                // Compatible mode: Native music plays naturally (we don't suppress unless our music plays)
                // No special handling needed - Playnite handles it
            }
        }

        #endregion

        #region Initialization

        private void InitializeServices()
        {
            try
            {
                var basePath = Path.Combine(_api.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName);
                var gamesPath = Path.Combine(basePath, Constants.GamesFolderName);
                var tempPath = Path.Combine(basePath, Constants.TempFolderName);

                // Initialize error handler service (for centralized error handling)
                _errorHandler = new ErrorHandlerService(logger, _fileLogger, _api);

                _fileService = new GameMusicFileService(gamesPath, _errorHandler);
                
                // Use SDL2 for reliable volume control (matching PlayniteSound)
                IMusicPlayer musicPlayer;
                try
                {
                    musicPlayer = new SDL2MusicPlayer(_errorHandler);
                    logger.Info("Initialized SDL2MusicPlayer");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Failed to initialize SDL2MusicPlayer, falling back to WPF MediaPlayer: {ex.Message}");
                    musicPlayer = new MusicPlayer(_errorHandler);
                }
                
                _playbackService = new MusicPlaybackService(musicPlayer, _fileService, _fileLogger, _errorHandler);
                // Suppress native music when our music starts (if suppression enabled or using native as default)
                _playbackService.OnMusicStarted += (settings) => 
                {
                    if (!IsFullscreen)
                        return;
                        
                    bool shouldSuppress = settings?.SuppressPlayniteBackgroundMusic == true ||
                                         (settings?.EnableDefaultMusic == true && settings?.UseNativeMusicAsDefault == true);
                    
                    if (shouldSuppress)
                    {
                        SuppressNativeMusic();
                    }
                };
                
                if (_settings != null)
                {
                    _playbackService.SetVolume(_settings.MusicVolume / Constants.VolumeDivisor);
                }
                
                // Initialize coordinator - centralizes all music playback logic and state management
                _coordinator = new MusicPlaybackCoordinator(
                    _playbackService,
                    _settingsService,
                    logger,
                    _fileLogger,
                    () => IsFullscreen,
                    () => IsDesktop,
                    () => SelectedGames?.FirstOrDefault()
                );
                _fileLogger?.Info("MusicPlaybackCoordinator initialized");

                // Initialize search cache service
                var extensionDataPath = Path.Combine(_api.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName);
                _cacheService = new SearchCacheService(
                    extensionDataPath,
                    enabled: _settings?.EnableSearchCache ?? true,
                    cacheDurationDays: _settings?.SearchCacheDurationDays ?? 7);
                _fileLogger?.Info("SearchCacheService initialized");

                _downloadManager = new DownloadManager(
                    _httpClient, _htmlWeb, tempPath,
                    _settings?.YtDlpPath, _settings?.FFmpegPath, _errorHandler, _cacheService, _settings);

                _downloadDialogService = new DownloadDialogService(
                    _api, _downloadManager, _playbackService, _fileService, _settingsService, _errorHandler);
                
                // Initialize normalization service with playback service and base path for backups
                _normalizationService = new Services.AudioNormalizationService(_errorHandler, _playbackService, basePath);
                _fileLogger?.Info("AudioNormalizationService initialized");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to initialize services");
            }
        }

        private void InitializeMenuHandlers()
        {
            try
            {
                _gameMenuHandler = new GameMenuHandler(
                    _api, logger, _downloadManager, _fileService, 
                    _playbackService, _downloadDialogService, _errorHandler);
                _mainMenuHandler = new MainMenuHandler(_api, Id);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to initialize menu handlers");
            }
        }

        private void SubscribeToMainModel()
        {
            try
            {
                var mainModel = GetMainModel();
                if (mainModel is INotifyPropertyChanged observable)
                {
                    observable.PropertyChanged += OnMainModelChanged;
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Could not subscribe to main model: {ex.Message}", ex);
            }
        }

        private void AttachLoginInputHandler()
        {
            try
            {
                if (Application.Current?.MainWindow != null)
                {
                    Application.Current.MainWindow.PreviewKeyDown += OnLoginDismissKeyPress;
                    _fileLogger?.Info("Login input handler attached");
                    
                    // Also start monitoring Xbox controller for login bypass
                    StartControllerLoginMonitoring();
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Failed to attach input handler: {ex.Message}", ex);
            }
        }

        private dynamic GetMainModel()
        {
            try
            {
                return _api.MainView
                    .GetType()
                    .GetField("mainModel", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(_api.MainView);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Start continuous native music suppression monitoring for initial fullscreen startup.
        /// Runs longer (10 seconds) to ensure we catch native music regardless of when Playnite loads it.
        /// </summary>
        private void StartNativeMusicSuppression()
        {
            if (_isNativeMusicSuppressionActive)
                return;

            try
            {
                _isNativeMusicSuppressionActive = true;
                _hasLoggedSuppression = false; // Reset for this session
                
                // Create a timer that checks every 50ms for native music (faster than before)
                _nativeMusicSuppressionTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(50)
                };
                
                _nativeMusicSuppressionTimer.Tick += (s, e) =>
                {
                    try
                    {
                        SuppressNativeMusic();
                    }
                    catch (Exception ex)
                    {
                        _fileLogger?.Debug($"Error in native music suppression timer: {ex.Message}");
                    }
                };
                
                _nativeMusicSuppressionTimer.Start();
                _fileLogger?.Info("Started native music suppression monitoring (15 second window)");
                
                // Stop monitoring after 15 seconds (extended to catch delayed audio initialization and theme transitions)
                Task.Delay(15000).ContinueWith(_ =>
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        StopNativeMusicSuppression();
                    }));
                });
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Error starting native music suppression: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop continuous native music suppression monitoring
        /// </summary>
        private void StopNativeMusicSuppression()
        {
            if (!_isNativeMusicSuppressionActive)
                return;

            try
            {
                _isNativeMusicSuppressionActive = false;
                
                if (_nativeMusicSuppressionTimer != null)
                {
                    _nativeMusicSuppressionTimer.Stop();
                    _nativeMusicSuppressionTimer = null;
                }
                
                _fileLogger?.Info("Stopped native music suppression monitoring");
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Error stopping native music suppression: {ex.Message}");
            }
        }

        /// <summary>
        /// Suppresses Playnite's native background music by stopping playback and preventing reload.
        /// Triggers when:
        /// 1. SuppressPlayniteBackgroundMusic is explicitly enabled, OR
        /// 2. UseNativeMusicAsDefault is enabled (we play the same file ourselves)
        /// </summary>
        private void SuppressNativeMusic()
        {
            if (!IsFullscreen)
                return;
            
            // Check if we should suppress
            bool shouldSuppress = _settings?.SuppressPlayniteBackgroundMusic == true ||
                                 (_settings?.EnableDefaultMusic == true && _settings?.UseNativeMusicAsDefault == true);
            
            if (!shouldSuppress)
                return;

            try
            {
                // Get BackgroundMusic property via reflection
                var mainModel = GetMainModel();
                if (mainModel?.App == null)
                    return;
                    
                var appType = mainModel.App.GetType();
                
                // Walk up inheritance chain to find FullscreenApplication
                while (appType != null && appType.Name != "FullscreenApplication")
                {
                    appType = appType.BaseType;
                }
                
                if (appType == null)
                    return;
                    
                var backgroundMusicProperty = appType.GetProperty("BackgroundMusic", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                
                if (backgroundMusicProperty == null)
                    return;
                    
                IntPtr currentMusic = (IntPtr)(backgroundMusicProperty.GetValue(null) ?? IntPtr.Zero);
                
                if (currentMusic != IntPtr.Zero)
                {
                    // Stop music playback
                    SDL_mixer.Mix_HaltMusic();
                    // Free the music resource
                    SDL_mixer.Mix_FreeMusic(currentMusic);
                    // Set BackgroundMusic to Zero to prevent Playnite from reloading it
                    backgroundMusicProperty.GetSetMethod(true)?.Invoke(null, new object[] { IntPtr.Zero });
                    
                    if (!_hasLoggedSuppression)
                    {
                        _fileLogger?.Info("SuppressNativeMusic: Successfully stopped native background music");
                        _hasLoggedSuppression = true;
                    }
                }
                else
                {
                    // Music not loaded yet, but set it to Zero anyway to prevent loading
                    backgroundMusicProperty.GetSetMethod(true)?.Invoke(null, new object[] { IntPtr.Zero });
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"SuppressNativeMusic: Exception - {ex.Message}");
                // Fallback to simple halt if reflection fails
                try
                {
                    SDL_mixer.Mix_HaltMusic();
                }
                catch { }
            }
        }

        #endregion

        #region Audio Normalization

        /// <summary>
        /// Normalize all music files in the library
        /// </summary>
        public void NormalizeAllMusicFiles()
        {
            try
            {
                if (_normalizationService == null)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage("Normalization service not available.", "UniPlaySong");
                    return;
                }

                if (_settings == null)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage("Settings not available.", "UniPlaySong");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_settings.FFmpegPath))
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg path is not configured. Please configure FFmpeg in the extension settings.",
                        "FFmpeg Not Configured");
                    return;
                }

                if (!_normalizationService.ValidateFFmpegAvailable(_settings.FFmpegPath))
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        $"FFmpeg not found or not accessible at: {_settings.FFmpegPath}\n\nPlease verify the FFmpeg path in extension settings.",
                        "FFmpeg Not Available");
                    return;
                }

                // Stop music playback before normalization to prevent file locking
                try
                {
                    if (_playbackService != null && _playbackService.IsPlaying)
                    {
                        logger.Info("Stopping music playback before bulk normalization");
                        _playbackService.Stop();
                        
                        // Give a moment for files to be released
                        System.Threading.Thread.Sleep(300);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Error stopping playback before normalization");
                }

                // Get all music files from all games
                var allMusicFiles = new List<string>();
                foreach (var game in PlayniteApi.Database.Games)
                {
                    var gameFiles = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                    allMusicFiles.AddRange(gameFiles);
                }

                if (allMusicFiles.Count == 0)
                {
                    PlayniteApi.Dialogs.ShowMessage("No music files found in your library.", "No Files to Normalize");
                    return;
                }

                // Show progress dialog
                ShowNormalizationProgress(allMusicFiles, "Normalizing All Music Files");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in NormalizeAllMusicFiles");
                PlayniteApi.Dialogs.ShowErrorMessage($"Error starting normalization: {ex.Message}", "Normalization Error");
            }
        }

        /// <summary>
        /// Normalize music files for selected games
        /// </summary>
        /// <summary>
        /// Normalize music files for a single game (for fullscreen menu)
        /// Shows progress dialog and displays confirmation when complete
        /// </summary>
        public void NormalizeSelectedGamesFullscreen(Game game)
        {
            if (game == null)
            {
                PlayniteApi.Dialogs.ShowMessage("No game selected.", "Normalization Error");
                return;
            }

            NormalizeSelectedGames(new List<Game> { game }, showSimpleConfirmation: true);
        }

        public void NormalizeSelectedGames(List<Playnite.SDK.Models.Game> games, bool showSimpleConfirmation = false)
        {
            try
            {
                if (_normalizationService == null)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage("Normalization service not available.", "UniPlaySong");
                    return;
                }

                if (_settings == null)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage("Settings not available.", "UniPlaySong");
                    return;
                }

                if (games == null || games.Count == 0)
                {
                    PlayniteApi.Dialogs.ShowMessage("No games selected.", "No Games Selected");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_settings.FFmpegPath))
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg path is not configured. Please configure FFmpeg in the extension settings.",
                        "FFmpeg Not Configured");
                    return;
                }

                if (!_normalizationService.ValidateFFmpegAvailable(_settings.FFmpegPath))
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        $"FFmpeg not found or not accessible at: {_settings.FFmpegPath}\n\nPlease verify the FFmpeg path in extension settings.",
                        "FFmpeg Not Available");
                    return;
                }

                // Stop music playback before normalization to prevent file locking
                try
                {
                    if (_playbackService != null && _playbackService.IsPlaying)
                    {
                        logger.Info("Stopping music playback before normalizing selected games");
                        _playbackService.Stop();
                        
                        // Give a moment for files to be released
                        System.Threading.Thread.Sleep(300);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Error stopping playback before normalization");
                }

                // Get all music files from selected games
                var allMusicFiles = new List<string>();
                foreach (var game in games)
                {
                    var gameFiles = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                    allMusicFiles.AddRange(gameFiles);
                }

                if (allMusicFiles.Count == 0)
                {
                    PlayniteApi.Dialogs.ShowMessage("No music files found for selected games.", "No Files to Normalize");
                    return;
                }

                // Show progress dialog
                var gameNames = string.Join(", ", games.Select(g => g.Name).Take(3));
                if (games.Count > 3) gameNames += $" and {games.Count - 3} more";
                ShowNormalizationProgress(allMusicFiles, $"Normalizing Music for {games.Count} Games");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in NormalizeSelectedGames");
                PlayniteApi.Dialogs.ShowErrorMessage($"Error starting normalization: {ex.Message}", "Normalization Error");
            }
        }

        /// <summary>
        /// Show normalization progress dialog and execute normalization
        /// </summary>
        private void ShowNormalizationProgress(List<string> musicFiles, string title, bool showSimpleConfirmation = false)
        {
            try
            {
                // Create progress dialog
                var progressDialog = new Views.NormalizationProgressDialog();

                // Create window
                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = true,
                    ShowCloseButton = true
                });

                window.Title = title;
                window.Width = 600;
                window.Height = 500;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.ShowInTaskbar = false;
                window.ResizeMode = ResizeMode.CanResize;
                window.Content = progressDialog;

                // Handle window closing
                window.Closing += (s, e) =>
                {
                    try
                    {
                        var mainWindow = PlayniteApi.Dialogs.GetCurrentAppWindow();
                        if (mainWindow != null)
                        {
                            mainWindow.Activate();
                            mainWindow.Focus();
                            mainWindow.Topmost = true;
                            mainWindow.Topmost = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, "Error returning focus during normalization dialog close");
                    }
                };

                // Start normalization asynchronously
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var settings = new Models.NormalizationSettings
                        {
                            TargetLoudness = _settings.NormalizationTargetLoudness,
                            TruePeak = _settings.NormalizationTruePeak,
                            LoudnessRange = _settings.NormalizationLoudnessRange,
                            AudioCodec = _settings.NormalizationCodec,
                            NormalizationSuffix = _settings.NormalizationSuffix,
                            SkipAlreadyNormalized = _settings.SkipAlreadyNormalized,
                            DoNotPreserveOriginals = _settings.DoNotPreserveOriginals,
                            FFmpegPath = _settings.FFmpegPath
                        };

                        var progress = new Progress<Models.NormalizationProgress>(p =>
                        {
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                progressDialog.ReportProgress(p);
                            }));
                        });

                        var result = await _normalizationService.NormalizeBulkAsync(
                            musicFiles,
                            settings,
                            progress,
                            progressDialog.CancellationToken);

                        // Show completion message
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            // Close progress dialog first
                            window.Close();

                            if (showSimpleConfirmation)
                            {
                                // Simple confirmation for fullscreen menu
                                PlayniteApi.Dialogs.ShowMessage(
                                    "Music normalized successfully.\n\nChanges will take effect when the game is re-selected.",
                                    "Normalization Complete");
                            }
                            else
                            {
                                // Detailed confirmation for settings menu
                                var message = $"Normalization Complete!\n\n" +
                                            $"Total: {result.TotalFiles} files\n" +
                                            $"Succeeded: {result.SuccessCount}\n" +
                                            $"Failed: {result.FailureCount}";

                                if (result.FailedFiles.Count > 0)
                                {
                                    message += $"\n\nFailed files:\n{string.Join("\n", result.FailedFiles.Take(5).Select(f => System.IO.Path.GetFileName(f)))}";
                                    if (result.FailedFiles.Count > 5)
                                    {
                                        message += $"\n... and {result.FailedFiles.Count - 5} more";
                                    }
                                }

                                PlayniteApi.Dialogs.ShowMessage(message, "Normalization Complete");
                            }
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            PlayniteApi.Dialogs.ShowMessage("Normalization was cancelled.", "Normalization Cancelled");
                        }));
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error during normalization");
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            window.Close();
                            PlayniteApi.Dialogs.ShowErrorMessage($"Error during normalization: {ex.Message}", "Normalization Error");
                        }));
                    }
                });

                // Show dialog
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing normalization progress dialog");
                PlayniteApi.Dialogs.ShowErrorMessage($"Error showing progress dialog: {ex.Message}", "Normalization Error");
            }
        }

        /// <summary>
        /// Delete all files in the PreservedOriginals folder
        /// </summary>
        public void DeletePreservedOriginals()
        {
            try
            {
                var preservedOriginalsDir = Path.Combine(
                    Path.Combine(_api.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName),
                    Constants.PreservedOriginalsFolderName);

                if (!Directory.Exists(preservedOriginalsDir))
                {
                    PlayniteApi.Dialogs.ShowMessage("PreservedOriginals folder does not exist or is empty.", "No Files to Delete");
                    return;
                }

                // Count files before deletion
                var allFiles = Directory.GetFiles(preservedOriginalsDir, "*.*", SearchOption.AllDirectories);
                var fileCount = allFiles.Length;
                
                if (fileCount == 0)
                {
                    PlayniteApi.Dialogs.ShowMessage("PreservedOriginals folder is empty.", "No Files to Delete");
                    return;
                }

                // Delete all files and directories
                try
                {
                    Directory.Delete(preservedOriginalsDir, true);
                    Directory.CreateDirectory(preservedOriginalsDir); // Recreate empty directory
                    
                    logger.Info($"Deleted {fileCount} files from PreservedOriginals folder");
                    PlayniteApi.Dialogs.ShowMessage(
                        $"Successfully deleted {fileCount} preserved original file(s).\n\nDisk space has been freed.",
                        "Deletion Complete");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Error deleting PreservedOriginals folder contents");
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        $"Error deleting preserved originals: {ex.Message}",
                        "Deletion Error");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in DeletePreservedOriginals");
                PlayniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "Delete Preserved Originals Error");
            }
        }

        /// <summary>
        /// Restore original files from PreservedOriginals folder
        /// Deletes normalized files and moves originals back to game music folders
        /// </summary>
        public void RestoreNormalizedFiles()
        {
            try
            {
                if (_normalizationService == null)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage("Normalization service not available.", "UniPlaySong");
                    return;
                }

                // Get all normalized files (files with -normalized suffix or files in music folders that have backups)
                var allMusicFiles = new List<string>();
                foreach (var game in PlayniteApi.Database.Games)
                {
                    var gameFiles = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                    allMusicFiles.AddRange(gameFiles);
                }

                if (allMusicFiles.Count == 0)
                {
                    PlayniteApi.Dialogs.ShowMessage("No music files found in your library.", "No Files to Restore");
                    return;
                }

                // Show progress dialog
                ShowRestoreProgress(allMusicFiles, "Restoring Original Files");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in RestoreNormalizedFiles");
                PlayniteApi.Dialogs.ShowErrorMessage($"Error starting restore: {ex.Message}", "Restore Error");
            }
        }

        /// <summary>
        /// Show restoration progress dialog and execute restoration of original files
        /// </summary>
        private void ShowRestoreProgress(List<string> musicFiles, string title)
        {
            try
            {
                // Create progress dialog
                var progressDialog = new Views.NormalizationProgressDialog();

                // Create window
                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = true,
                    ShowCloseButton = true
                });

                window.Title = title;
                window.Width = 600;
                window.Height = 500;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.ShowInTaskbar = false;
                window.ResizeMode = ResizeMode.CanResize;
                window.Content = progressDialog;

                // Handle window closing
                window.Closing += (s, e) =>
                {
                    try
                    {
                        var mainWindow = PlayniteApi.Dialogs.GetCurrentAppWindow();
                        if (mainWindow != null)
                        {
                            mainWindow.Activate();
                            mainWindow.Focus();
                            mainWindow.Topmost = true;
                            mainWindow.Topmost = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, "Error returning focus during restore dialog close");
                    }
                };

                // Get normalization suffix from settings
                var suffix = _settings?.NormalizationSuffix ?? "-normalized";

                // Start deletion asynchronously
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var progress = new Progress<Models.NormalizationProgress>(p =>
                        {
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                progressDialog.ReportProgress(p);
                            }));
                        });

                        var result = await _normalizationService.RestoreFromBackupsAsync(
                            musicFiles,
                            suffix,
                            progress,
                            progressDialog.CancellationToken);

                        // Show completion message
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            var message = $"Deletion Complete!\n\n" +
                                        $"Total: {result.TotalFiles} normalized files\n" +
                                        $"Deleted: {result.SuccessCount}\n" +
                                        $"Failed: {result.FailureCount}";

                            if (result.FailedFiles.Count > 0)
                            {
                                message += $"\n\nFailed files:\n{string.Join("\n", result.FailedFiles.Take(5).Select(f => System.IO.Path.GetFileName(f)))}";
                                if (result.FailedFiles.Count > 5)
                                {
                                    message += $"\n... and {result.FailedFiles.Count - 5} more";
                                }
                            }

                            PlayniteApi.Dialogs.ShowMessage(message, "Deletion Complete");
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            PlayniteApi.Dialogs.ShowMessage("Restore was cancelled.", "Restore Cancelled");
                        }));
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error during restore");
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            PlayniteApi.Dialogs.ShowErrorMessage($"Error during restore: {ex.Message}", "Restore Error");
                        }));
                    }
                });

                // Show dialog
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing restore progress dialog");
                PlayniteApi.Dialogs.ShowErrorMessage($"Error showing progress dialog: {ex.Message}", "Restore Error");
            }
        }

        #endregion

        #region Settings & Menus

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return new UniPlaySongSettingsViewModel(this);
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new UniPlaySongSettingsView(new UniPlaySongSettingsViewModel(this));
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            if (_gameMenuHandler == null || args.Games.Count == 0)
                return Enumerable.Empty<GameMenuItem>();

            const string menuSection = Constants.MenuSectionName;
            const string downloadSection = menuSection + "|Download";
            var items = new List<GameMenuItem>();
            var games = args.Games.ToList();

            // Add controller-friendly options (accessible in fullscreen mode)
            items.Add(new GameMenuItem
            {
                Description = "Download Music (🎮 Mode)",
                MenuSection = menuSection,
                Action = _ => ShowControllerDownloadDialog(games.FirstOrDefault())
            });
            
            // Only show primary song options for single game selection
            if (games.Count == 1)
            {
                var singleGame = games.First();
                items.Add(new GameMenuItem
                {
                    Description = "Set Primary Song (🎮 Mode)",
                    MenuSection = menuSection,
                    Action = _ => ShowControllerSetPrimarySong(singleGame)
                });
                items.Add(new GameMenuItem
                {
                    Description = "Remove Primary Song (🎮 Mode)",
                    MenuSection = menuSection,
                    Action = _ => ShowControllerRemovePrimarySong(singleGame)
                });
                items.Add(new GameMenuItem
                {
                    Description = "Delete Songs (🎮 Mode)",
                    MenuSection = menuSection,
                    Action = _ => ShowControllerDeleteSongs(singleGame)
                });
                items.Add(new GameMenuItem
                {
                    Description = "Normalize Selected Music",
                    MenuSection = menuSection,
                    Action = _ => NormalizeSelectedGamesFullscreen(singleGame)
                });
            }

            // Multi-game selection: Show "Download All" options
            if (games.Count > 1)
            {
                // Download All submenu with source options
                items.Add(new GameMenuItem
                {
                    Description = $"Download All ({games.Count} games)",
                    MenuSection = downloadSection,
                    Action = _ => _gameMenuHandler.DownloadMusicForGames(games, Models.Source.KHInsider)
                });
                items.Add(new GameMenuItem
                {
                    Description = "KHInsider",
                    MenuSection = downloadSection,
                    Action = _ => _gameMenuHandler.DownloadMusicForGames(games, Models.Source.KHInsider)
                });
                items.Add(new GameMenuItem
                {
                    Description = "YouTube",
                    MenuSection = downloadSection,
                    Action = _ => _gameMenuHandler.DownloadMusicForGames(games, Models.Source.YouTube)
                });

                // Add retry option if there are failed downloads
                var failedCount = _gameMenuHandler.FailedDownloads.Count(fd => !fd.Resolved);
                if (failedCount > 0)
                {
                    items.Add(new GameMenuItem
                    {
                        Description = "-",
                        MenuSection = menuSection
                    });
                    items.Add(new GameMenuItem
                    {
                        Description = $"Retry Failed Downloads ({failedCount})",
                        MenuSection = menuSection,
                        Action = _ => _gameMenuHandler.RetryFailedDownloads()
                    });
                }

                return items;
            }

            // Single game selection: Original behavior
            var game = games[0];
            var songs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
            
            // All items under "UniPlaySong" parent menu
            items.Add(new GameMenuItem
            {
                Description = "Download Music...",
                MenuSection = menuSection,
                Action = _ => _gameMenuHandler.DownloadMusicForGame(game)
            });
            items.Add(new GameMenuItem
            {
                Description = "Open Music Folder",
                MenuSection = menuSection,
                Action = _ => _gameMenuHandler.OpenMusicFolder(game)
            });

            if (songs.Count > 0)
            {
                items.Add(new GameMenuItem 
                { 
                    Description = "-",
                    MenuSection = menuSection
                });
                items.Add(new GameMenuItem
                {
                    Description = "Set Primary Song...",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.SetPrimarySong(game)
                });
                items.Add(new GameMenuItem
                {
                    Description = "Clear Primary Song",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.ClearPrimarySong(game)
                });
            }

            return items;
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            if (_mainMenuHandler == null)
                return Enumerable.Empty<MainMenuItem>();

            var items = new List<MainMenuItem>
            {
                new MainMenuItem
                {
                    Description = "UniPlaySong Settings",
                    Action = _ => _mainMenuHandler.OpenSettings()
                }
            };

            // Add retry failed downloads option if there are any failed downloads
            if (_gameMenuHandler != null && _gameMenuHandler.FailedDownloads.Any(fd => !fd.Resolved))
            {
                items.Add(new MainMenuItem
                {
                    Description = $"Retry Failed Downloads ({_gameMenuHandler.FailedDownloads.Count(fd => !fd.Resolved)})",
                    Action = _ => _gameMenuHandler.RetryFailedDownloads()
                });
            }

            return items;
        }

        #endregion

        #region Public API

        public IDownloadManager GetDownloadManager() => _downloadManager;
        public IMusicPlaybackService GetPlaybackService() => _playbackService;
        public GameMusicFileService GetFileService() => _fileService;
        public DownloadDialogService GetDownloadDialogService() => _downloadDialogService;
        public SettingsService GetSettingsService() => _settingsService;
        public SearchCacheService GetSearchCacheService() => _cacheService;
        
        public ErrorHandlerService GetErrorHandlerService() => _errorHandler;
        public Services.INormalizationService GetNormalizationService() => _normalizationService;
        public new IPlayniteAPI PlayniteApi => _api;

        #endregion

        #region Controller Dialogs

        /// <summary>
        /// Show controller-friendly file picker for setting primary song
        /// </summary>
        private void ShowControllerSetPrimarySong(Game game)
        {
            try
            {
                logger.Debug($"ShowControllerSetPrimarySong called for game: {game?.Name}");

                // Create controller file picker dialog
                var filePickerDialog = new Views.ControllerFilePickerDialog();

                // Create window using Playnite's dialog system
                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = true,
                    ShowCloseButton = true
                });

                window.Title = $"Set Primary Song - {game?.Name ?? "Unknown Game"}";
                window.Width = 700;
                window.Height = 500;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.ShowInTaskbar = false;
                window.ResizeMode = ResizeMode.CanResize;
                window.Content = filePickerDialog;

                // Initialize the dialog
                filePickerDialog.InitializeForGame(game, PlayniteApi, _fileService, _playbackService, Views.ControllerFilePickerDialog.DialogMode.SetPrimary);

                // Handle window events for focus management
                window.Closing += (s, e) =>
                {
                    try
                    {
                        var mainWindow = PlayniteApi.Dialogs.GetCurrentAppWindow();
                        if (mainWindow != null)
                        {
                            mainWindow.Activate();
                            mainWindow.Focus();
                            mainWindow.Topmost = true;
                            mainWindow.Topmost = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, "Error returning focus during set primary dialog close");
                    }
                };

                // Show dialog
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing controller set primary song dialog");
                PlayniteApi.Dialogs.ShowErrorMessage("Failed to open primary song selector.", "UniPlaySong");
            }
        }

        /// <summary>
        /// Show controller-friendly dialog for removing primary song
        /// </summary>
        private void ShowControllerRemovePrimarySong(Game game)
        {
            try
            {
                logger.Debug($"ShowControllerRemovePrimarySong called for game: {game?.Name}");

                // Check if there is a primary song to remove
                var currentPrimary = _fileService?.GetPrimarySong(game);
                if (string.IsNullOrEmpty(currentPrimary))
                {
                    PlayniteApi.Dialogs.ShowMessage("No primary song is currently set for this game.", "UniPlaySong");
                    return;
                }

                // Create controller file picker dialog
                var filePickerDialog = new Views.ControllerFilePickerDialog();

                // Create window using Playnite's dialog system
                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = true,
                    ShowCloseButton = true
                });

                window.Title = $"Remove Primary Song - {game?.Name ?? "Unknown Game"}";
                window.Width = 700;
                window.Height = 500;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.ShowInTaskbar = false;
                window.ResizeMode = ResizeMode.CanResize;
                window.Content = filePickerDialog;

                // Initialize the dialog
                filePickerDialog.InitializeForGame(game, PlayniteApi, _fileService, _playbackService, Views.ControllerFilePickerDialog.DialogMode.RemovePrimary);

                // Handle window events for focus management
                window.Closing += (s, e) =>
                {
                    try
                    {
                        var mainWindow = PlayniteApi.Dialogs.GetCurrentAppWindow();
                        if (mainWindow != null)
                        {
                            mainWindow.Activate();
                            mainWindow.Focus();
                            mainWindow.Topmost = true;
                            mainWindow.Topmost = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, "Error returning focus during remove primary dialog close");
                    }
                };

                // Show dialog
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing controller remove primary song dialog");
                PlayniteApi.Dialogs.ShowErrorMessage("Failed to open primary song remover.", "UniPlaySong");
            }
        }

        /// <summary>
        /// Show controller-friendly dialog for deleting songs
        /// </summary>
        private void ShowControllerDeleteSongs(Game game)
        {
            try
            {
                logger.Debug($"ShowControllerDeleteSongs called for game: {game?.Name}");

                // Check if there are any songs to delete
                var availableSongs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                if (availableSongs.Count == 0)
                {
                    PlayniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                    return;
                }

                // Create controller delete songs dialog
                var deleteSongsDialog = new Views.ControllerDeleteSongsDialog();

                // Create window using Playnite's dialog system
                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = true,
                    ShowCloseButton = true
                });

                window.Title = $"Delete Songs - {game?.Name ?? "Unknown Game"}";
                window.Width = 750;
                window.Height = 550;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.ShowInTaskbar = false;
                window.ResizeMode = ResizeMode.CanResize;
                window.Content = deleteSongsDialog;

                // Initialize the dialog
                deleteSongsDialog.InitializeForGame(game, PlayniteApi, _fileService, _playbackService);

                // Handle window events for focus management
                window.Closing += (s, e) =>
                {
                    try
                    {
                        var mainWindow = PlayniteApi.Dialogs.GetCurrentAppWindow();
                        if (mainWindow != null)
                        {
                            mainWindow.Activate();
                            mainWindow.Focus();
                            mainWindow.Topmost = true;
                            mainWindow.Topmost = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, "Error returning focus during delete songs dialog close");
                    }
                };

                // Show dialog
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing controller delete songs dialog");
                PlayniteApi.Dialogs.ShowErrorMessage("Failed to open song deletion dialog.", "UniPlaySong");
            }
        }

        /// <summary>
        /// Show the controller-optimized download dialog for a specific game
        /// </summary>
        public void ShowControllerDownloadDialog(Game game)
        {
            try
            {
                logger.Info($"Opening controller download dialog for game: {game?.Name}");
                
                // Create a window for the controller dialog using Playnite's dialog system
                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = true,
                    ShowCloseButton = true
                });

                window.Title = $"Download Music - {game?.Name ?? "Unknown Game"}";
                window.Width = 800;
                window.Height = 600;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.ShowInTaskbar = false;
                window.ResizeMode = ResizeMode.CanResize;

                // Set background color for fullscreen compatibility
                window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 33, 33));

                // Create and set the controller dialog
                var controllerDialog = new Views.SimpleControllerDialog();
                window.Content = controllerDialog;

                // Initialize the dialog with real download functionality
                controllerDialog.InitializeForGame(game, _downloadDialogService, PlayniteApi, _downloadManager, _playbackService, _fileService);

                // Ensure the window stays in fullscreen context
                window.Focusable = true;
                window.KeyDown += (s, e) => controllerDialog.Focus();

                // Show the dialog and ensure it gets focus
                window.Loaded += (s, e) => 
                {
                    window.Activate();
                    window.Focus();
                    controllerDialog.Focus();
                };

                // Handle window closing to prevent focus loss and dark overlay
                window.Closing += (s, e) =>
                {
                    try
                    {
                        logger.Debug("Controller dialog window closing");
                        
                        // Force focus return to prevent dark overlay
                        var mainWindow = PlayniteApi.Dialogs.GetCurrentAppWindow();
                        if (mainWindow != null)
                        {
                            logger.Debug("Forcing focus return to main window");
                            mainWindow.Activate();
                            mainWindow.Focus();
                            mainWindow.Topmost = true;
                            mainWindow.Topmost = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, "Error returning focus to main window during close");
                    }
                };

                window.Closed += (s, e) =>
                {
                    try
                    {
                        logger.Debug("Controller dialog window closed");
                        
                        // Additional focus restoration after window is fully closed
                        Task.Delay(50).ContinueWith(_ =>
                        {
                            try
                            {
                                var mainWindow = PlayniteApi.Dialogs.GetCurrentAppWindow();
                                if (mainWindow != null)
                                {
                                    mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        mainWindow.Activate();
                                        mainWindow.Focus();
                                    }));
                                }
                            }
                            catch (Exception delayEx)
                            {
                                logger.Debug(delayEx, "Error in delayed focus restoration");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, "Error in window closed handler");
                    }
                };
                
                var result = window.ShowDialog();
                
                logger.Info($"Controller download dialog completed with result: {result}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing controller download dialog");
                PlayniteApi.Dialogs.ShowErrorMessage($"Error showing download dialog: {ex.Message}", "Download Dialog Error");
            }
        }

        #endregion

        #region Xbox Controller Login Bypass Support

        // XInput API definitions (same as in SimpleControllerDialog)
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_1_4(int dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_1_3(int dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_9_1_0(int dwUserIndex, ref XINPUT_STATE pState);

        private static int XInputGetState(int dwUserIndex, ref XINPUT_STATE pState)
        {
            try
            {
                return XInputGetState_1_4(dwUserIndex, ref pState);
            }
            catch
            {
                try
                {
                    return XInputGetState_1_3(dwUserIndex, ref pState);
                }
                catch
                {
                    try
                    {
                        return XInputGetState_9_1_0(dwUserIndex, ref pState);
                    }
                    catch
                    {
                        return -1; // No XInput available
                    }
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        // Button constants
        private const ushort XINPUT_GAMEPAD_A = 0x1000;
        private const ushort XINPUT_GAMEPAD_START = 0x0010;

        private XINPUT_STATE _lastControllerLoginState;
        private bool _hasLastLoginState = false;

        /// <summary>
        /// Start monitoring Xbox controller for login bypass
        /// </summary>
        private void StartControllerLoginMonitoring()
        {
            if (_isControllerLoginMonitoring) return;

            try
            {
                _isControllerLoginMonitoring = true;
                _controllerLoginMonitoringCancellation = new CancellationTokenSource();
                
                Task.Run(async () =>
                {
                    _fileLogger?.Info("Starting Xbox controller login monitoring");
                    
                    while (!_controllerLoginMonitoringCancellation.Token.IsCancellationRequested)
                    {
                        try
                        {
                            // Check controller 0 (first controller)
                            XINPUT_STATE state = new XINPUT_STATE();
                            int result = XInputGetState(0, ref state);
                            
                            if (result == 0) // Success
                            {
                                // Check for button presses (only on state change)
                                if (_hasLastLoginState && state.dwPacketNumber != _lastControllerLoginState.dwPacketNumber)
                                {
                                    CheckLoginBypassButtonPresses(state.Gamepad, _lastControllerLoginState.Gamepad);
                                }
                                
                                _lastControllerLoginState = state;
                                _hasLastLoginState = true;
                            }
                            
                            await Task.Delay(50, _controllerLoginMonitoringCancellation.Token); // Check every 50ms
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _fileLogger?.Debug($"Error in controller login monitoring loop: {ex.Message}");
                            await Task.Delay(1000, _controllerLoginMonitoringCancellation.Token); // Wait longer on error
                        }
                    }
                    
                    _fileLogger?.Info("Xbox controller login monitoring stopped");
                }, _controllerLoginMonitoringCancellation.Token);
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Failed to start controller login monitoring: {ex.Message}", ex);
                _isControllerLoginMonitoring = false;
            }
        }

        /// <summary>
        /// Stop monitoring Xbox controller for login bypass
        /// </summary>
        private void StopControllerLoginMonitoring()
        {
            try
            {
                _isControllerLoginMonitoring = false;
                _controllerLoginMonitoringCancellation?.Cancel();
                _controllerLoginMonitoringCancellation?.Dispose();
                _controllerLoginMonitoringCancellation = null;
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Error stopping controller login monitoring: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Check for login bypass button presses (A button or Start button)
        /// </summary>
        private void CheckLoginBypassButtonPresses(XINPUT_GAMEPAD currentState, XINPUT_GAMEPAD lastState)
        {
            try
            {
                // Get newly pressed buttons (buttons that are pressed now but weren't before)
                ushort newlyPressed = (ushort)(currentState.wButtons & ~lastState.wButtons);
                
                if (newlyPressed == 0) return; // No new button presses
                
                // Check for A button or Start button (common login bypass buttons)
                if ((newlyPressed & XINPUT_GAMEPAD_A) != 0 || (newlyPressed & XINPUT_GAMEPAD_START) != 0)
                {
                    string buttonName = (newlyPressed & XINPUT_GAMEPAD_A) != 0 ? "A" : "Start";
                    _fileLogger?.Info($"Xbox controller {buttonName} button pressed - triggering login dismiss");
                    
                    // Trigger login dismiss on UI thread
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            _coordinator.HandleLoginDismiss();
                        }
                        catch (Exception ex)
                        {
                            _fileLogger?.Error($"Error handling controller login dismiss: {ex.Message}", ex);
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Error checking login bypass button presses: {ex.Message}", ex);
            }
        }

        #endregion
    }
}
