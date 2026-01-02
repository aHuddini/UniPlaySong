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
        
        private IMusicPlaybackService _playbackService;
        private IDownloadManager _downloadManager;
        private GameMusicFileService _fileService;
        private ErrorHandlerService _errorHandler;
        private GameMenuHandler _gameMenuHandler;
        private MainMenuHandler _mainMenuHandler;
        private Handlers.ControllerDialogHandler _controllerDialogHandler;
        private Handlers.NormalizationDialogHandler _normalizationDialogHandler;
        private Handlers.TrimDialogHandler _trimDialogHandler;
        private Handlers.WaveformTrimDialogHandler _waveformTrimDialogHandler;
        private Services.IWaveformTrimService _waveformTrimService;
        private DownloadDialogService _downloadDialogService;
        private SettingsService _settingsService;
        private SearchCacheService _cacheService;
        private Services.INormalizationService _normalizationService;
        private Services.ITrimService _trimService;
        private Services.AudioRepairService _repairService;
        private Services.MigrationService _migrationService;
        private IMusicPlaybackCoordinator _coordinator;
        
        private UniPlaySongSettings _settings => _settingsService?.Current;
        
        private readonly HttpClient _httpClient;
        private readonly HtmlWeb _htmlWeb;

        public override Guid Id { get; } = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        
        private IEnumerable<Game> SelectedGames => _api.MainView.SelectedGames;
        private bool IsFullscreen => _api.ApplicationInfo.Mode == ApplicationMode.Fullscreen;
        private bool IsDesktop => _api.ApplicationInfo.Mode == ApplicationMode.Desktop;

        public UniPlaySong(IPlayniteAPI api) : base(api)
        {
            _api = api;
            _httpClient = new HttpClient();
            _htmlWeb = new HtmlWeb();

            try
            {
                var extensionPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                _fileLogger = new FileLogger(extensionPath);
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                _fileLogger.Info($"=== UniPlaySong v{version} Starting ===");
            }
            catch
            {
                // File logger initialization failed - continue without it
            }

            Properties = new GenericPluginProperties { HasSettings = true };

            _settingsService = new SettingsService(_api, logger, _fileLogger, this);
            _settingsService.SettingsChanged += OnSettingsServiceChanged;

            // Wire up debug logging setting to FileLogger (both instance and global static)
            // This allows DEBUG-level logs to be conditional based on user preference
            if (_settings != null)
            {
                // Set the global static so all classes can check it
                FileLogger.GlobalDebugEnabled = () => _settings.EnableDebugLogging;

                // Also set the instance property if FileLogger was initialized
                if (_fileLogger != null)
                {
                    _fileLogger.IsDebugEnabled = () => _settings.EnableDebugLogging;
                }
            }

            InitializeServices();
            InitializeMenuHandlers();
            
            WindowMonitor.Attach(_playbackService, _errorHandler);
            
            if (_settings != null)
            {
                _settings.PropertyChanged += OnSettingsChanged;
                
                if (_settings.PauseOnTrailer)
                {
                    MediaElementsMonitor.Attach(_api, _settings);
                }
            }
            
            _settingsService.SettingPropertyChanged += OnSettingsServicePropertyChanged;
            SubscribeToMainModel();
            
            // Try early native music suppression in fullscreen mode
            // Catches native music that starts before OnApplicationStarted
            if (IsFullscreen)
            {
                bool shouldSuppress = _settings?.SuppressPlayniteBackgroundMusic == true ||
                                     (_settings?.EnableDefaultMusic == true && _settings?.UseNativeMusicAsDefault == true);
                
                if (shouldSuppress)
                {
                    try
                    {
                        SuppressNativeMusic();
                    }
                    catch
                    {
                        // Audio system may not be initialized yet - ignore
                    }
                }
            }

            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            logger.Info($"UniPlaySong v{assemblyVersion} loaded");
        }

        #region Playnite Events

        /// <summary>
        /// Handles game selection events from Playnite.
        /// Delegates all logic to the music playback coordinator.
        /// </summary>
        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            var game = args?.NewValue?.FirstOrDefault();
            _coordinator.HandleGameSelected(game, IsFullscreen);
        }

        /// <summary>
        /// Handles application startup events from Playnite.
        /// Initializes skip state, login detection, and native music suppression.
        /// </summary>
        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            _fileLogger?.Info($"Application started - Mode: {_api.ApplicationInfo.Mode}");

            // Register plugin instance for access from dialogs and services
            Application.Current.Properties["UniPlaySongPlugin"] = this;

            if (_settings == null)
            {
                _fileLogger?.Warn("OnApplicationStarted: Settings are null, reloading...");
                _settingsService?.LoadSettings();
            }
            
            if (IsFullscreen && _settings?.SkipFirstSelectionAfterModeSwitch == true)
            {
                _coordinator.ResetSkipStateForModeSwitch();
            }
            
            if (_settings?.ThemeCompatibleSilentSkip == true && IsFullscreen)
            {
                AttachLoginInputHandler();
            }
            
            // Suppress native music if:
            // 1. SuppressPlayniteBackgroundMusic is explicitly enabled, OR
            // 2. UseNativeMusicAsDefault is enabled (we play the same file, suppress duplicate)
            bool shouldSuppressOnStartup = _settings?.SuppressPlayniteBackgroundMusic == true ||
                                          (_settings?.EnableDefaultMusic == true && _settings?.UseNativeMusicAsDefault == true);
            
            if (IsFullscreen && shouldSuppressOnStartup)
            {
                _fileLogger?.Info($"OnApplicationStarted: Starting native music suppression (SuppressPlayniteBackgroundMusic={_settings?.SuppressPlayniteBackgroundMusic}, UseNativeMusicAsDefault={_settings?.UseNativeMusicAsDefault})");
                SuppressNativeMusic();
                StartNativeMusicSuppression();
            }

            // Subscribe to application focus/minimize events for PauseOnDeactivate feature
            Application.Current.Deactivated += OnApplicationDeactivate;
            Application.Current.Activated += OnApplicationActivate;
            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.StateChanged += OnWindowStateChanged;
            }
        }

        /// <summary>
        /// Handles library update events from Playnite.
        /// Triggers auto-download of music for newly added games if enabled.
        /// </summary>
        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            try
            {
                _fileLogger?.Info($"OnLibraryUpdated: Library updated event received");

                if (_settings == null)
                {
                    _fileLogger?.Warn("OnLibraryUpdated: Settings is null");
                    return;
                }

                if (!_settings.AutoDownloadOnLibraryUpdate)
                {
                    _fileLogger?.Debug("OnLibraryUpdated: Auto-download disabled, skipping");
                    // Still update timestamp to avoid processing old games later
                    _settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
                    SavePluginSettings(_settings);
                    return;
                }

                // Get games added since last auto-download check
                var lastCheck = _settings.LastAutoLibUpdateAssetsDownload;
                _fileLogger?.Info($"OnLibraryUpdated: Checking for games added after {lastCheck}");

                var newGames = _api.Database.Games
                    .Where(g => g.Added.HasValue && g.Added.Value > lastCheck)
                    .ToList();

                _fileLogger?.Info($"OnLibraryUpdated: Found {newGames.Count} new game(s)");

                // Start auto-download in background if we have new games
                if (newGames.Count > 0)
                {
                    Task.Run(() => AutoDownloadMusicForGamesAsync(newGames));
                }

                // Update timestamp AFTER identifying games (like PlayniteSound does)
                _settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
                SavePluginSettings(_settings);
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"OnLibraryUpdated: Error - {ex.Message}", ex);
                logger.Error(ex, "Error in OnLibraryUpdated");
            }
        }

        /// <summary>
        /// Automatically downloads music for a list of games.
        /// Uses BestAlbumPick and BestSongPick to select the most relevant music.
        /// </summary>
        private async Task AutoDownloadMusicForGamesAsync(List<Game> games)
        {
            if (games == null || games.Count == 0)
                return;

            _fileLogger?.Info($"AutoDownloadMusicForGamesAsync: Starting auto-download for {games.Count} game(s)");

            var successCount = 0;
            var skipCount = 0;
            var failCount = 0;

            foreach (var game in games)
            {
                try
                {
                    // Check if game already has music
                    if (_fileService.HasMusic(game))
                    {
                        _fileLogger?.Debug($"AutoDownload: Skipping '{game.Name}' - already has music");
                        skipCount++;
                        continue;
                    }

                    var success = await AutoDownloadMusicForGameAsync(game);
                    if (success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }

                    // Rate limiting: wait between downloads to avoid throttling
                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    _fileLogger?.Error($"AutoDownload: Error processing '{game.Name}' - {ex.Message}", ex);
                    failCount++;
                }
            }

            _fileLogger?.Info($"AutoDownloadMusicForGamesAsync: Completed - Success: {successCount}, Skipped: {skipCount}, Failed: {failCount}");

            // Show notification with results
            if (successCount > 0 || failCount > 0)
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    var message = $"Auto-download completed:\n" +
                                  $"• Downloaded: {successCount}\n" +
                                  $"• Skipped (already have music): {skipCount}\n" +
                                  $"• Failed: {failCount}";
                    _api.Notifications.Add(new NotificationMessage(
                        "UniPlaySong_AutoDownload",
                        message,
                        NotificationType.Info));
                }));
            }
        }

        /// <summary>
        /// Automatically downloads music for a single game.
        /// Returns true if music was successfully downloaded.
        /// </summary>
        private async Task<bool> AutoDownloadMusicForGameAsync(Game game)
        {
            try
            {
                _fileLogger?.Debug($"AutoDownload: Processing '{game.Name}'");

                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
                {
                    // Get albums for this game (try KHInsider first, fallback to YouTube)
                    var albums = _downloadManager.GetAlbumsForGame(game.Name, Models.Source.All, cts.Token, auto: true);
                    if (albums == null || !albums.Any())
                    {
                        _fileLogger?.Debug($"AutoDownload: No albums found for '{game.Name}'");
                        return false;
                    }

                    // Use BestAlbumPick to select the best album
                    var bestAlbum = _downloadManager.BestAlbumPick(albums, game);
                    if (bestAlbum == null)
                    {
                        _fileLogger?.Debug($"AutoDownload: No suitable album found for '{game.Name}'");
                        return false;
                    }

                    _fileLogger?.Debug($"AutoDownload: Selected album '{bestAlbum.Name}' for '{game.Name}'");

                    // Get songs from the album
                    var songs = _downloadManager.GetSongsFromAlbum(bestAlbum, cts.Token);
                    if (songs == null || !songs.Any())
                    {
                        _fileLogger?.Debug($"AutoDownload: No songs found in album '{bestAlbum.Name}'");
                        return false;
                    }

                    // Use BestSongPick to select the best song (only 1 song for auto-download)
                    var bestSongs = _downloadManager.BestSongPick(songs, game.Name, maxSongs: 1);
                    if (bestSongs == null || bestSongs.Count == 0)
                    {
                        _fileLogger?.Debug($"AutoDownload: No suitable song found for '{game.Name}'");
                        return false;
                    }

                    var bestSong = bestSongs.First();
                    _fileLogger?.Debug($"AutoDownload: Selected song '{bestSong.Name}' for '{game.Name}'");

                    // Ensure game music directory exists
                    var gameDir = _fileService.EnsureGameMusicDirectory(game);
                    if (string.IsNullOrWhiteSpace(gameDir))
                    {
                        _fileLogger?.Error($"AutoDownload: Failed to create music directory for '{game.Name}'");
                        return false;
                    }

                    // Generate safe filename
                    var safeFileName = Common.StringHelper.CleanForPath(bestSong.Name);
                    var extension = Path.GetExtension(bestSong.Id);
                    if (string.IsNullOrEmpty(extension))
                        extension = ".mp3";
                    var downloadPath = Path.Combine(gameDir, safeFileName + extension);

                    // Download the song
                    var success = _downloadManager.DownloadSong(bestSong, downloadPath, cts.Token, isPreview: false);
                    if (success)
                    {
                        _fileLogger?.Info($"AutoDownload: Successfully downloaded '{bestSong.Name}' for '{game.Name}'");

                        // Auto-normalize if enabled
                        if (_settings?.AutoNormalizeAfterDownload == true && _normalizationService != null)
                        {
                            try
                            {
                                var normSettings = new Models.NormalizationSettings
                                {
                                    FFmpegPath = _settings.FFmpegPath,
                                    TargetLoudness = _settings.NormalizationTargetLoudness,
                                    TruePeak = _settings.NormalizationTruePeak,
                                    LoudnessRange = _settings.NormalizationLoudnessRange,
                                    AudioCodec = _settings.NormalizationCodec,
                                    NormalizationSuffix = _settings.NormalizationSuffix,
                                    TrimSuffix = _settings.TrimSuffix,
                                    DoNotPreserveOriginals = _settings.DoNotPreserveOriginals
                                };
                                await _normalizationService.NormalizeFileAsync(
                                    downloadPath,
                                    normSettings,
                                    null,
                                    CancellationToken.None);
                                _fileLogger?.Debug($"AutoDownload: Normalized '{bestSong.Name}'");
                            }
                            catch (Exception ex)
                            {
                                _fileLogger?.Warn($"AutoDownload: Normalization failed for '{bestSong.Name}' - {ex.Message}");
                            }
                        }

                        return true;
                    }
                    else
                    {
                        _fileLogger?.Warn($"AutoDownload: Download failed for '{bestSong.Name}'");
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _fileLogger?.Warn($"AutoDownload: Timeout for '{game.Name}'");
                return false;
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"AutoDownload: Error for '{game.Name}' - {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Handles application shutdown events from Playnite.
        /// Cleans up resources and stops all monitoring systems.
        /// </summary>
        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            _playbackService?.Stop();
            _downloadManager?.Cleanup();
            _httpClient?.Dispose();

            // Unsubscribe from focus/minimize events
            Application.Current.Deactivated -= OnApplicationDeactivate;
            Application.Current.Activated -= OnApplicationActivate;
            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.StateChanged -= OnWindowStateChanged;
            }

            // Remove plugin instance registration
            if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
            {
                Application.Current.Properties.Remove("UniPlaySongPlugin");
            }

            StopControllerLoginMonitoring();
            StopNativeMusicSuppression();

            _fileLogger?.Info("Application stopped");
        }

        #endregion

        #region Music Playback

        /// <summary>
        /// Determines if music should play for the currently selected game.
        /// Delegates to the coordinator for all playback decisions.
        /// </summary>
        private bool ShouldPlayMusic()
        {
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
        /// Handles SettingsService property change events.
        /// Forwards video state changes to the coordinator.
        /// </summary>
        private void OnSettingsServicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UniPlaySongSettings.VideoIsPlaying))
            {
                _coordinator?.HandleVideoStateChange(_settings.VideoIsPlaying);
            }
        }

        /// <summary>
        /// Handles SettingsService settings changed events.
        /// Manages plugin-level concerns: volume updates, media monitor, and PropertyChanged subscriptions.
        /// Coordinator subscribes directly to SettingsService, so no manual UpdateSettings() needed.
        /// </summary>
        private void OnSettingsServiceChanged(object sender, SettingsChangedEventArgs e)
        {
            try
            {
                // Coordinator subscribes directly to SettingsService - no manual update needed

                // Check if MusicState or EnableMusic changed - if so, re-evaluate playback
                bool musicSettingsChanged = e.OldSettings != null && e.NewSettings != null &&
                    (e.OldSettings.MusicState != e.NewSettings.MusicState ||
                     e.OldSettings.EnableMusic != e.NewSettings.EnableMusic);

                if (musicSettingsChanged)
                {
                    var game = SelectedGames?.FirstOrDefault();
                    if (game != null && _coordinator != null)
                    {
                        // Re-evaluate if music should be playing with new settings
                        if (!_coordinator.ShouldPlayMusic(game))
                        {
                            _fileLogger?.Debug($"OnSettingsServiceChanged: MusicState/EnableMusic changed - stopping music (State: {e.NewSettings.MusicState}, Enable: {e.NewSettings.EnableMusic})");
                            _playbackService?.Stop();
                        }
                    }
                    else if (e.NewSettings?.EnableMusic == false || e.NewSettings?.MusicState == AudioState.Never)
                    {
                        // No game selected but music disabled - stop any default music
                        _fileLogger?.Debug("OnSettingsServiceChanged: Music disabled - stopping all playback");
                        _playbackService?.Stop();
                    }
                }

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
                
                // Recreate DownloadManager if download settings changed (so YouTubeDownloader gets new settings)
                if (e.OldSettings != null && e.NewSettings != null)
                {
                    bool downloadSettingsChanged = 
                        e.OldSettings.YtDlpPath != e.NewSettings.YtDlpPath ||
                        e.OldSettings.FFmpegPath != e.NewSettings.FFmpegPath ||
                        e.OldSettings.UseFirefoxCookies != e.NewSettings.UseFirefoxCookies;
                    
                    if (downloadSettingsChanged && _downloadManager != null)
                    {
                        var tempPath = Path.Combine(_api.Paths.ExtensionsDataPath, "UniPlaySong", "temp");
                        _downloadManager = new DownloadManager(
                            _httpClient, _htmlWeb, tempPath,
                            e.NewSettings?.YtDlpPath, e.NewSettings?.FFmpegPath, _errorHandler, _cacheService, e.NewSettings);
                        logger.Info("DownloadManager recreated with updated download settings");
                    }
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
        /// Called when settings are saved in the settings UI.
        /// Reloads settings via SettingsService, which automatically notifies all subscribers.
        /// </summary>
        public void OnSettingsSaved()
        {
            try
            {
                // Reload settings from disk via SettingsService
                // SettingsService will automatically notify all subscribers via SettingsChanged event
                _settingsService.LoadSettings();

                // Immediately check if music should be stopped based on new settings
                // This handles the case where MusicState was changed to Never or a mode-specific setting
                var currentSettings = _settingsService.Current;
                if (currentSettings != null && _coordinator != null)
                {
                    var game = SelectedGames?.FirstOrDefault();
                    bool shouldStop = false;

                    // Check if music should be completely disabled
                    if (!currentSettings.EnableMusic || currentSettings.MusicState == AudioState.Never)
                    {
                        shouldStop = true;
                    }
                    // Check mode-specific settings
                    else if (IsDesktop && currentSettings.MusicState == AudioState.Fullscreen)
                    {
                        shouldStop = true;
                    }
                    else if (IsFullscreen && currentSettings.MusicState == AudioState.Desktop)
                    {
                        shouldStop = true;
                    }

                    if (shouldStop)
                    {
                        _fileLogger?.Debug($"OnSettingsSaved: Stopping music immediately (State: {currentSettings.MusicState}, Mode: {(IsFullscreen ? "Fullscreen" : "Desktop")})");
                        _playbackService?.Stop();
                    }
                }

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

        /// <summary>
        /// Handles window state changes (minimize/restore).
        /// Pauses music when minimized if PauseOnMinimize is enabled.
        /// </summary>
        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            var windowState = Application.Current?.MainWindow?.WindowState;
            _fileLogger?.Debug($"OnWindowStateChanged - WindowState: {windowState}, PauseOnMinimize: {_settings?.PauseOnMinimize}");

            if (_settings?.PauseOnMinimize == true)
            {
                switch (windowState)
                {
                    case WindowState.Normal:
                    case WindowState.Maximized:
                        _fileLogger?.Debug("OnWindowStateChanged: Window restored - resuming music");
                        ResumeAfterPause();
                        break;
                    case WindowState.Minimized:
                        _fileLogger?.Debug("OnWindowStateChanged: Window minimized - pausing music");
                        _playbackService?.Pause();
                        break;
                }
            }
        }

        /// <summary>
        /// Handles application losing focus.
        /// Pauses music when Playnite loses focus if PauseOnFocusLoss is enabled.
        /// </summary>
        private void OnApplicationDeactivate(object sender, EventArgs e)
        {
            _fileLogger?.Debug($"OnApplicationDeactivate - PauseOnFocusLoss: {_settings?.PauseOnFocusLoss}");
            if (_settings?.PauseOnFocusLoss == true)
            {
                _fileLogger?.Debug("OnApplicationDeactivate: Pausing music");
                _playbackService?.Pause();
            }
        }

        /// <summary>
        /// Handles application gaining focus.
        /// Resumes music when Playnite gains focus if PauseOnFocusLoss is enabled.
        /// </summary>
        private void OnApplicationActivate(object sender, EventArgs e)
        {
            _fileLogger?.Debug($"OnApplicationActivate - PauseOnFocusLoss: {_settings?.PauseOnFocusLoss}");
            if (_settings?.PauseOnFocusLoss == true)
            {
                // Only resume if window is not minimized (minimize has its own resume logic)
                var windowState = Application.Current?.MainWindow?.WindowState;
                if (windowState != WindowState.Minimized)
                {
                    _fileLogger?.Debug("OnApplicationActivate: Resuming music");
                    ResumeAfterPause();
                }
            }
        }

        /// <summary>
        /// Resumes music after a pause (focus loss or minimize).
        /// Uses Resume() if music is loaded.
        /// </summary>
        private void ResumeAfterPause()
        {
            if (_playbackService == null) return;

            // If music is loaded, resume it
            if (_playbackService.IsLoaded)
            {
                _fileLogger?.Debug("ResumeAfterPause: Music is loaded - calling Resume()");
                _playbackService.Resume();
            }
            else
            {
                _fileLogger?.Debug("ResumeAfterPause: Music not loaded - nothing to resume");
            }
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

            // Wire up normalization service to download dialog service for auto-normalize feature
            _downloadDialogService.SetNormalizationService(_normalizationService);

            // Initialize trim service with playback service and base path for backups
            _trimService = new Services.AudioTrimService(_errorHandler, _playbackService, basePath);
            _fileLogger?.Info("AudioTrimService initialized");

            // Initialize waveform trim service for precise trimming
            _waveformTrimService = new Services.WaveformTrimService(_errorHandler, _playbackService, basePath);
            _fileLogger?.Info("WaveformTrimService initialized");

            // Initialize migration service for PlayniteSound <-> UniPlaySong migration
            _migrationService = new Services.MigrationService(_api, _errorHandler);
            _fileLogger?.Info("MigrationService initialized");

            // Initialize audio repair service for fixing problematic audio files
            _repairService = new Services.AudioRepairService(_errorHandler, _playbackService, basePath);
            _fileLogger?.Info("AudioRepairService initialized");
        }

        private void InitializeMenuHandlers()
        {
            _gameMenuHandler = new GameMenuHandler(
                _api, logger, _downloadManager, _fileService,
                _playbackService, _downloadDialogService, _errorHandler,
                _repairService, () => _settings);
            _mainMenuHandler = new MainMenuHandler(_api, Id);
            _controllerDialogHandler = new Handlers.ControllerDialogHandler(
                _api, _fileService, _playbackService, _downloadDialogService, _downloadManager);
            _normalizationDialogHandler = new Handlers.NormalizationDialogHandler(
                _api, _normalizationService, _playbackService, _fileService, () => _settings);
            _trimDialogHandler = new Handlers.TrimDialogHandler(
                _api, _trimService, _playbackService, _fileService, () => _settings);
            _waveformTrimDialogHandler = new Handlers.WaveformTrimDialogHandler(
                _api, () => _settings, _fileService, _playbackService, _waveformTrimService);
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
        /// Starts continuous native music suppression monitoring for initial fullscreen startup.
        /// Polls every 50ms for 15 seconds to catch native music that starts at different times.
        /// </summary>
        private void StartNativeMusicSuppression()
        {
            if (_isNativeMusicSuppressionActive)
                return;

            try
            {
                _isNativeMusicSuppressionActive = true;
                _hasLoggedSuppression = false;
                
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
                
                // Stop after 15 seconds to catch delayed audio initialization and theme transitions
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
        /// Stops continuous native music suppression monitoring.
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
        /// Uses reflection to access Playnite's internal BackgroundMusic property and SDL2_mixer to stop playback.
        /// </summary>
        /// <remarks>
        /// Triggers when:
        /// 1. SuppressPlayniteBackgroundMusic is explicitly enabled, OR
        /// 2. UseNativeMusicAsDefault is enabled (we play the same file ourselves)
        /// </remarks>
        private void SuppressNativeMusic()
        {
            if (!IsFullscreen)
                return;
            
            bool shouldSuppress = _settings?.SuppressPlayniteBackgroundMusic == true ||
                                 (_settings?.EnableDefaultMusic == true && _settings?.UseNativeMusicAsDefault == true);
            
            if (!shouldSuppress)
                return;

            try
            {
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
                    SDL2MixerWrapper.Mix_HaltMusic();
                    SDL2MixerWrapper.Mix_FreeMusic(currentMusic);
                    // Set to Zero to prevent Playnite from reloading
                    backgroundMusicProperty.GetSetMethod(true)?.Invoke(null, new object[] { IntPtr.Zero });
                    
                    if (!_hasLoggedSuppression)
                    {
                        _fileLogger?.Info("SuppressNativeMusic: Successfully stopped native background music");
                        _hasLoggedSuppression = true;
                    }
                }
                else
                {
                    // Music not loaded yet - set to Zero to prevent loading
                    backgroundMusicProperty.GetSetMethod(true)?.Invoke(null, new object[] { IntPtr.Zero });
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"SuppressNativeMusic: Exception - {ex.Message}");
                // Fallback to simple halt if reflection fails
                try
                {
                    SDL2MixerWrapper.Mix_HaltMusic();
                }
                catch { }
            }
        }

        #endregion

        #region Audio Normalization

        /// <summary>
        /// Normalize all music files in the library.
        /// Delegates to NormalizationDialogHandler.
        /// </summary>
        public void NormalizeAllMusicFiles()
        {
            _normalizationDialogHandler?.NormalizeAllMusicFiles();
        }

        /// <summary>
        /// Normalize music files for a single game (for fullscreen menu).
        /// Delegates to NormalizationDialogHandler.
        /// </summary>
        public void NormalizeSelectedGamesFullscreen(Game game)
        {
            _normalizationDialogHandler?.NormalizeSelectedGamesFullscreen(game);
        }

        /// <summary>
        /// Normalize music files for selected games.
        /// Delegates to NormalizationDialogHandler.
        /// </summary>
        public void NormalizeSelectedGames(List<Game> games, bool showSimpleConfirmation = false)
        {
            _normalizationDialogHandler?.NormalizeSelectedGames(games, showSimpleConfirmation);
        }

        /// <summary>
        /// Delete all files in the PreservedOriginals folder.
        /// Delegates to NormalizationDialogHandler.
        /// </summary>
        public void DeletePreservedOriginals()
        {
            _normalizationDialogHandler?.DeletePreservedOriginals();
        }

        /// <summary>
        /// Restore original files from PreservedOriginals folder.
        /// Delegates to NormalizationDialogHandler.
        /// </summary>
        public void RestoreNormalizedFiles()
        {
            _normalizationDialogHandler?.RestoreNormalizedFiles();
        }

        /// <summary>
        /// Normalize a single music file.
        /// Delegates to NormalizationDialogHandler.
        /// </summary>
        public void NormalizeSingleFile(Game game, string filePath)
        {
            _normalizationDialogHandler?.NormalizeSingleFile(game, filePath);
        }

        /// <summary>
        /// Trim a single music file.
        /// Delegates to TrimDialogHandler.
        /// </summary>
        public void TrimSingleFile(Game game, string filePath)
        {
            _trimDialogHandler?.TrimSingleFile(game, filePath);
        }

        #endregion

        #region Music Migration

        /// <summary>
        /// Run a migration operation with progress dialog
        /// </summary>
        public void RunMigrationWithProgress(
            string title,
            Func<IProgress<Services.MigrationProgress>, System.Threading.CancellationToken, System.Threading.Tasks.Task<Services.MigrationBatchResult>> migrationTask)
        {
            try
            {
                // Create progress dialog
                var progressDialog = new Views.MigrationProgressDialog();
                progressDialog.SetTitle(title);

                var window = Common.DialogHelper.CreateFixedDialog(
                    PlayniteApi,
                    title,
                    progressDialog,
                    width: 550,
                    height: 450);

                Common.DialogHelper.AddFocusReturnHandler(window, PlayniteApi, "migration dialog close");

                // Start migration asynchronously
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var progress = new Progress<Services.MigrationProgress>(p =>
                        {
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                progressDialog.ReportProgress(p);
                            }));
                        });

                        var result = await migrationTask(progress, progressDialog.CancellationToken);

                        // Report completion
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            progressDialog.ReportCompletion(result);

                            // Show summary message after a short delay
                            if (!result.WasCancelled)
                            {
                                var message = $"Migration Complete!\n\n" +
                                            $"Games processed: {result.TotalGames}\n" +
                                            $"Files copied: {result.TotalFilesCopied}\n" +
                                            $"Files skipped (already exist): {result.TotalFilesSkipped}\n" +
                                            $"Failed: {result.FailedGames}";

                                PlayniteApi.Dialogs.ShowMessage(message, "Migration Complete");
                            }
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            PlayniteApi.Dialogs.ShowMessage("Migration was cancelled.", "Migration Cancelled");
                        }));
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error during migration");
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            PlayniteApi.Dialogs.ShowErrorMessage($"Error during migration: {ex.Message}", "Migration Error");
                        }));
                    }
                });

                // Show dialog (blocks until closed)
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing migration progress dialog");
                PlayniteApi.Dialogs.ShowErrorMessage($"Error showing progress dialog: {ex.Message}", "Migration Error");
            }
        }

        #endregion

        #region Audio Trimming

        /// <summary>
        /// Trim leading silence from all music files in the library.
        /// Delegates to TrimDialogHandler.
        /// </summary>
        public void TrimAllMusicFiles()
        {
            _trimDialogHandler?.TrimAllMusicFiles();
        }

        /// <summary>
        /// Trim leading silence from music files for a single game (for fullscreen menu).
        /// Delegates to TrimDialogHandler.
        /// </summary>
        public void TrimSelectedGamesFullscreen(Game game)
        {
            _trimDialogHandler?.TrimSelectedGamesFullscreen(game);
        }

        /// <summary>
        /// Trim leading silence from music files for selected games.
        /// Delegates to TrimDialogHandler.
        /// </summary>
        public void TrimSelectedGames(List<Game> games, bool showSimpleConfirmation = false)
        {
            _trimDialogHandler?.TrimSelectedGames(games, showSimpleConfirmation);
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
            if (args.Games.Count == 0)
                return Enumerable.Empty<GameMenuItem>();

            const string menuSection = Constants.MenuSectionName;
            var items = new List<GameMenuItem>();
            var games = args.Games.ToList();

            // Multi-game selection: Show bulk operations
            if (games.Count > 1)
            {
                // Download All - uses Source.All (tries KHInsider first, falls back to YouTube)
                items.Add(new GameMenuItem
                {
                    Description = $"Download All ({games.Count} games)",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.DownloadMusicForGames(games, Models.Source.All)
                });

                // Normalize All - bulk normalize selected games
                items.Add(new GameMenuItem
                {
                    Description = $"Normalize All ({games.Count} games)",
                    MenuSection = menuSection,
                    Action = _ => _normalizationDialogHandler.NormalizeSelectedGames(games)
                });

                // Trim All - bulk trim selected games
                items.Add(new GameMenuItem
                {
                    Description = $"Trim All ({games.Count} games)",
                    MenuSection = menuSection,
                    Action = _ => _trimDialogHandler.TrimSelectedGames(games)
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

            // Single game selection
            var game = games[0];
            var songs = _fileService?.GetAvailableSongs(game) ?? new List<string>();

            if (IsFullscreen)
            {
                // === FULLSCREEN MODE: Organized with submenus for controller navigation ===
                var primarySongSection = $"{menuSection}|Primary Song";
                var audioProcessingSection = $"{menuSection}|Audio Processing";
                var manageMusicSection = $"{menuSection}|Manage Music";
                var pcModeSection = $"{menuSection}|🖥️ PC Mode";

                // Download Music (🎮 Mode) - at parent level for quick access
                items.Add(new GameMenuItem
                {
                    Description = "🎮 Download Music",
                    MenuSection = menuSection,
                    Action = _ => _controllerDialogHandler.ShowDownloadDialog(game)
                });

                // === Primary Song Submenu (🎮 Mode options) ===
                items.Add(new GameMenuItem
                {
                    Description = "🎮 Set Primary Song",
                    MenuSection = primarySongSection,
                    Action = _ => _controllerDialogHandler.ShowSetPrimarySong(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "🎮 Clear Primary Song",
                    MenuSection = primarySongSection,
                    Action = _ => _controllerDialogHandler.ClearPrimarySong(game)
                });

                // === Audio Processing Submenu ===
                items.Add(new GameMenuItem
                {
                    Description = "🎮 Normalize Single Song",
                    MenuSection = audioProcessingSection,
                    Action = _ => _controllerDialogHandler.ShowNormalizeIndividualSong(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "Normalize Music Folder",
                    MenuSection = audioProcessingSection,
                    Action = _ => NormalizeSelectedGamesFullscreen(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "🎮 Silence Trim - Single Song",
                    MenuSection = audioProcessingSection,
                    Action = _ => _controllerDialogHandler.ShowTrimIndividualSong(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "Silence Trim - Music Folder",
                    MenuSection = audioProcessingSection,
                    Action = _ => TrimSelectedGamesFullscreen(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "🎮 Precise Trim",
                    MenuSection = audioProcessingSection,
                    Action = _ => _waveformTrimDialogHandler.ShowControllerPreciseTrimDialog(game)
                });

                // === Manage Music Submenu (🎮 Mode options) ===
                items.Add(new GameMenuItem
                {
                    Description = "🎮 Delete Songs",
                    MenuSection = manageMusicSection,
                    Action = _ => _controllerDialogHandler.ShowDeleteSongs(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "Open Music Folder",
                    MenuSection = manageMusicSection,
                    Action = _ => _gameMenuHandler.OpenMusicFolder(game)
                });

                // === PC Mode Submenu (Desktop dialogs for keyboard/mouse users) ===
                items.Add(new GameMenuItem
                {
                    Description = "🖥️ Download Music",
                    MenuSection = pcModeSection,
                    Action = _ => _gameMenuHandler.DownloadMusicForGame(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "🖥️ Download From URL",
                    MenuSection = pcModeSection,
                    Action = _ => _gameMenuHandler.DownloadFromUrl(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "🖥️ Set Primary Song",
                    MenuSection = pcModeSection,
                    Action = _ => _gameMenuHandler.SetPrimarySong(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "🖥️Clear Primary Song",
                    MenuSection = pcModeSection,
                    Action = _ => _gameMenuHandler.ClearPrimarySong(game)
                });
            }
            else
            {
                // === DESKTOP MODE: Organized with submenus ===
                var audioProcessingSection = $"{menuSection}|Audio Processing";
                var audioEditingSection = $"{menuSection}|Audio Editing";
                var controllerModeSection = $"{menuSection}|🎮 Controller Mode";

                // Download Music - Desktop download dialog with source selection
                items.Add(new GameMenuItem
                {
                    Description = "Download Music",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.DownloadMusicForGame(game)
                });

                // Download From URL - Download audio from a specific YouTube URL
                items.Add(new GameMenuItem
                {
                    Description = "Download From URL",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.DownloadFromUrl(game)
                });

                // Separator before Primary Song section
                items.Add(new GameMenuItem
                {
                    Description = "-",
                    MenuSection = menuSection
                });

                // Set Primary Song - Desktop file picker
                items.Add(new GameMenuItem
                {
                    Description = "Set Primary Song",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.SetPrimarySong(game)
                });

                // Clear Primary Song - Desktop
                items.Add(new GameMenuItem
                {
                    Description = "Clear Primary Song",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.ClearPrimarySong(game)
                });

                // === Audio Processing Submenu (Normalize and Silence Trim options) ===
                items.Add(new GameMenuItem
                {
                    Description = "Normalize Single Song",
                    MenuSection = audioProcessingSection,
                    Action = _ => _gameMenuHandler.ShowNormalizeIndividualSong(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "Normalize Music Folder",
                    MenuSection = audioProcessingSection,
                    Action = _ => _normalizationDialogHandler.NormalizeSelectedGames(new List<Game> { game })
                });

                items.Add(new GameMenuItem
                {
                    Description = "Silence Trim - Single Song",
                    MenuSection = audioProcessingSection,
                    Action = _ => _gameMenuHandler.ShowTrimIndividualSong(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "Silence Trim - Music Folder",
                    MenuSection = audioProcessingSection,
                    Action = _ => _trimDialogHandler.TrimSelectedGames(new List<Game> { game })
                });

                // === Audio Editing Submenu (Precise Trim and Repair options) ===
                items.Add(new GameMenuItem
                {
                    Description = "Precise Trim",
                    MenuSection = audioEditingSection,
                    Action = _ => _waveformTrimDialogHandler.ShowPreciseTrimDialog(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "Repair Audio File",
                    MenuSection = audioEditingSection,
                    Action = _ => _gameMenuHandler.ShowRepairAudioFile(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "Repair Music Folder",
                    MenuSection = audioEditingSection,
                    Action = _ => _gameMenuHandler.RepairAllAudioFiles(game)
                });

                // Separator before utility options
                items.Add(new GameMenuItem
                {
                    Description = "-",
                    MenuSection = menuSection
                });

                // Open Music Folder
                items.Add(new GameMenuItem
                {
                    Description = "Open Music Folder",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.OpenMusicFolder(game)
                });

                // === Controller Mode Submenu (Controller-friendly dialogs) ===
                items.Add(new GameMenuItem
                {
                    Description = "🎮 Download Music",
                    MenuSection = controllerModeSection,
                    Action = _ => _controllerDialogHandler.ShowDownloadDialog(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "🎮 Set Primary Song",
                    MenuSection = controllerModeSection,
                    Action = _ => _controllerDialogHandler.ShowSetPrimarySong(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "🎮 Clear Primary Song",
                    MenuSection = controllerModeSection,
                    Action = _ => _controllerDialogHandler.ClearPrimarySong(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "🎮 Delete Songs",
                    MenuSection = controllerModeSection,
                    Action = _ => _controllerDialogHandler.ShowDeleteSongs(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "🎮 Precise Trim",
                    MenuSection = controllerModeSection,
                    Action = _ => _waveformTrimDialogHandler.ShowControllerPreciseTrimDialog(game)
                });
            }

            return items;
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            var items = new List<MainMenuItem>
            {
                new MainMenuItem
                {
                    Description = "UniPlaySong Settings",
                    Action = _ => _mainMenuHandler.OpenSettings()
                }
            };

            // Add retry failed downloads option if there are any failed downloads
            if (_gameMenuHandler.FailedDownloads.Any(fd => !fd.Resolved))
            {
                items.Add(new MainMenuItem
                {
                    Description = $"Retry Failed Downloads ({_gameMenuHandler.FailedDownloads.Count(fd => !fd.Resolved)})",
                    Action = _ => _gameMenuHandler.RetryFailedDownloads()
                });
            }

            return items;
        }

        /// <summary>
        /// Downloads music for all games that don't have music yet.
        /// Triggered from settings Downloads tab.
        /// Shows a non-blocking progress dialog with cancellation support.
        /// </summary>
        public void DownloadMusicForAllGamesFromSettings()
        {
            try
            {
                // Get all games without music
                var gamesWithoutMusic = _api.Database.Games
                    .Where(g => !_fileService.HasMusic(g))
                    .ToList();

                if (gamesWithoutMusic.Count == 0)
                {
                    _api.Dialogs.ShowMessage(
                        "All games already have music!",
                        "UniPlaySong");
                    return;
                }

                var result = _api.Dialogs.ShowMessage(
                    $"Found {gamesWithoutMusic.Count} game(s) without music.\n\n" +
                    $"Do you want to automatically download music for all of them?\n\n" +
                    $"A progress dialog will show the current status. You can cancel at any time.",
                    "UniPlaySong - Download Music for All Games",
                    System.Windows.MessageBoxButton.YesNo);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    _fileLogger?.Info($"DownloadMusicForAllGames: Starting download for {gamesWithoutMusic.Count} game(s)");

                    // Use Playnite's global progress dialog (non-blocking, cancelable)
                    var progressOptions = new GlobalProgressOptions(
                        $"Downloading music for {gamesWithoutMusic.Count} games...",
                        cancelable: true)
                    {
                        IsIndeterminate = false
                    };

                    _api.Dialogs.ActivateGlobalProgress(args =>
                    {
                        BulkDownloadMusicWithProgress(gamesWithoutMusic, args);
                    }, progressOptions);
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DownloadMusicForAllGames: Error - {ex.Message}", ex);
                _api.Dialogs.ShowErrorMessage($"Error starting download: {ex.Message}", "UniPlaySong");
            }
        }

        /// <summary>
        /// Performs bulk download with progress reporting.
        /// Called from ActivateGlobalProgress for non-blocking UI updates.
        /// </summary>
        private void BulkDownloadMusicWithProgress(List<Game> games, GlobalProgressActionArgs progressArgs)
        {
            var successCount = 0;
            var skipCount = 0;
            var failCount = 0;
            var totalGames = games.Count;

            progressArgs.ProgressMaxValue = totalGames;

            for (int i = 0; i < totalGames; i++)
            {
                // Check for cancellation
                if (progressArgs.CancelToken.IsCancellationRequested)
                {
                    _fileLogger?.Info($"BulkDownload: Cancelled by user at {i}/{totalGames}");
                    break;
                }

                var game = games[i];

                // Update progress text
                progressArgs.Text = $"Downloading {i + 1}/{totalGames}: {game.Name}";
                progressArgs.CurrentProgressValue = i;

                try
                {
                    // Check if game already has music (double-check)
                    if (_fileService.HasMusic(game))
                    {
                        _fileLogger?.Debug($"BulkDownload: Skipping '{game.Name}' - already has music");
                        skipCount++;
                        continue;
                    }

                    // Synchronously download music for this game
                    var success = AutoDownloadMusicForGameSync(game, progressArgs.CancelToken);
                    if (success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }

                    // Rate limiting: wait between downloads to avoid throttling
                    if (i < totalGames - 1 && !progressArgs.CancelToken.IsCancellationRequested)
                    {
                        Thread.Sleep(2000);
                    }
                }
                catch (OperationCanceledException)
                {
                    _fileLogger?.Info($"BulkDownload: Cancelled during '{game.Name}'");
                    break;
                }
                catch (Exception ex)
                {
                    _fileLogger?.Error($"BulkDownload: Error processing '{game.Name}' - {ex.Message}", ex);
                    failCount++;
                }
            }

            // Final progress update
            progressArgs.CurrentProgressValue = totalGames;

            _fileLogger?.Info($"BulkDownload: Completed - Success: {successCount}, Skipped: {skipCount}, Failed: {failCount}");

            // Show completion notification
            var wasCancelled = progressArgs.CancelToken.IsCancellationRequested;
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                var statusMessage = wasCancelled ? "Download cancelled" : "Download completed";
                var message = $"{statusMessage}:\n" +
                              $"• Downloaded: {successCount}\n" +
                              $"• Skipped (already have music): {skipCount}\n" +
                              $"• Failed: {failCount}";

                _api.Notifications.Add(new NotificationMessage(
                    "UniPlaySong_BulkDownload",
                    message,
                    wasCancelled ? NotificationType.Info : NotificationType.Info));
            }));
        }

        /// <summary>
        /// Synchronously downloads music for a single game with cancellation support.
        /// Used by BulkDownloadMusicWithProgress for progress dialog integration.
        /// Returns true if music was successfully downloaded.
        /// </summary>
        private bool AutoDownloadMusicForGameSync(Game game, CancellationToken cancellationToken)
        {
            try
            {
                _fileLogger?.Debug($"AutoDownloadSync: Processing '{game.Name}'");

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromMinutes(2));

                    // Get albums for this game (try KHInsider first, fallback to YouTube)
                    // Skip cache for bulk download to ensure fresh results
                    var albums = _downloadManager.GetAlbumsForGame(game.Name, Models.Source.All, cts.Token, auto: true, skipCache: true);
                    if (albums == null || !albums.Any())
                    {
                        _fileLogger?.Debug($"AutoDownloadSync: No albums found for '{game.Name}'");
                        return false;
                    }

                    // Use BestAlbumPick to select the best album
                    var bestAlbum = _downloadManager.BestAlbumPick(albums, game);
                    if (bestAlbum == null)
                    {
                        _fileLogger?.Debug($"AutoDownloadSync: No suitable album found for '{game.Name}'");
                        return false;
                    }

                    _fileLogger?.Debug($"AutoDownloadSync: Selected album '{bestAlbum.Name}' for '{game.Name}'");

                    // Get songs from the album
                    var songs = _downloadManager.GetSongsFromAlbum(bestAlbum, cts.Token);
                    if (songs == null || !songs.Any())
                    {
                        _fileLogger?.Debug($"AutoDownloadSync: No songs found in album '{bestAlbum.Name}'");
                        return false;
                    }

                    // Use BestSongPick to select the best song (only 1 song for auto-download)
                    var bestSongs = _downloadManager.BestSongPick(songs, game.Name, maxSongs: 1);
                    if (bestSongs == null || bestSongs.Count == 0)
                    {
                        _fileLogger?.Debug($"AutoDownloadSync: No suitable song found for '{game.Name}'");
                        return false;
                    }

                    var bestSong = bestSongs.First();
                    _fileLogger?.Debug($"AutoDownloadSync: Selected song '{bestSong.Name}' for '{game.Name}'");

                    // Ensure game music directory exists
                    var gameDir = _fileService.EnsureGameMusicDirectory(game);
                    if (string.IsNullOrWhiteSpace(gameDir))
                    {
                        _fileLogger?.Error($"AutoDownloadSync: Failed to create music directory for '{game.Name}'");
                        return false;
                    }

                    // Generate safe filename
                    var safeFileName = Common.StringHelper.CleanForPath(bestSong.Name);
                    var extension = Path.GetExtension(bestSong.Id);
                    if (string.IsNullOrEmpty(extension))
                        extension = ".mp3";
                    var downloadPath = Path.Combine(gameDir, safeFileName + extension);

                    // Download the song
                    var success = _downloadManager.DownloadSong(bestSong, downloadPath, cts.Token, isPreview: false);
                    if (success)
                    {
                        _fileLogger?.Info($"AutoDownloadSync: Successfully downloaded '{bestSong.Name}' for '{game.Name}'");

                        // Auto-normalize if enabled
                        if (_settings?.AutoNormalizeAfterDownload == true && _normalizationService != null)
                        {
                            try
                            {
                                var normSettings = new Models.NormalizationSettings
                                {
                                    FFmpegPath = _settings.FFmpegPath,
                                    TargetLoudness = _settings.NormalizationTargetLoudness,
                                    TruePeak = _settings.NormalizationTruePeak,
                                    LoudnessRange = _settings.NormalizationLoudnessRange,
                                    AudioCodec = _settings.NormalizationCodec,
                                    NormalizationSuffix = _settings.NormalizationSuffix,
                                    TrimSuffix = _settings.TrimSuffix,
                                    DoNotPreserveOriginals = _settings.DoNotPreserveOriginals
                                };
                                _normalizationService.NormalizeFileAsync(
                                    downloadPath,
                                    normSettings,
                                    null,
                                    cts.Token).Wait(cts.Token);
                                _fileLogger?.Debug($"AutoDownloadSync: Normalized '{bestSong.Name}'");
                            }
                            catch (Exception ex)
                            {
                                _fileLogger?.Warn($"AutoDownloadSync: Normalization failed for '{bestSong.Name}' - {ex.Message}");
                            }
                        }

                        return true;
                    }
                    else
                    {
                        _fileLogger?.Warn($"AutoDownloadSync: Download failed for '{bestSong.Name}'");
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _fileLogger?.Warn($"AutoDownloadSync: Cancelled for '{game.Name}'");
                throw; // Re-throw to signal cancellation
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"AutoDownloadSync: Error for '{game.Name}' - {ex.Message}", ex);
                return false;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Gets the download manager service.
        /// </summary>
        public IDownloadManager GetDownloadManager() => _downloadManager;

        /// <summary>
        /// Gets the music playback service.
        /// </summary>
        public IMusicPlaybackService GetPlaybackService() => _playbackService;

        /// <summary>
        /// Gets the game music file service.
        /// </summary>
        public GameMusicFileService GetFileService() => _fileService;

        /// <summary>
        /// Gets the download dialog service.
        /// </summary>
        public DownloadDialogService GetDownloadDialogService() => _downloadDialogService;

        /// <summary>
        /// Gets the settings service.
        /// </summary>
        public SettingsService GetSettingsService() => _settingsService;

        /// <summary>
        /// Gets the search cache service.
        /// </summary>
        public SearchCacheService GetSearchCacheService() => _cacheService;
        
        /// <summary>
        /// Gets the error handler service.
        /// </summary>
        public ErrorHandlerService GetErrorHandlerService() => _errorHandler;

        /// <summary>
        /// Gets the audio normalization service.
        /// </summary>
        public Services.INormalizationService GetNormalizationService() => _normalizationService;

        /// <summary>
        /// Gets the audio trim service.
        /// </summary>
        public Services.ITrimService GetTrimService() => _trimService;

        /// <summary>
        /// Gets the migration service.
        /// </summary>
        public Services.MigrationService GetMigrationService() => _migrationService;

        /// <summary>
        /// Gets the Playnite API instance.
        /// </summary>
        public new IPlayniteAPI PlayniteApi => _api;

        #endregion

        #region Cleanup Operations

        /// <summary>
        /// Gets storage information for the cleanup UI.
        /// </summary>
        public (int gameCount, int fileCount, long totalBytes, int preservedCount, long preservedBytes) GetStorageInfo()
        {
            try
            {
                var basePath = Path.Combine(_api.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName);
                var gamesPath = Path.Combine(basePath, Constants.GamesFolderName);
                var preservedPath = Path.Combine(basePath, "PreservedOriginals");

                int gameCount = 0;
                int fileCount = 0;
                long totalBytes = 0;
                int preservedCount = 0;
                long preservedBytes = 0;

                // Count game folders and music files
                if (Directory.Exists(gamesPath))
                {
                    var gameDirs = Directory.GetDirectories(gamesPath);
                    gameCount = gameDirs.Length;

                    foreach (var gameDir in gameDirs)
                    {
                        var files = Directory.GetFiles(gameDir, "*.*", SearchOption.AllDirectories)
                            .Where(f => Constants.SupportedAudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                        foreach (var file in files)
                        {
                            fileCount++;
                            try { totalBytes += new FileInfo(file).Length; } catch { }
                        }
                    }
                }

                // Count preserved originals
                if (Directory.Exists(preservedPath))
                {
                    var preservedFiles = Directory.GetFiles(preservedPath, "*.*", SearchOption.AllDirectories);
                    preservedCount = preservedFiles.Length;
                    foreach (var file in preservedFiles)
                    {
                        try { preservedBytes += new FileInfo(file).Length; } catch { }
                    }
                }

                return (gameCount, fileCount, totalBytes, preservedCount, preservedBytes);
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"GetStorageInfo: Error - {ex.Message}", ex);
                return (0, 0, 0, 0, 0);
            }
        }

        /// <summary>
        /// Deletes all music files and game folders.
        /// </summary>
        public (int deletedFiles, int deletedFolders, bool success) DeleteAllMusic()
        {
            try
            {
                // Stop any playing music first
                _playbackService?.Stop();

                var basePath = Path.Combine(_api.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName);
                var gamesPath = Path.Combine(basePath, Constants.GamesFolderName);
                var preservedPath = Path.Combine(basePath, "PreservedOriginals");

                int deletedFiles = 0;
                int deletedFolders = 0;

                // Delete all game music folders
                if (Directory.Exists(gamesPath))
                {
                    var gameDirs = Directory.GetDirectories(gamesPath);
                    foreach (var gameDir in gameDirs)
                    {
                        try
                        {
                            var files = Directory.GetFiles(gameDir, "*.*", SearchOption.AllDirectories);
                            deletedFiles += files.Length;
                            Directory.Delete(gameDir, true);
                            deletedFolders++;
                        }
                        catch (Exception ex)
                        {
                            _fileLogger?.Warn($"DeleteAllMusic: Failed to delete '{gameDir}' - {ex.Message}");
                        }
                    }
                }

                // Delete preserved originals
                if (Directory.Exists(preservedPath))
                {
                    try
                    {
                        var files = Directory.GetFiles(preservedPath, "*.*", SearchOption.AllDirectories);
                        deletedFiles += files.Length;
                        Directory.Delete(preservedPath, true);
                        _fileLogger?.Info("DeleteAllMusic: Deleted PreservedOriginals folder");
                    }
                    catch (Exception ex)
                    {
                        _fileLogger?.Warn($"DeleteAllMusic: Failed to delete PreservedOriginals - {ex.Message}");
                    }
                }

                _fileLogger?.Info($"DeleteAllMusic: Deleted {deletedFiles} files in {deletedFolders} folders");
                return (deletedFiles, deletedFolders, true);
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DeleteAllMusic: Error - {ex.Message}", ex);
                return (0, 0, false);
            }
        }

        /// <summary>
        /// Resets all settings to their default values.
        /// </summary>
        public bool ResetSettingsToDefaults()
        {
            try
            {
                // Create new default settings
                var defaultSettings = new UniPlaySongSettings();

                // Copy default values to current settings
                var currentSettings = _settings;
                if (currentSettings == null)
                {
                    _fileLogger?.Error("ResetSettingsToDefaults: Current settings is null");
                    return false;
                }

                // Playback settings
                currentSettings.EnableMusic = defaultSettings.EnableMusic;
                currentSettings.MusicState = defaultSettings.MusicState;
                currentSettings.MusicVolume = defaultSettings.MusicVolume;
                currentSettings.RandomizeOnEverySelect = defaultSettings.RandomizeOnEverySelect;
                currentSettings.RandomizeOnMusicEnd = defaultSettings.RandomizeOnMusicEnd;
                currentSettings.SkipFirstSelectionAfterModeSwitch = defaultSettings.SkipFirstSelectionAfterModeSwitch;
                currentSettings.ThemeCompatibleSilentSkip = defaultSettings.ThemeCompatibleSilentSkip;
                currentSettings.PauseOnTrailer = defaultSettings.PauseOnTrailer;
                currentSettings.PauseOnFocusLoss = defaultSettings.PauseOnFocusLoss;
                currentSettings.PauseOnMinimize = defaultSettings.PauseOnMinimize;

                // Default music settings
                currentSettings.EnableDefaultMusic = defaultSettings.EnableDefaultMusic;
                currentSettings.DefaultMusicPath = defaultSettings.DefaultMusicPath;
                currentSettings.UseNativeMusicAsDefault = defaultSettings.UseNativeMusicAsDefault;
                currentSettings.SuppressPlayniteBackgroundMusic = defaultSettings.SuppressPlayniteBackgroundMusic;

                // Download settings
                currentSettings.AutoDownloadOnLibraryUpdate = defaultSettings.AutoDownloadOnLibraryUpdate;
                currentSettings.LastAutoLibUpdateAssetsDownload = defaultSettings.LastAutoLibUpdateAssetsDownload;
                currentSettings.AutoNormalizeAfterDownload = defaultSettings.AutoNormalizeAfterDownload;

                // Normalization settings
                currentSettings.NormalizationTargetLoudness = defaultSettings.NormalizationTargetLoudness;
                currentSettings.NormalizationTruePeak = defaultSettings.NormalizationTruePeak;
                currentSettings.NormalizationLoudnessRange = defaultSettings.NormalizationLoudnessRange;
                currentSettings.NormalizationCodec = defaultSettings.NormalizationCodec;
                currentSettings.NormalizationSuffix = defaultSettings.NormalizationSuffix;
                currentSettings.TrimSuffix = defaultSettings.TrimSuffix;
                currentSettings.DoNotPreserveOriginals = defaultSettings.DoNotPreserveOriginals;

                // Cache settings
                currentSettings.EnableSearchCache = defaultSettings.EnableSearchCache;
                currentSettings.SearchCacheDurationDays = defaultSettings.SearchCacheDurationDays;

                // Debug settings
                currentSettings.EnableDebugLogging = defaultSettings.EnableDebugLogging;

                // Note: We intentionally don't reset FFmpegPath, YtDlpPath, UseFirefoxCookies
                // as these are system-specific paths that users configure

                // Save the settings
                SavePluginSettings(currentSettings);
                _fileLogger?.Info("ResetSettingsToDefaults: Settings reset to defaults");

                return true;
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"ResetSettingsToDefaults: Error - {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Performs a complete factory reset: deletes all music, clears cache, resets settings.
        /// </summary>
        public (int deletedFiles, int deletedFolders, bool success) FactoryReset()
        {
            try
            {
                _fileLogger?.Info("FactoryReset: Starting factory reset...");

                // Stop any playing music first
                _playbackService?.Stop();

                // Delete all music files
                var (deletedFiles, deletedFolders, deleteSuccess) = DeleteAllMusic();

                // Clear the search cache
                try
                {
                    _cacheService?.ClearCache();
                    _fileLogger?.Info("FactoryReset: Search cache cleared");
                }
                catch (Exception ex)
                {
                    _fileLogger?.Warn($"FactoryReset: Failed to clear cache - {ex.Message}");
                }

                // Delete temp folder
                try
                {
                    var basePath = Path.Combine(_api.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName);
                    var tempPath = Path.Combine(basePath, Constants.TempFolderName);
                    if (Directory.Exists(tempPath))
                    {
                        Directory.Delete(tempPath, true);
                        _fileLogger?.Info("FactoryReset: Temp folder deleted");
                    }
                }
                catch (Exception ex)
                {
                    _fileLogger?.Warn($"FactoryReset: Failed to delete temp folder - {ex.Message}");
                }

                // Reset settings to defaults
                var settingsReset = ResetSettingsToDefaults();

                _fileLogger?.Info($"FactoryReset: Complete - Deleted {deletedFiles} files in {deletedFolders} folders, settings reset: {settingsReset}");

                return (deletedFiles, deletedFolders, deleteSuccess && settingsReset);
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"FactoryReset: Error - {ex.Message}", ex);
                return (0, 0, false);
            }
        }

        #endregion

        #region Xbox Controller Login Bypass Support

        private XInputWrapper.XINPUT_STATE _lastControllerLoginState;
        private bool _hasLastLoginState = false;

        /// <summary>
        /// Starts monitoring Xbox controller input for login screen bypass.
        /// Polls controller every 50ms and triggers login dismiss on A or Start button press.
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
                            XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                            int result = XInputWrapper.XInputGetState(0, ref state);
                            
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
        /// Stops monitoring Xbox controller input for login screen bypass.
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
        /// Checks for login bypass button presses (A button or Start button).
        /// Detects newly pressed buttons and triggers login dismiss on UI thread.
        /// </summary>
        private void CheckLoginBypassButtonPresses(XInputWrapper.XINPUT_GAMEPAD currentState, XInputWrapper.XINPUT_GAMEPAD lastState)
        {
            try
            {
                // Get newly pressed buttons (pressed now but not before)
                ushort newlyPressed = (ushort)(currentState.wButtons & ~lastState.wButtons);
                
                if (newlyPressed == 0)
                    return;
                
                if ((newlyPressed & XInputWrapper.XINPUT_GAMEPAD_A) != 0 || (newlyPressed & XInputWrapper.XINPUT_GAMEPAD_START) != 0)
                {
                    string buttonName = (newlyPressed & XInputWrapper.XINPUT_GAMEPAD_A) != 0 ? "A" : "Start";
                    _fileLogger?.Info($"Xbox controller {buttonName} button pressed - triggering login dismiss");
                    
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
