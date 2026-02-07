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
using UniPlaySong.Audio;
using UniPlaySong.DeskMediaControl;

namespace UniPlaySong
{
    /// <summary>
    /// UniPlaySong - Console-like game music preview extension for Playnite
    /// Plays game-specific music when browsing your library
    /// </summary>
    public class UniPlaySong : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
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
                        // Resolving assembly dependency
                        return Assembly.LoadFrom(dllPath);
                    }

                    // Also try with .exe extension (for some dependencies)
                    string exePath = Path.Combine(extensionPath, $"{assemblyName}.exe");
                    if (File.Exists(exePath))
                    {
                        // Resolving assembly dependency
                        return Assembly.LoadFrom(exePath);
                    }
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, $"Error resolving assembly {args.Name}");
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
        private SearchHintsService _hintsService;
        private Services.INormalizationService _normalizationService;
        private Services.ITrimService _trimService;
        private Services.AudioRepairService _repairService;
        private Services.MigrationService _migrationService;
        private Services.GameMusicTagService _tagService;
        private Services.AudioAmplifyService _amplifyService;
        private Handlers.AmplifyDialogHandler _amplifyDialogHandler;
        private IMusicPlaybackCoordinator _coordinator;
        private IMusicPlayer _currentMusicPlayer;
        private bool _isUsingLiveEffectsPlayer;
        private string _gamesPath;

        // Fullscreen volume integration - respects Playnite's Background Music Volume slider
        private double _playniteFullscreenVolume = 1.0;
        private dynamic _fullscreenSettingsObj;

        // Desktop top panel media control (play/pause button)
        private TopPanelMediaControlViewModel _topPanelMediaControl;

        // Cached settings ViewModel - ensures GetSettings and GetSettingsView use the same instance
        // Critical: Without this, GetSettingsView creates a separate ViewModel and changes aren't saved
        private UniPlaySongSettingsViewModel _settingsViewModel;

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

                // Initialize dedicated downloader logger
                Downloaders.DownloaderLogger.Initialize(extensionPath);
            }
            catch
            {
                // File logger initialization failed - continue without it
            }

            Properties = new GenericPluginProperties { HasSettings = true };

            _settingsService = new SettingsService(_api, Logger, _fileLogger, this);
            _settingsService.SettingsChanged += OnSettingsServiceChanged;

            // Initialize settings ViewModel once - cached for GetSettings/GetSettingsView
            // Following PlayniteSound pattern: cache in constructor, not in GetSettings
            _settingsViewModel = new UniPlaySongSettingsViewModel(this);

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

            // Register custom elements for theme integration
            // SourceName + "_" + ElementName = "UPS_MusicControl", "UPS_SpectrumVisualizer"
            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                SourceName = "UPS",
                ElementList = new List<string> { "MusicControl", "SpectrumVisualizer" }
            });

            // Initialize MusicControl static services
            Controls.MusicControl.UpdateServices(_settings);

            WindowMonitor.Attach(_playbackService, _errorHandler);
            
            if (_settings != null)
            {
                _settings.PropertyChanged += OnSettingsChanged;

                if (_settings.PauseOnTrailer)
                {
                    MediaElementsMonitor.Attach(_api, _settings);
                }

                // Sync toast settings to DialogHelper at startup
                DialogHelper.SyncToastSettings(_settings);
            }
            
            _settingsService.SettingPropertyChanged += OnSettingsServicePropertyChanged;
            SubscribeToMainModel();
            
            // Always suppress native music early in fullscreen mode to avoid SDL2_mixer volume conflicts
            // Catches native music that starts before OnApplicationStarted
            if (IsFullscreen)
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

            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            Logger.Info($"UniPlaySong v{assemblyVersion} loaded");
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
            _fileLogger?.Debug($"Application started - Mode: {_api.ApplicationInfo.Mode}");

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
            
            // In fullscreen mode: always suppress native music to avoid SDL2_mixer volume conflicts,
            // and integrate with Playnite's BackgroundVolume slider
            if (IsFullscreen)
            {
                _fileLogger?.Debug("OnApplicationStarted: Fullscreen mode - suppressing native music and integrating BackgroundVolume");
                SuppressNativeMusic();
                StartNativeMusicSuppression();

                // Initialize fullscreen volume integration (PlayniteSound-proven pattern)
                InitializeFullscreenSettingsRef();
                SubscribeToFullscreenVolumeChanges();
                _playbackService?.SetVolumeMultiplier(_playniteFullscreenVolume);
            }

            // Subscribe to game collection changes for auto-cleanup of music on game removal
            _api.Database.Games.ItemCollectionChanged += OnGamesCollectionChanged;

            // Subscribe to application focus/minimize events for PauseOnDeactivate feature
            Application.Current.Deactivated += OnApplicationDeactivate;
            Application.Current.Activated += OnApplicationActivate;
            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.StateChanged += OnWindowStateChanged;
                Application.Current.MainWindow.IsVisibleChanged += OnWindowVisibilityChanged;
            }

            // Check initial window state and add pause sources if starting minimized/in tray/unfocused
            // This must be called AFTER event handlers are registered but BEFORE music can start playing
            CheckInitialWindowState();

            // Mark initialization complete - this allows deferred playback to proceed
            // Any game selection that happened during startup will now be processed
            _playbackService?.MarkInitializationComplete();

            // Check for search hints updates on startup (if enabled)
            if (_settings?.AutoCheckHintsOnStartup == true)
            {
                CheckForHintsUpdatesAsync();
            }
        }

        /// <summary>
        /// Asynchronously checks for search hints database updates from GitHub.
        /// Runs in the background to avoid blocking startup.
        /// </summary>
        private void CheckForHintsUpdatesAsync()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var hintsService = GetSearchHintsService();
                    if (hintsService == null)
                    {
                        _fileLogger?.Warn("CheckForHintsUpdatesAsync: Hints service not available");
                        return;
                    }

                    var (hasUpdate, gitHubCount, currentCount) = hintsService.CheckForHintsUpdates();

                    if (hasUpdate)
                    {
                        _fileLogger?.Debug($"CheckForHintsUpdatesAsync: Update available - GitHub has {gitHubCount} entries, current has {currentCount} entries");

                        // Show notification on UI thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var result = _api.Dialogs.ShowMessage(
                                $"A newer version of the search hints database is available on GitHub.\n\n" +
                                $"Current: {currentCount} entries\n" +
                                $"GitHub: {gitHubCount} entries ({gitHubCount - currentCount} new)\n\n" +
                                $"The search hints database helps auto-download find correct soundtracks for games with problematic names.\n\n" +
                                $"Would you like to download the update now?",
                                "Search Hints Update Available",
                                System.Windows.MessageBoxButton.YesNo);

                            if (result == System.Windows.MessageBoxResult.Yes)
                            {
                                var success = hintsService.DownloadHintsFromGitHub();
                                if (success)
                                {
                                    _api.Dialogs.ShowMessage(
                                        "Search hints database updated successfully.",
                                        "Update Complete");
                                }
                                else
                                {
                                    _api.Dialogs.ShowErrorMessage(
                                        "Failed to download search hints from GitHub.\n\nPlease check your internet connection and try again from the settings menu.",
                                        "Download Failed");
                                }
                            }
                        });
                    }
                    else
                    {
                        _fileLogger?.Debug($"CheckForHintsUpdatesAsync: No update available (GitHub: {gitHubCount}, Current: {currentCount})");
                    }
                }
                catch (Exception ex)
                {
                    _fileLogger?.Warn($"CheckForHintsUpdatesAsync: Error checking for updates - {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Handles library update events from Playnite.
        /// Triggers auto-download of music for newly added games if enabled.
        /// Also triggers auto-tagging of games if enabled.
        /// </summary>
        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            try
            {
                _fileLogger?.Debug($"OnLibraryUpdated: Library updated event received");

                if (_settings == null)
                {
                    _fileLogger?.Warn("OnLibraryUpdated: Settings is null");
                    return;
                }

                // Handle auto-download
                if (_settings.AutoDownloadOnLibraryUpdate)
                {
                    // Get games added since last auto-download check
                    var lastCheck = _settings.LastAutoLibUpdateAssetsDownload;
                    _fileLogger?.Debug($"OnLibraryUpdated: Checking for games added after {lastCheck}");

                    var newGames = _api.Database.Games
                        .Where(g => g.Added.HasValue && g.Added.Value > lastCheck)
                        .ToList();

                    _fileLogger?.Debug($"OnLibraryUpdated: Found {newGames.Count} new game(s)");

                    // Start auto-download in background if we have new games
                    if (newGames.Count > 0)
                    {
                        Task.Run(() => AutoDownloadMusicForGamesAsync(newGames));
                    }

                    // Update timestamp AFTER identifying games (like PlayniteSound does)
                    _settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
                    SavePluginSettings(_settings);
                }
                else
                {
                    _fileLogger?.Debug("OnLibraryUpdated: Auto-download disabled");
                    // Still update timestamp to avoid processing old games later
                    _settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
                    SavePluginSettings(_settings);
                }

                // Handle auto-tagging (scans all games, not just new ones)
                if (_settings.AutoTagOnLibraryUpdate && _tagService != null)
                {
                    _fileLogger?.Debug("OnLibraryUpdated: Starting auto-tag scan in background");
                    Task.Run(async () =>
                    {
                        try
                        {
                            var result = await _tagService.ScanAndTagAllGamesAsync();
                            _fileLogger?.Debug($"OnLibraryUpdated: Auto-tag complete - {result.GamesWithMusic} with music, {result.GamesWithoutMusic} without, {result.GamesModified} updated");
                        }
                        catch (Exception ex)
                        {
                            _fileLogger?.Error($"OnLibraryUpdated: Auto-tag error - {ex.Message}", ex);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"OnLibraryUpdated: Error - {ex.Message}", ex);
                Logger.Error(ex, "Error in OnLibraryUpdated");
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

            _fileLogger?.Debug($"AutoDownloadMusicForGamesAsync: Starting auto-download for {games.Count} game(s)");

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

            _fileLogger?.Debug($"AutoDownloadMusicForGamesAsync: Completed - Success: {successCount}, Skipped: {skipCount}, Failed: {failCount}");

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
                        extension = Constants.DefaultAudioExtension;
                    var downloadPath = Path.Combine(gameDir, safeFileName + extension);

                    // Download the song
                    var success = _downloadManager.DownloadSong(bestSong, downloadPath, cts.Token, isPreview: false);
                    if (success)
                    {
                        _fileLogger?.Debug($"AutoDownload: Successfully downloaded '{bestSong.Name}' for '{game.Name}'");

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

            // Unsubscribe from game collection changes
            _api.Database.Games.ItemCollectionChanged -= OnGamesCollectionChanged;

            // Unsubscribe from focus/minimize events
            Application.Current.Deactivated -= OnApplicationDeactivate;
            Application.Current.Activated -= OnApplicationActivate;
            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.StateChanged -= OnWindowStateChanged;
                Application.Current.MainWindow.IsVisibleChanged -= OnWindowVisibilityChanged;
            }

            // Remove plugin instance registration
            if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
            {
                Application.Current.Properties.Remove("UniPlaySongPlugin");
            }

            StopControllerLoginMonitoring();
            StopNativeMusicSuppression();
            UnsubscribeFromFullscreenVolumeChanges();

            _fileLogger?.Debug("Application stopped");
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

        /// <summary>
        /// Handles changes to the games database collection.
        /// When games are removed, deletes their associated music directories if the setting is enabled.
        /// </summary>
        private void OnGamesCollectionChanged(object sender, ItemCollectionChangedEventArgs<Game> args)
        {
            if (_settings?.AutoDeleteMusicOnGameRemoval != true)
            {
                return;
            }

            if (args.RemovedItems == null || args.RemovedItems.Count == 0)
            {
                return;
            }

            foreach (var removedGame in args.RemovedItems)
            {
                try
                {
                    var gameId = removedGame.Id.ToString();
                    var musicDir = Path.Combine(_gamesPath, gameId);

                    if (!Directory.Exists(musicDir))
                    {
                        continue;
                    }

                    // Stop playback if the removed game is currently playing
                    if (_playbackService?.CurrentGame?.Id == removedGame.Id)
                    {
                        _fileLogger?.Debug($"OnGamesCollectionChanged: Stopping playback for removed game '{removedGame.Name}'");
                        _playbackService.Stop();
                    }

                    var fileCount = Directory.GetFiles(musicDir, "*.*", SearchOption.AllDirectories).Length;
                    Directory.Delete(musicDir, true);
                    _fileLogger?.Debug($"OnGamesCollectionChanged: Deleted music directory for removed game '{removedGame.Name}' (ID: {gameId}, {fileCount} files)");
                }
                catch (Exception ex)
                {
                    _fileLogger?.Warn($"OnGamesCollectionChanged: Failed to delete music for removed game '{removedGame.Name}' - {ex.Message}");
                    Logger.Warn($"Failed to delete music directory for removed game '{removedGame.Name}': {ex.Message}");
                }
            }
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            // VideoIsPlaying and ThemeOverlayActive are handled by OnSettingsServicePropertyChanged
            // (via SettingsService relay) — not here, to avoid double-firing HandleVideoStateChange
            // and HandleThemeOverlayChange which causes inconsistent pause state.
        }

        /// <summary>
        /// Handles SettingsService property change events.
        /// Forwards video state and theme overlay changes to the coordinator.
        /// </summary>
        private void OnSettingsServicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UniPlaySongSettings.VideoIsPlaying))
            {
                _coordinator?.HandleVideoStateChange(_settings.VideoIsPlaying);
            }
            else if (e.PropertyName == nameof(UniPlaySongSettings.ThemeOverlayActive))
            {
                _coordinator?.HandleThemeOverlayChange(_settings.ThemeOverlayActive);
            }
        }

        /// <summary>
        /// Handles SettingsService settings changed events.
        /// Manages plugin-level concerns: volume updates, media monitor, and PropertyChanged subscriptions.
        /// Coordinator subscribes directly to SettingsService, so no manual UpdateSettings() needed.
        /// </summary>
        private void OnSettingsServiceChanged(object sender, SettingsChangedEventArgs e)
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

            // Always update MediaElementsMonitor's settings reference on every settings change.
            // Without this, the monitor writes VideoIsPlaying to a stale settings object
            // after any plugin's settings are saved (Playnite creates a new settings instance).
            MediaElementsMonitor.UpdateSettings(e.NewSettings);

            // Attach media monitor if pause on trailer setting was just enabled
            if (e.OldSettings?.PauseOnTrailer != e.NewSettings?.PauseOnTrailer)
            {
                if (e.NewSettings?.PauseOnTrailer == true)
                {
                    MediaElementsMonitor.Attach(_api, e.NewSettings);
                }
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
                        e.NewSettings?.YtDlpPath, e.NewSettings?.FFmpegPath, _errorHandler, _cacheService, _hintsService, e.NewSettings);
                }

                // Check if LiveEffectsEnabled changed - need to switch music players
                bool liveEffectsChanged = e.OldSettings.LiveEffectsEnabled != e.NewSettings.LiveEffectsEnabled;
                if (liveEffectsChanged)
                {
                    _fileLogger?.Debug($"LiveEffectsEnabled changed from {e.OldSettings.LiveEffectsEnabled} to {e.NewSettings.LiveEffectsEnabled} - recreating music player");
                    RecreateMusicPlayerForLiveEffects();
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
        }

        /// <summary>
        /// Called when settings are saved in the settings UI.
        /// Reloads settings via SettingsService, which automatically notifies all subscribers.
        /// </summary>
        public void OnSettingsSaved()
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
            switch (windowState)
            {
                case WindowState.Normal:
                case WindowState.Maximized:
                    // Always remove — if the source isn't present, HashSet.Remove is a no-op
                    _playbackService?.RemovePauseSource(Models.PauseSource.Minimized);
                    break;
                case WindowState.Minimized:
                    if (_settings?.PauseOnMinimize == true)
                        _playbackService?.AddPauseSource(Models.PauseSource.Minimized);
                    break;
            }
        }

        /// <summary>
        /// Handles window visibility changes (show/hide in system tray).
        /// Pauses music when hidden in system tray if PauseWhenInSystemTray is enabled.
        /// </summary>
        private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var isVisible = (bool)e.NewValue;
            if (isVisible)
                // Always remove — if the source isn't present, HashSet.Remove is a no-op
                _playbackService?.RemovePauseSource(Models.PauseSource.SystemTray);
            else if (_settings?.PauseWhenInSystemTray == true)
                _playbackService?.AddPauseSource(Models.PauseSource.SystemTray);
        }

        /// <summary>
        /// Handles application losing focus.
        /// Pauses music when Playnite loses focus if PauseOnFocusLoss is enabled.
        /// </summary>
        private void OnApplicationDeactivate(object sender, EventArgs e)
        {
            if (_settings?.PauseOnFocusLoss == true)
                _playbackService?.AddPauseSource(Models.PauseSource.FocusLoss);
        }

        /// <summary>
        /// Handles application gaining focus.
        /// Resumes music when Playnite gains focus if PauseOnFocusLoss is enabled.
        /// </summary>
        private void OnApplicationActivate(object sender, EventArgs e)
        {
            // Always remove — if the source isn't present, HashSet.Remove is a no-op
            _playbackService?.RemovePauseSource(Models.PauseSource.FocusLoss);
        }

        /// <summary>
        /// Early check of initial window state during service initialization.
        /// Called before OnGameSelected can trigger music playback.
        /// This is a best-effort check - if MainWindow isn't available yet, we retry in CheckInitialWindowState().
        /// </summary>
        private void CheckInitialWindowStateEarly()
        {
            try
            {
                var window = Application.Current?.MainWindow;
                if (window == null) return;

                if (_settings?.PauseOnMinimize == true && window.WindowState == WindowState.Minimized)
                    _playbackService?.AddPauseSource(Models.PauseSource.Minimized);

                if (_settings?.PauseWhenInSystemTray == true && !window.IsVisible)
                    _playbackService?.AddPauseSource(Models.PauseSource.SystemTray);

                if (_settings?.PauseOnFocusLoss == true && !window.IsActive)
                    _playbackService?.AddPauseSource(Models.PauseSource.FocusLoss);
            }
            catch
            {
                // Early initialization - window may not be fully available yet
            }
        }

        /// <summary>
        /// Checks the initial window state at startup and adds appropriate pause sources.
        /// This ensures music doesn't play if Playnite starts minimized, in system tray, or unfocused.
        /// Called once after event handlers are registered in OnApplicationStarted.
        /// </summary>
        private void CheckInitialWindowState()
        {
            var window = Application.Current?.MainWindow;
            if (window == null) return;

            if (_settings?.PauseOnMinimize == true && window.WindowState == WindowState.Minimized)
                _playbackService?.AddPauseSource(Models.PauseSource.Minimized);

            if (_settings?.PauseWhenInSystemTray == true && !window.IsVisible)
                _playbackService?.AddPauseSource(Models.PauseSource.SystemTray);

            if (_settings?.PauseOnFocusLoss == true && !window.IsActive)
                _playbackService?.AddPauseSource(Models.PauseSource.FocusLoss);
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
            _gamesPath = gamesPath;
            var tempPath = Path.Combine(basePath, Constants.TempFolderName);

            // Initialize error handler service (for centralized error handling)
            _errorHandler = new ErrorHandlerService(Logger, _fileLogger, _api);

            _fileService = new GameMusicFileService(gamesPath, _errorHandler);

            // Create the appropriate music player based on LiveEffectsEnabled setting
            _currentMusicPlayer = CreateMusicPlayer();

            _playbackService = new MusicPlaybackService(_currentMusicPlayer, _fileService, _fileLogger, _errorHandler);
            // Always suppress native music in fullscreen when our music starts (avoids SDL2_mixer volume conflicts)
            _playbackService.OnMusicStarted += (settings) =>
            {
                if (!IsFullscreen)
                    return;

                SuppressNativeMusic();
            };

            if (_settings != null)
            {
                _playbackService.SetVolume(_settings.MusicVolume / Constants.VolumeDivisor);
            }

            // Check initial window state EARLY - before any music can start playing
            // This ensures pause sources are set before OnGameSelected triggers
            CheckInitialWindowStateEarly();

            // Initialize coordinator - centralizes all music playback logic and state management
            _coordinator = new MusicPlaybackCoordinator(
                _playbackService,
                _settingsService,
                Logger,
                _fileLogger,
                () => IsFullscreen,
                () => IsDesktop,
                () => SelectedGames?.FirstOrDefault()
            );
            _fileLogger?.Debug("MusicPlaybackCoordinator initialized");

            // Initialize search cache service
            var extensionDataPath = Path.Combine(_api.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName);
            _cacheService = new SearchCacheService(
                extensionDataPath,
                enabled: _settings?.EnableSearchCache ?? true,
                cacheDurationDays: _settings?.SearchCacheDurationDays ?? 7);
            _fileLogger?.Debug("SearchCacheService initialized");

            // Initialize search hints service (allows user overrides for problematic game searches)
            var pluginInstallPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _hintsService = new SearchHintsService(pluginInstallPath, extensionDataPath);
            _fileLogger?.Debug($"SearchHintsService initialized (bundled: {_hintsService.GetBundledHintsFilePath()}, user: {_hintsService.GetUserHintsFilePath()})");

            _downloadManager = new DownloadManager(
                _httpClient, _htmlWeb, tempPath,
                _settings?.YtDlpPath, _settings?.FFmpegPath, _errorHandler, _cacheService, _hintsService, _settings);

            _downloadDialogService = new DownloadDialogService(
                _api, _downloadManager, _playbackService, _fileService, _settingsService, _errorHandler);

            // Initialize normalization service with playback service and base path for backups
            _normalizationService = new Services.AudioNormalizationService(_errorHandler, _playbackService, basePath);
            _fileLogger?.Debug("AudioNormalizationService initialized");

            // Wire up normalization service to download dialog service for auto-normalize feature
            _downloadDialogService.SetNormalizationService(_normalizationService);

            // Initialize trim service with playback service and base path for backups
            _trimService = new Services.AudioTrimService(_errorHandler, _playbackService, basePath);
            _fileLogger?.Debug("AudioTrimService initialized");

            // Initialize waveform trim service for precise trimming
            _waveformTrimService = new Services.WaveformTrimService(_errorHandler, _playbackService, basePath);
            _fileLogger?.Debug("WaveformTrimService initialized");

            // Initialize migration service for PlayniteSound <-> UniPlaySong migration
            _migrationService = new Services.MigrationService(_api, _errorHandler);
            _fileLogger?.Debug("MigrationService initialized");

            // Initialize audio repair service for fixing problematic audio files
            _repairService = new Services.AudioRepairService(_errorHandler, _playbackService, basePath);
            _fileLogger?.Debug("AudioRepairService initialized");

            // Initialize game music tag service for tagging games by music status
            _tagService = new Services.GameMusicTagService(_api, _fileService);
            _fileLogger?.Debug("GameMusicTagService initialized");

            // Wire up tag service to download dialog service for post-download tag updates
            _downloadDialogService.SetTagService(_tagService);

            // Wire up search cache service to download dialog service for Auto-Add Songs on skipped games
            _downloadDialogService.SetSearchCacheService(_cacheService);

            // Initialize amplify service for audio volume adjustments
            _amplifyService = new Services.AudioAmplifyService(_errorHandler, _playbackService, basePath);
            _fileLogger?.Debug("AudioAmplifyService initialized");

            // Initialize Desktop top panel media control (play/pause button)
            // Pass a getter function so it always uses the current playback service (handles Live Effects toggle)
            _topPanelMediaControl = new TopPanelMediaControlViewModel(
                () => _playbackService,
                () => _settings,
                () => SelectedGames?.FirstOrDefault(),
                msg => _fileLogger?.Debug(msg),
                (ex, context) => _errorHandler?.HandleError(ex, context, showUserMessage: false)
            );
            _fileLogger?.Debug("TopPanelMediaControlViewModel initialized");
        }

        private void InitializeMenuHandlers()
        {
            _gameMenuHandler = new GameMenuHandler(
                _api, Logger, _downloadManager, _fileService,
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

            // Initialize amplify dialog handler (service initialized in InitializeServices)
            _amplifyDialogHandler = new Handlers.AmplifyDialogHandler(
                _api, () => _settings, _fileService, _playbackService, _amplifyService);
        }

        /// <summary>
        /// Creates the appropriate music player based on LiveEffectsEnabled setting.
        /// When live effects are enabled, uses NAudioMusicPlayer with real-time effects chain.
        /// Otherwise, uses SDL2MusicPlayer (or falls back to WPF MediaPlayer).
        /// </summary>
        private IMusicPlayer CreateMusicPlayer()
        {
            bool useLiveEffects = _settings?.LiveEffectsEnabled ?? false;

            if (useLiveEffects)
            {
                try
                {
                    var player = new NAudioMusicPlayer(_settingsService);
                    // NAudioMusicPlayer initialized (Live Effects enabled)
                    _isUsingLiveEffectsPlayer = true;
                    return player;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to initialize NAudioMusicPlayer, falling back to standard player: {ex.Message}");
                    _isUsingLiveEffectsPlayer = false;
                    // Fall through to standard player creation
                }
            }

            // Standard player: SDL2 with WPF fallback
            _isUsingLiveEffectsPlayer = false;
            try
            {
                var player = new SDL2MusicPlayer(_errorHandler);
                // SDL2MusicPlayer initialized
                return player;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to initialize SDL2MusicPlayer, falling back to WPF MediaPlayer: {ex.Message}");
                return new MusicPlayer(_errorHandler);
            }
        }

        /// <summary>
        /// Recreates the playback service with a new player when LiveEffectsEnabled changes.
        /// Saves current playback state and restarts music after switching players.
        /// </summary>
        private void RecreateMusicPlayerForLiveEffects()
        {
            try
            {
                _fileLogger?.Debug($"Recreating music player (LiveEffectsEnabled changed to: {_settings?.LiveEffectsEnabled})");

                // Save current game before stopping - we'll restart music for this game after recreation
                var currentGame = SelectedGames?.FirstOrDefault();
                bool wasPlaying = _playbackService?.IsPlaying == true || _playbackService?.IsLoaded == true;

                _fileLogger?.Debug($"Current state before recreation: Game={currentGame?.Name ?? "null"}, WasPlaying={wasPlaying}");

                // Stop current playback
                _playbackService?.Stop();

                // Dispose old player if it implements IDisposable
                if (_currentMusicPlayer is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                // Create new player
                _currentMusicPlayer = CreateMusicPlayer();

                // Recreate playback service with new player
                var oldService = _playbackService;
                _playbackService = new MusicPlaybackService(_currentMusicPlayer, _fileService, _fileLogger, _errorHandler);

                // Mark initialization complete — app is already running, skip deferred-playback gate
                _playbackService.MarkInitializationComplete();

                // Re-attach event handlers - always suppress native music in fullscreen
                _playbackService.OnMusicStarted += (settings) =>
                {
                    if (!IsFullscreen)
                        return;

                    SuppressNativeMusic();
                };

                if (_settings != null)
                {
                    _playbackService.SetVolume(_settings.MusicVolume / Constants.VolumeDivisor);
                }

                // Reapply fullscreen volume multiplier to the new service
                if (IsFullscreen)
                {
                    _playbackService.SetVolumeMultiplier(_playniteFullscreenVolume);
                }

                // Update coordinator with new playback service
                _coordinator = new MusicPlaybackCoordinator(
                    _playbackService,
                    _settingsService,
                    Logger,
                    _fileLogger,
                    () => IsFullscreen,
                    () => IsDesktop,
                    () => SelectedGames?.FirstOrDefault()
                );

                _fileLogger?.Debug($"Music player recreated successfully (using: {(_isUsingLiveEffectsPlayer ? "NAudioMusicPlayer" : "SDL2/WPF")})");

                // Re-subscribe top panel control to the new playback service
                _topPanelMediaControl?.ResubscribeToEvents(_playbackService);

                // Restart music for the current game if music was playing before the switch
                if (wasPlaying && currentGame != null && _coordinator.ShouldPlayMusic(currentGame))
                {
                    _fileLogger?.Debug($"Restarting music for game: {currentGame.Name}");
                    _playbackService.PlayGameMusic(currentGame, _settings, forceReload: true);
                }
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(ex, "recreating music player for live effects", showUserMessage: false);
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
                    _fileLogger?.Debug("Login input handler attached");
                    
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
        /// Reads Playnite's fullscreen BackgroundVolume setting via cached reflection reference.
        /// Returns 1.0 if not in fullscreen or reflection fails (graceful degradation).
        /// </summary>
        private double GetPlayniteFullscreenVolume()
        {
            try
            {
                if (_fullscreenSettingsObj == null)
                {
                    return 1.0;
                }

                float bgVolume = (float)(_fullscreenSettingsObj.BackgroundVolume);
                return Math.Max(0.0, Math.Min(1.0, bgVolume));
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"GetPlayniteFullscreenVolume: Failed to read BackgroundVolume - {ex.Message}");
                return 1.0;
            }
        }

        /// <summary>
        /// Initializes the cached reference to Playnite's internal fullscreen settings object.
        /// Uses the same reflection pattern proven by PlayniteSound.
        /// </summary>
        private void InitializeFullscreenSettingsRef()
        {
            try
            {
                _fullscreenSettingsObj = _api.ApplicationSettings.Fullscreen
                    .GetType()
                    .GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(_api.ApplicationSettings.Fullscreen);

                if (_fullscreenSettingsObj != null)
                {
                    _playniteFullscreenVolume = GetPlayniteFullscreenVolume();
                    _fileLogger?.Debug($"InitializeFullscreenSettingsRef: Got fullscreen settings, BackgroundVolume={_playniteFullscreenVolume:F2}");
                }
                else
                {
                    _fileLogger?.Warn("InitializeFullscreenSettingsRef: Could not get fullscreen settings object");
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"InitializeFullscreenSettingsRef: Failed - {ex.Message}");
            }
        }

        /// <summary>
        /// Subscribes to Playnite's fullscreen settings PropertyChanged to detect BackgroundVolume changes.
        /// Uses the same pattern proven by PlayniteSound (ctx.AppSettings.Fullscreen as INotifyPropertyChanged).
        /// </summary>
        private void SubscribeToFullscreenVolumeChanges()
        {
            try
            {
                dynamic ctx = Application.Current.MainWindow?.DataContext;
                if (ctx == null)
                {
                    _fileLogger?.Warn("SubscribeToFullscreenVolumeChanges: MainWindow.DataContext is null");
                    return;
                }

                var fullscreenSettings = ctx.AppSettings?.Fullscreen as INotifyPropertyChanged;
                if (fullscreenSettings != null)
                {
                    fullscreenSettings.PropertyChanged += OnFullscreenSettingsChanged;
                    _fileLogger?.Debug("SubscribeToFullscreenVolumeChanges: Subscribed to fullscreen settings changes");
                }
                else
                {
                    _fileLogger?.Warn("SubscribeToFullscreenVolumeChanges: Could not cast fullscreen settings to INotifyPropertyChanged");
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"SubscribeToFullscreenVolumeChanges: Failed - {ex.Message}");
            }
        }

        /// <summary>
        /// Unsubscribes from Playnite's fullscreen settings PropertyChanged.
        /// </summary>
        private void UnsubscribeFromFullscreenVolumeChanges()
        {
            try
            {
                dynamic ctx = Application.Current?.MainWindow?.DataContext;
                if (ctx == null)
                    return;

                var fullscreenSettings = ctx.AppSettings?.Fullscreen as INotifyPropertyChanged;
                if (fullscreenSettings != null)
                {
                    fullscreenSettings.PropertyChanged -= OnFullscreenSettingsChanged;
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"UnsubscribeFromFullscreenVolumeChanges: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles changes to Playnite's fullscreen settings.
        /// When BackgroundVolume changes, updates the volume multiplier on the playback service.
        /// </summary>
        private void OnFullscreenSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "BackgroundVolume")
            {
                _playniteFullscreenVolume = GetPlayniteFullscreenVolume();
                _fileLogger?.Debug($"OnFullscreenSettingsChanged: BackgroundVolume changed to {_playniteFullscreenVolume:F2}");
                _playbackService?.SetVolumeMultiplier(IsFullscreen ? _playniteFullscreenVolume : 1.0);
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
                _fileLogger?.Debug("Started native music suppression monitoring (15 second window)");
                
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
                
                _fileLogger?.Debug("Stopped native music suppression monitoring");
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
                        _fileLogger?.Debug("SuppressNativeMusic: Successfully stopped native background music");
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

        /// <summary>
        /// Repair a single audio file.
        /// Delegates to GameMenuHandler.
        /// </summary>
        public void RepairSingleFile(Game game, string filePath)
        {
            // Use the existing repair logic from GameMenuHandler but for a specific file
            if (_gameMenuHandler == null)
            {
                PlayniteApi.Dialogs.ShowErrorMessage("Game menu handler not initialized.", "UniPlaySong");
                return;
            }

            // Validate FFmpeg availability
            if (_settings == null || string.IsNullOrWhiteSpace(_settings.FFmpegPath))
            {
                PlayniteApi.Dialogs.ShowMessage(
                    "FFmpeg is required for audio repair. Please configure FFmpeg path in Settings → Audio Normalization.",
                    "UniPlaySong");
                return;
            }

            if (_repairService == null || !_repairService.ValidateFFmpegAvailable(_settings.FFmpegPath))
            {
                PlayniteApi.Dialogs.ShowMessage(
                    $"FFmpeg not found or not working at: {_settings.FFmpegPath}\n\nPlease verify the path in Settings → Audio Normalization.",
                    "UniPlaySong");
                return;
            }

            // Run repair with progress
            RepairSingleFileWithProgress(game, filePath, _settings.FFmpegPath);
        }

        /// <summary>
        /// Repair a single audio file with progress dialog.
        /// </summary>
        private void RepairSingleFileWithProgress(Game game, string filePath, string ffmpegPath)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            // Starting audio repair

            var progressTitle = "UniPlaySong - Repairing Audio";
            var progressOptions = new GlobalProgressOptions(progressTitle, false)
            {
                IsIndeterminate = true
            };

            Services.AudioProbeResult probeResult = null;
            bool repairSuccess = false;

            PlayniteApi.Dialogs.ActivateGlobalProgress((args) =>
            {
                args.Text = $"Analyzing: {fileName}";

                // Probe the file
                var probeTask = _repairService.ProbeFileAsync(filePath, ffmpegPath);
                probeTask.Wait();
                probeResult = probeTask.Result;

                if (probeResult.Success)
                {
                    args.Text = $"Repairing: {fileName}";

                    // Repair the file
                    var repairTask = _repairService.RepairFileAsync(filePath, ffmpegPath);
                    repairTask.Wait();
                    repairSuccess = repairTask.Result;
                }
            }, progressOptions);

            // Show result
            if (probeResult == null || !probeResult.Success)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    $"Failed to analyze audio file: {fileName}\n\n" +
                    $"Error: {probeResult?.ErrorMessage ?? "Unknown error"}",
                    "UniPlaySong - Repair Failed");
                return;
            }

            if (repairSuccess)
            {
                var issuesSummary = probeResult.HasIssues
                    ? $"Issues detected: {probeResult.Issues}\n\n"
                    : "No obvious issues detected, but file was re-encoded.\n\n";

                PlayniteApi.Dialogs.ShowMessage(
                    $"Successfully repaired: {fileName}\n\n" +
                    issuesSummary +
                    "The original file has been backed up to PreservedOriginals folder.\n" +
                    "The repaired file should now play correctly.",
                    "UniPlaySong - Repair Complete");

                // Audio repair completed successfully
            }
            else
            {
                PlayniteApi.Dialogs.ShowMessage(
                    $"Failed to repair audio file: {fileName}\n\n" +
                    "The file may be too corrupted to repair, or FFmpeg encountered an error.\n" +
                    "Check the logs for more details.",
                    "UniPlaySong - Repair Failed");

                Logger.Error($"Audio repair failed for: {fileName}");
            }
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
                        Logger.Error(ex, "Error during migration");
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
                Logger.Error(ex, "Error showing migration progress dialog");
                PlayniteApi.Dialogs.ShowErrorMessage($"Error showing progress dialog: {ex.Message}", "Migration Error");
            }
        }

        /// <summary>
        /// Run a delete operation with progress dialog (no completion report)
        /// </summary>
        public void RunDeleteWithProgress(
            string title,
            Func<IProgress<Services.MigrationProgress>, System.Threading.CancellationToken, System.Threading.Tasks.Task<Services.PlayniteSoundDeleteResult>> deleteTask)
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

                Common.DialogHelper.AddFocusReturnHandler(window, PlayniteApi, "delete dialog close");

                // Start delete asynchronously
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

                        var result = await deleteTask(progress, progressDialog.CancellationToken);

                        // Report completion as a batch result for UI
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            var batchResult = new Services.MigrationBatchResult
                            {
                                TotalGames = result.TotalGames,
                                SuccessfulGames = result.GamesProcessed,
                                FailedGames = result.GamesFailed,
                                TotalFilesCopied = result.FilesDeleted, // Using FilesDeleted for display
                                WasCancelled = result.WasCancelled
                            };
                            progressDialog.ReportCompletion(batchResult);

                            if (!result.WasCancelled)
                            {
                                var message = $"Delete Complete!\n\n" +
                                            $"Games processed: {result.GamesProcessed}\n" +
                                            $"Files deleted: {result.FilesDeleted}\n" +
                                            $"Folders removed: {result.FoldersDeleted}\n" +
                                            $"Failed: {result.FilesFailed}";

                                PlayniteApi.Dialogs.ShowMessage(message, "Delete Complete");
                            }
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            PlayniteApi.Dialogs.ShowMessage("Delete operation was cancelled.", "Delete Cancelled");
                        }));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error during delete");
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            PlayniteApi.Dialogs.ShowErrorMessage($"Error during delete: {ex.Message}", "Delete Error");
                        }));
                    }
                });

                // Show dialog (blocks until closed)
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error showing delete progress dialog");
                PlayniteApi.Dialogs.ShowErrorMessage($"Error showing progress dialog: {ex.Message}", "Delete Error");
            }
        }

        /// <summary>
        /// Runs game music tag scanning with progress dialog.
        /// </summary>
        public void RunTagScanWithProgress(Services.GameMusicTagService tagService)
        {
            try
            {
                var progressOptions = new GlobalProgressOptions("Scanning games for music status...", true)
                {
                    IsIndeterminate = false
                };

                _api.Dialogs.ActivateGlobalProgress((args) =>
                {
                    try
                    {
                        var progress = new Progress<Services.TagScanProgress>(p =>
                        {
                            args.CurrentProgressValue = p.ProcessedCount;
                            args.ProgressMaxValue = p.TotalCount;
                            args.Text = $"Scanning: {p.CurrentGame}\n({p.ProcessedCount}/{p.TotalCount})";
                        });

                        var task = tagService.ScanAndTagAllGamesAsync(progress, args.CancelToken);
                        task.Wait(args.CancelToken);
                        var result = task.Result;

                        if (!args.CancelToken.IsCancellationRequested)
                        {
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                _api.Dialogs.ShowMessage(
                                    $"Tag scan complete!\n\n" +
                                    $"Games scanned: {result.TotalGames}\n" +
                                    $"With music: {result.GamesWithMusic}\n" +
                                    $"Without music: {result.GamesWithoutMusic}\n" +
                                    $"Tags updated: {result.GamesModified}",
                                    "Scan Complete");
                            }));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // User cancelled, do nothing
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error during tag scan");
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            _api.Dialogs.ShowErrorMessage($"Error during scan: {ex.Message}", "Scan Error");
                        }));
                    }
                }, progressOptions);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error showing tag scan progress");
                _api.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "Scan Error");
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
            // Return cached ViewModel (initialized in constructor)
            // Following PlayniteSound pattern - Playnite will set this as DataContext
            return _settingsViewModel;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            // Return view WITHOUT setting DataContext manually
            // Playnite sets the ISettings object (from GetSettings) as DataContext automatically
            // Following PlayniteSound pattern: pass plugin reference, not ViewModel
            return new UniPlaySongSettingsView(this);
        }

        /// <summary>
        /// Returns custom controls for theme integration.
        /// Called by Playnite when a theme uses UPS_MusicControl or UPS_SpectrumVisualizer elements.
        /// </summary>
        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            if (args.Name == "MusicControl")
            {
                // Creating MusicControl instance for theme
                return new Controls.MusicControl(_settings);
            }
            if (args.Name == "SpectrumVisualizer")
            {
                // Creating SpectrumVisualizer instance for theme
                return new Controls.SpectrumVisualizerPluginControl(() => _settings);
            }
            return null;
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            if (args.Games.Count == 0)
                return Enumerable.Empty<GameMenuItem>();

            const string menuSection = Constants.MenuSectionName;
            var items = new List<GameMenuItem>();
            var games = args.Games.ToList();

            // Debug: Log how many games Playnite is passing to us
            _fileLogger?.Debug($"[Menu] GetGameMenuItems called with {games.Count} game(s)");

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

                // Delete Music (All) - bulk delete music for selected games
                items.Add(new GameMenuItem
                {
                    Description = $"Delete Music (All) ({games.Count} games)",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.DeleteAllMusicForGames(games)
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

                items.Add(new GameMenuItem
                {
                    Description = "🎮 Amplify Audio",
                    MenuSection = audioProcessingSection,
                    Action = _ => _amplifyDialogHandler.ShowControllerAmplifyDialog(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "🎮 Repair Audio File",
                    MenuSection = audioProcessingSection,
                    Action = _ => _controllerDialogHandler.ShowRepairIndividualSong(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "Repair Music Folder",
                    MenuSection = audioProcessingSection,
                    Action = _ => _gameMenuHandler.RepairAllAudioFiles(game)
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

                // === Audio Editing Submenu (Precise Trim, Amplify, and Repair options) ===
                items.Add(new GameMenuItem
                {
                    Description = "Precise Trim",
                    MenuSection = audioEditingSection,
                    Action = _ => _waveformTrimDialogHandler.ShowPreciseTrimDialog(game)
                });

                items.Add(new GameMenuItem
                {
                    Description = "Amplify Audio",
                    MenuSection = audioEditingSection,
                    Action = _ => _amplifyDialogHandler.ShowAmplifyDialog(game)
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
        /// Gets the top panel items for Desktop mode.
        /// Provides a play/pause toggle button for music control.
        /// </summary>
        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            // Only show in Desktop mode
            if (!IsDesktop)
            {
                return Enumerable.Empty<TopPanelItem>();
            }

            // Return all media control items from our ViewModel
            if (_topPanelMediaControl != null)
            {
                return _topPanelMediaControl.GetTopPanelItems();
            }

            return Enumerable.Empty<TopPanelItem>();
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
                    _fileLogger?.Debug($"DownloadMusicForAllGames: Starting download for {gamesWithoutMusic.Count} game(s)");

                    // Get currently selected game for playback resume after batch download
                    var currentGame = SelectedGames?.FirstOrDefault();

                    // Use the new parallel batch download dialog with Material Design UI
                    var maxConcurrent = _settings?.MaxConcurrentDownloads ?? 3;
                    var batchResult = _downloadDialogService?.ShowBatchDownloadDialogWithResults(
                        gamesWithoutMusic,
                        Models.Source.All,
                        overwrite: false,
                        maxConcurrentDownloads: maxConcurrent,
                        currentGame: currentGame);

                    var allDownloadedPaths = new List<string>();

                    _fileLogger?.Debug($"DownloadMusicForAllGames: batchResult={batchResult != null}, SuccessCount={batchResult?.SuccessCount}, FailedCount={batchResult?.FailedCount}, FailedGames.Count={batchResult?.FailedGames?.Count}");

                    if (batchResult != null)
                    {
                        allDownloadedPaths.AddRange(batchResult.DownloadedFiles);

                        // Prompt for retry if there were failures
                        if (batchResult.FailedGames.Count > 0)
                        {
                            _fileLogger?.Debug($"DownloadMusicForAllGames: {batchResult.FailedGames.Count} games failed, prompting for retry");
                            var retryDownloads = _downloadDialogService?.PromptAndRetryFailedDownloads(batchResult.FailedGames);
                            if (retryDownloads != null && retryDownloads.Count > 0)
                            {
                                allDownloadedPaths.AddRange(retryDownloads);
                            }
                        }
                        else
                        {
                            _fileLogger?.Debug("DownloadMusicForAllGames: No failed games to retry");
                        }
                    }
                    else
                    {
                        _fileLogger?.Warn("DownloadMusicForAllGames: batchResult is null");
                    }

                    // Trigger auto-normalize if any files were downloaded
                    if (allDownloadedPaths.Count > 0)
                    {
                        _fileLogger?.Debug($"DownloadMusicForAllGames: {allDownloadedPaths.Count} files downloaded, triggering auto-normalize");
                        _downloadDialogService?.AutoNormalizeDownloadedFiles(allDownloadedPaths);
                    }
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
                    _fileLogger?.Debug($"BulkDownload: Cancelled by user at {i}/{totalGames}");
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
                    _fileLogger?.Debug($"BulkDownload: Cancelled during '{game.Name}'");
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

            _fileLogger?.Debug($"BulkDownload: Completed - Success: {successCount}, Skipped: {skipCount}, Failed: {failCount}");

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
                        extension = Constants.DefaultAudioExtension;
                    var downloadPath = Path.Combine(gameDir, safeFileName + extension);

                    // Download the song
                    var success = _downloadManager.DownloadSong(bestSong, downloadPath, cts.Token, isPreview: false);
                    if (success)
                    {
                        _fileLogger?.Debug($"AutoDownloadSync: Successfully downloaded '{bestSong.Name}' for '{game.Name}'");

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
        /// Gets the search hints service.
        /// </summary>
        public SearchHintsService GetSearchHintsService() => _hintsService;

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
        /// Gets the game music tag service.
        /// </summary>
        public Services.GameMusicTagService GetGameMusicTagService() => _tagService;

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
                        _fileLogger?.Debug("DeleteAllMusic: Deleted PreservedOriginals folder");
                    }
                    catch (Exception ex)
                    {
                        _fileLogger?.Warn($"DeleteAllMusic: Failed to delete PreservedOriginals - {ex.Message}");
                    }
                }

                _fileLogger?.Debug($"DeleteAllMusic: Deleted {deletedFiles} files in {deletedFolders} folders");
                return (deletedFiles, deletedFolders, true);
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DeleteAllMusic: Error - {ex.Message}", ex);
                return (0, 0, false);
            }
        }

        /// <summary>
        /// Scans the Games music folder for orphaned directories (music for games no longer in the library)
        /// and deletes them.
        /// </summary>
        /// <returns>Tuple of (deletedFolders, deletedFiles, success)</returns>
        public (int deletedFolders, int deletedFiles, bool success) CleanupOrphanedMusic()
        {
            try
            {
                if (!Directory.Exists(_gamesPath))
                {
                    _fileLogger?.Debug("CleanupOrphanedMusic: Games path does not exist, nothing to clean");
                    return (0, 0, true);
                }

                var gameDirs = Directory.GetDirectories(_gamesPath);
                int deletedFolders = 0;
                int deletedFiles = 0;

                foreach (var gameDir in gameDirs)
                {
                    try
                    {
                        var dirName = Path.GetFileName(gameDir);

                        if (!Guid.TryParse(dirName, out var gameId))
                        {
                            _fileLogger?.Debug($"CleanupOrphanedMusic: Skipping non-GUID directory '{dirName}'");
                            continue;
                        }

                        // Check if this game still exists in the database
                        var game = _api.Database.Games[gameId];
                        if (game != null)
                        {
                            continue;
                        }

                        // Game no longer exists — this is an orphaned music folder
                        // Stop playback if this orphaned folder is somehow playing
                        if (_playbackService?.CurrentGame?.Id == gameId)
                        {
                            _playbackService.Stop();
                        }

                        var fileCount = Directory.GetFiles(gameDir, "*.*", SearchOption.AllDirectories).Length;
                        Directory.Delete(gameDir, true);
                        deletedFiles += fileCount;
                        deletedFolders++;
                        _fileLogger?.Debug($"CleanupOrphanedMusic: Deleted orphaned music directory '{dirName}' ({fileCount} files)");
                    }
                    catch (Exception ex)
                    {
                        _fileLogger?.Warn($"CleanupOrphanedMusic: Failed to delete '{gameDir}' - {ex.Message}");
                    }
                }

                _fileLogger?.Debug($"CleanupOrphanedMusic: Completed - Deleted {deletedFolders} orphaned folders ({deletedFiles} files)");
                return (deletedFolders, deletedFiles, true);
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"CleanupOrphanedMusic: Error - {ex.Message}", ex);
                return (0, 0, false);
            }
        }

        /// <summary>
        /// Counts orphaned music directories (music for games no longer in the library).
        /// </summary>
        /// <returns>Number of orphaned game music directories</returns>
        public int CountOrphanedMusicFolders()
        {
            try
            {
                if (!Directory.Exists(_gamesPath))
                {
                    return 0;
                }

                int count = 0;
                var gameDirs = Directory.GetDirectories(_gamesPath);

                foreach (var gameDir in gameDirs)
                {
                    var dirName = Path.GetFileName(gameDir);
                    if (!Guid.TryParse(dirName, out var gameId))
                    {
                        continue;
                    }

                    if (_api.Database.Games[gameId] == null)
                    {
                        count++;
                    }
                }

                return count;
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"CountOrphanedMusicFolders: Error - {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Scans all music files and returns info about songs longer than the specified duration.
        /// </summary>
        /// <param name="maxMinutes">Maximum allowed duration in minutes</param>
        /// <param name="progressArgs">Optional progress args for UI updates</param>
        /// <returns>List of (filePath, duration, fileSize, gameFolder) for songs exceeding the limit</returns>
        public List<(string filePath, TimeSpan duration, long fileSize, string gameFolder)> GetLongSongs(int maxMinutes, GlobalProgressActionArgs progressArgs = null)
        {
            var longSongs = new List<(string filePath, TimeSpan duration, long fileSize, string gameFolder)>();
            var maxDuration = TimeSpan.FromMinutes(maxMinutes);

            try
            {
                var basePath = Path.Combine(_api.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName);
                var gamesPath = Path.Combine(basePath, Constants.GamesFolderName);

                if (!Directory.Exists(gamesPath))
                    return longSongs;

                // First collect all audio files
                var allFiles = new List<(string path, string gameFolder)>();
                var gameDirs = Directory.GetDirectories(gamesPath);
                foreach (var gameDir in gameDirs)
                {
                    var gameFolder = Path.GetFileName(gameDir);
                    var files = Directory.GetFiles(gameDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => Constants.SupportedAudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                    foreach (var file in files)
                    {
                        allFiles.Add((file, gameFolder));
                    }
                }

                if (progressArgs != null)
                {
                    progressArgs.ProgressMaxValue = allFiles.Count;
                }

                int processed = 0;
                foreach (var (file, gameFolder) in allFiles)
                {
                    if (progressArgs?.CancelToken.IsCancellationRequested == true)
                        break;

                    processed++;
                    if (progressArgs != null)
                    {
                        progressArgs.CurrentProgressValue = processed;
                        progressArgs.Text = $"Scanning ({processed}/{allFiles.Count}): {Path.GetFileName(file)}";
                    }

                    try
                    {
                        using (var reader = new NAudio.Wave.AudioFileReader(file))
                        {
                            if (reader.TotalTime > maxDuration)
                            {
                                var fileSize = new FileInfo(file).Length;
                                longSongs.Add((file, reader.TotalTime, fileSize, gameFolder));
                                _fileLogger?.Debug($"GetLongSongs: Found long song '{Path.GetFileName(file)}' ({reader.TotalTime:hh\\:mm\\:ss}) in {gameFolder}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _fileLogger?.Warn($"GetLongSongs: Failed to read duration of '{file}' - {ex.Message}");
                    }
                }

                _fileLogger?.Debug($"GetLongSongs: Found {longSongs.Count} songs longer than {maxMinutes} minutes (scanned {allFiles.Count} files)");
                return longSongs;
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"GetLongSongs: Error - {ex.Message}", ex);
                return longSongs;
            }
        }

        /// <summary>
        /// Deletes the specified list of long songs.
        /// </summary>
        /// <param name="longSongs">List of songs to delete (from GetLongSongs)</param>
        /// <param name="progressArgs">Optional progress args for UI updates</param>
        /// <returns>Number of deleted files, freed bytes, and success status</returns>
        public (int deletedFiles, long freedBytes, bool success) DeleteLongSongs(List<(string filePath, TimeSpan duration, long fileSize, string gameFolder)> longSongs, GlobalProgressActionArgs progressArgs = null)
        {
            try
            {
                // Stop any playing music first
                _playbackService?.Stop();

                int deletedFiles = 0;
                long freedBytes = 0;

                if (progressArgs != null)
                {
                    progressArgs.ProgressMaxValue = longSongs.Count;
                }

                int processed = 0;
                foreach (var (filePath, duration, fileSize, gameFolder) in longSongs)
                {
                    if (progressArgs?.CancelToken.IsCancellationRequested == true)
                        break;

                    processed++;
                    if (progressArgs != null)
                    {
                        progressArgs.CurrentProgressValue = processed;
                        progressArgs.Text = $"Deleting ({processed}/{longSongs.Count}): {Path.GetFileName(filePath)}";
                    }

                    try
                    {
                        File.Delete(filePath);
                        deletedFiles++;
                        freedBytes += fileSize;
                        _fileLogger?.Debug($"DeleteLongSongs: Deleted '{Path.GetFileName(filePath)}' ({duration:hh\\:mm\\:ss}, {fileSize / 1024.0 / 1024.0:F1} MB)");
                    }
                    catch (Exception ex)
                    {
                        _fileLogger?.Warn($"DeleteLongSongs: Failed to delete '{filePath}' - {ex.Message}");
                    }
                }

                _fileLogger?.Debug($"DeleteLongSongs: Deleted {deletedFiles} files, freed {freedBytes / 1024.0 / 1024.0:F1} MB");
                return (deletedFiles, freedBytes, true);
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DeleteLongSongs: Error - {ex.Message}", ex);
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
                currentSettings.PauseWhenInSystemTray = defaultSettings.PauseWhenInSystemTray;

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
                _fileLogger?.Debug("ResetSettingsToDefaults: Settings reset to defaults");

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
                _fileLogger?.Debug("FactoryReset: Starting factory reset...");

                // Stop any playing music first
                _playbackService?.Stop();

                // Delete all music files
                var (deletedFiles, deletedFolders, deleteSuccess) = DeleteAllMusic();

                // Clear the search cache
                try
                {
                    _cacheService?.ClearCache();
                    _fileLogger?.Debug("FactoryReset: Search cache cleared");
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
                        _fileLogger?.Debug("FactoryReset: Temp folder deleted");
                    }
                }
                catch (Exception ex)
                {
                    _fileLogger?.Warn($"FactoryReset: Failed to delete temp folder - {ex.Message}");
                }

                // Reset settings to defaults
                var settingsReset = ResetSettingsToDefaults();

                _fileLogger?.Debug($"FactoryReset: Complete - Deleted {deletedFiles} files in {deletedFolders} folders, settings reset: {settingsReset}");

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
                    _fileLogger?.Debug("Starting Xbox controller login monitoring");
                    
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
                    
                    _fileLogger?.Debug("Xbox controller login monitoring stopped");
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
                    _fileLogger?.Debug($"Xbox controller {buttonName} button pressed - triggering login dismiss");
                    
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
