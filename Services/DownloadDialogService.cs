using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong;
using UniPlaySong.Common;
using UniPlaySong.Downloaders;
using UniPlaySong.Models;
using UniPlaySong.ViewModels;
using UniPlaySong.Views;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Service for showing download dialogs (album/song selection)
    /// </summary>
    public class DownloadDialogService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        /// <summary>
        /// Logs a debug message only if debug logging is enabled in settings.
        /// </summary>
        private static void LogDebug(string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                Logger.Debug(message);
            }
        }

        private readonly IPlayniteAPI _playniteApi;
        private readonly IDownloadManager _downloadManager;
        private readonly IMusicPlaybackService _playbackService;
        private readonly GameMusicFileService _fileService;
        private readonly SettingsService _settingsService;
        private readonly ErrorHandlerService _errorHandler;
        private INormalizationService _normalizationService;
        private GameMusicTagService _tagService;

        /// <summary>
        /// Returns true if Playnite is in Fullscreen mode.
        /// Topmost dialogs are only needed in Fullscreen mode to appear above the fullscreen window.
        /// In Desktop mode, Topmost causes dialogs to block other applications.
        /// </summary>
        private bool IsFullscreenMode => _playniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen;

        public DownloadDialogService(
            IPlayniteAPI playniteApi,
            IDownloadManager downloadManager,
            IMusicPlaybackService playbackService,
            GameMusicFileService fileService,
            SettingsService settingsService,
            ErrorHandlerService errorHandler)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _downloadManager = downloadManager ?? throw new ArgumentNullException(nameof(downloadManager));
            _playbackService = playbackService;
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        }

        /// <summary>
        /// Sets the normalization service for auto-normalize after download feature.
        /// Called after service initialization in UniPlaySong.cs.
        /// </summary>
        public void SetNormalizationService(INormalizationService normalizationService)
        {
            _normalizationService = normalizationService;
        }

        /// <summary>
        /// Sets the tag service for updating game music status tags after downloads.
        /// Called after service initialization in UniPlaySong.cs.
        /// </summary>
        public void SetTagService(GameMusicTagService tagService)
        {
            _tagService = tagService;
        }

        /// <summary>
        /// Pre-loads Material Design assemblies to ensure they're available for XAML parsing
        /// </summary>
        private static void PreloadMaterialDesignAssemblies()
        {
            try
            {
                string extensionPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(extensionPath))
                {
                    return;
                }

                // Pre-load Material Design assemblies in order of dependency
                string[] assembliesToLoad = new[]
                {
                    "Microsoft.Xaml.Behaviors.dll",
                    "MaterialDesignColors.dll",
                    "MaterialDesignThemes.Wpf.dll"
                };

                foreach (string assemblyName in assembliesToLoad)
                {
                    string assemblyPath = Path.Combine(extensionPath, assemblyName);
                    if (File.Exists(assemblyPath))
                    {
                        try
                        {
                            Assembly.LoadFrom(assemblyPath);
                        }
                        catch
                        {
                            // Ignore if already loaded or fails
                        }
                    }
                }
            }
            catch
            {
                // Continue if pre-loading fails
            }
        }

        /// <summary>
        /// Shows source selection dialog (KHInsider or YouTube) - works in fullscreen
        /// </summary>
        public Source? ShowSourceSelectionDialog()
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            // Check if YouTube paths are configured (warn but allow selection)
            var youtubeConfigured = _settingsService.Current != null && 
                !string.IsNullOrWhiteSpace(_settingsService.Current.YtDlpPath) && 
                !string.IsNullOrWhiteSpace(_settingsService.Current.FFmpegPath) &&
                System.IO.File.Exists(_settingsService.Current.YtDlpPath);

            var sourceOptions = new List<Playnite.SDK.GenericItemOption>
            {
                new Playnite.SDK.GenericItemOption("KHInsider", "Download from KHInsider (Game soundtracks)"),
                new Playnite.SDK.GenericItemOption("YouTube", 
                    youtubeConfigured 
                        ? "Download from YouTube (Playlists and videos)" 
                        : "Download from YouTube (Playlists and videos) - yt-dlp/ffmpeg required for downloads")
            };

            // Create a simple selection dialog using our custom view
            var viewModel = new DownloadDialogViewModel(
                _playniteApi,
                _downloadManager,
                _playbackService,
                null, // No game for source selection
                Source.KHInsider, // Dummy source
                isSongSelection: false,
                album: null,
                fileService: null,
                errorHandler: _errorHandler);

            // Set up the view model with source options
            viewModel.SetSourceOptions(sourceOptions);

            var window = _playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = false,
                ShowCloseButton = true
            });

            window.Height = 400;
            window.Width = 600;
            window.Title = "Select Download Source";
            window.ShowInTaskbar = !IsFullscreenMode; // Show in taskbar for Desktop mode accessibility
            window.Topmost = IsFullscreenMode; // Only set Topmost in Fullscreen mode to avoid blocking other apps in Desktop mode

            // Set background color for fullscreen mode (fixes transparency issue)
            window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 33, 33));

            var view = new DownloadDialogView();
            window.Content = view;
            window.DataContext = viewModel;
            var ownerWindow = _playniteApi.Dialogs.GetCurrentAppWindow();
            window.Owner = ownerWindow;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Handle confirm/cancel
            viewModel.ConfirmCommand = new Common.RelayCommand(() =>
            {
                window.DialogResult = true;
                window.Close();
            }, () => viewModel.GetSelectedItems().Count > 0);

            viewModel.CancelCommand = new Common.RelayCommand(() =>
            {
                window.DialogResult = false;
                window.Close();
            });

            window.Loaded += (s, e) =>
            {
                window.Activate();
                window.Focus();
            };

            var result = window.ShowDialog();
            if (result == true)
            {
                var selected = viewModel.GetSelectedItems();
                if (selected.Count > 0)
                {
                    var selectedItem = selected[0];
                    if (selectedItem is Playnite.SDK.GenericItemOption selectedOption)
                    {
                        LogDebug($"User selected source option: {selectedOption.Name}");

                        if (selectedOption.Name == "KHInsider")
                        {
                            LogDebug("Returning Source.KHInsider");
                            return Source.KHInsider;
                        }
                        else if (selectedOption.Name == "YouTube")
                        {
                            LogDebug("YouTube selected - validating configuration...");
                            // Validate YouTube configuration before returning
                            if (_settingsService.Current == null || string.IsNullOrWhiteSpace(_settingsService.Current.YtDlpPath) || string.IsNullOrWhiteSpace(_settingsService.Current.FFmpegPath))
                            {
                                Logger.Warn("YouTube paths not configured");
                                _playniteApi.Dialogs.ShowMessage(
                                    "YouTube downloads require yt-dlp and ffmpeg to be configured.\n\n" +
                                    "Please set the paths in Settings → UniPlaySong → YouTube Download Settings.",
                                    "YouTube Configuration Required");
                                return null; // Prevent proceeding with YouTube if not configured
                            }
                            
                            if (!System.IO.File.Exists(_settingsService.Current.YtDlpPath))
                            {
                                Logger.Warn($"yt-dlp not found at: {_settingsService.Current.YtDlpPath}");
                                _playniteApi.Dialogs.ShowMessage(
                                    $"yt-dlp not found at:\n{_settingsService.Current.YtDlpPath}\n\n" +
                                    "Please check the path in Settings → UniPlaySong → YouTube Download Settings.",
                                    "yt-dlp Not Found");
                                return null; // Prevent proceeding if yt-dlp not found
                            }
                            
                            LogDebug("YouTube configuration validated successfully");
                            return Source.YouTube;
                        }
                        else
                        {
                            Logger.Warn($"Unknown source option selected: {selectedOption.Name}");
                        }
                    }
                    else
                    {
                        Logger.Warn($"Selected item is not a GenericItemOption: {selectedItem?.GetType().Name ?? "null"}");
                    }
                }
                else
                {
                    Logger.Warn("No items selected in source selection dialog");
                }
            }
            else
            {
                LogDebug("Source selection dialog cancelled or closed");
            }

            return null;
        }

        /// <summary>
        /// Shows unified album selection dialog that searches BOTH KHInsider and YouTube
        /// Used for retry feature to simplify the user experience
        /// </summary>
        public Album ShowUnifiedAlbumSelectionDialog(Game game)
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            LogDebug($"ShowUnifiedAlbumSelectionDialog called for game: {game?.Name ?? "null"}");

            LogDebug("Creating unified DownloadDialogViewModel...");
            DownloadDialogViewModel viewModel = _errorHandler.Try(
                () => new DownloadDialogViewModel(
                    _playniteApi,
                    _downloadManager,
                    _playbackService,
                    game,
                    Source.All, // Search both sources
                    isSongSelection: false,
                    album: null,
                    fileService: null,
                    errorHandler: _errorHandler),
                defaultValue: null,
                context: $"creating unified download dialog for game '{game?.Name ?? "null"}'",
                showUserMessage: true
            );

            if (viewModel == null)
            {
                return null;
            }

            LogDebug("Unified DownloadDialogViewModel created successfully");

            return _errorHandler.Try(
                () =>
                {
                    var window = _playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                    {
                        ShowMinimizeButton = false,
                        ShowMaximizeButton = true,
                        ShowCloseButton = true
                    });

                    window.Height = 600;
                    window.Width = 800;
                    window.Title = $"Find Music for: {game.Name}";
                    window.ShowInTaskbar = !IsFullscreenMode; // Show in taskbar for Desktop mode accessibility
                    window.Topmost = IsFullscreenMode; // Only set Topmost in Fullscreen mode

                    // Set background color for fullscreen mode
                    window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 33, 33));

                    var view = new DownloadDialogView();
                    window.Content = view;
                    window.DataContext = viewModel;
                    var ownerWindow = _playniteApi.Dialogs.GetCurrentAppWindow();
                    window.Owner = ownerWindow;
                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                    // Trigger unified search when window loads
                    window.Loaded += (s, e) =>
                    {
                        _errorHandler.Try(
                            () =>
                            {
                                window.Activate();
                                window.Focus();
                                LogDebug("Unified album selection window loaded and activated");

                                // Trigger search after window is loaded
                                if (game != null)
                                {
                                    var app = System.Windows.Application.Current;
                                    if (app?.Dispatcher != null)
                                    {
                                        app.Dispatcher.BeginInvoke(new Action(() =>
                                        {
                                            _errorHandler.Try(
                                                () =>
                                                {
                                                    // Set search term and trigger unified search
                                                    viewModel.SearchTerm = game.Name;
                                                    viewModel.PerformSearch();
                                                },
                                                context: "unified auto-search after window load"
                                            );
                                        }), System.Windows.Threading.DispatcherPriority.Loaded);
                                    }
                                }
                            },
                            context: "handling unified window loaded event"
                        );
                    };

                    // Handle confirm/cancel via commands
                    viewModel.ConfirmCommand = new Common.RelayCommand(() =>
                    {
                        window.DialogResult = true;
                        window.Close();
                    }, () => viewModel.GetSelectedItems().Count > 0);

                    viewModel.CancelCommand = new Common.RelayCommand(() =>
                    {
                        viewModel.CleanupPreviewFiles();
                        window.DialogResult = false;
                        window.Close();
                    });

                    // No back button for unified search (can't go back to source selection)
                    viewModel.ShowBackButton = false;

                    window.Closing += (s, e) =>
                    {
                        viewModel.CleanupPreviewFiles();
                    };

                    LogDebug("Showing unified album selection window...");
                    var result = window.ShowDialog();
                    LogDebug($"Unified album selection window closed with result: {result}");

                    if (result == true)
                    {
                        var selected = viewModel.GetSelectedItems();
                        var album = selected.FirstOrDefault() as Album;
                        if (album != null)
                        {
                            LogDebug($"User selected album: {album.Name} from {album.Source}");
                            return album;
                        }
                    }

                    LogDebug("User cancelled unified album selection or no album selected");
                    return null;
                },
                defaultValue: null,
                context: $"showing unified album selection dialog for '{game?.Name ?? "null"}'",
                showUserMessage: true
            );
        }

        /// <summary>
        /// Shows album selection dialog and returns selected album
        /// </summary>
        public Album ShowAlbumSelectionDialog(Game game, Source source)
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();
            
            LogDebug($"ShowAlbumSelectionDialog called for game: {game?.Name ?? "null"}, source: {source}");
            
            // YouTube configuration is now validated in ShowSourceSelectionDialog before YouTube can be selected
            // So if we reach here with YouTube source, it should be valid
            if (source == Source.YouTube)
            {
                LogDebug("YouTube source confirmed - configuration already validated");
            }

            LogDebug("Creating DownloadDialogViewModel...");
            DownloadDialogViewModel viewModel = _errorHandler.Try(
                () => new DownloadDialogViewModel(
                    _playniteApi,
                    _downloadManager,
                    _playbackService,
                    game,
                    source,
                    isSongSelection: false,
                    album: null,
                    fileService: null,
                    errorHandler: _errorHandler),
                defaultValue: null,
                context: $"creating download dialog for game '{game?.Name ?? "null"}' with source {source}",
                showUserMessage: true
            );
            
            if (viewModel == null)
            {
                return null;
            }
            
            LogDebug("DownloadDialogViewModel created successfully");
            
            LogDebug("Creating album selection window...");

            return _errorHandler.Try(
                () =>
                {
                    var window = _playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                    {
                        ShowMinimizeButton = false,
                        ShowMaximizeButton = true,
                        ShowCloseButton = true
                    });

                    window.Height = 500;
                    window.Width = 700;
                    window.Title = $"Select Album for {game.Name}";
                    window.ShowInTaskbar = !IsFullscreenMode; // Show in taskbar for Desktop mode accessibility
                    window.Topmost = IsFullscreenMode; // Only set Topmost in Fullscreen mode to avoid blocking other apps

                    // Set background color for fullscreen mode (fixes transparency issue)
                    window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 33, 33));
                    
                    var view = new DownloadDialogView();
                    window.Content = view;
                    window.DataContext = viewModel;
                    var ownerWindow = _playniteApi.Dialogs.GetCurrentAppWindow();
                    window.Owner = ownerWindow;
                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    
                    // Ensure window is activated and focused in fullscreen
                    // Also trigger search when window loads (PNS pattern - WindowOpenedCommand)
                    window.Loaded += (s, e) =>
                    {
                        _errorHandler.Try(
                            () =>
                            {
                                window.Activate();
                                window.Focus();
                                LogDebug("Album selection window loaded and activated");
                                
                                // Trigger search after window is loaded (similar to PNS WindowOpenedCommand)
                                // This ensures window is visible and responsive before search starts
                                if (game != null)
                                {
                                    // Use BeginInvoke to defer search until after window is fully rendered
                                    var app = System.Windows.Application.Current;
                                    if (app?.Dispatcher != null)
                                    {
                                        app.Dispatcher.BeginInvoke(new Action(() =>
                                        {
                                            _errorHandler.Try(
                                                () =>
                                                {
                                                    // Set search term to game name and trigger search
                                                    viewModel.SearchTerm = game.Name;
                                                    viewModel.PerformSearch();
                                                },
                                                context: "auto-search after window load"
                                            );
                                        }), System.Windows.Threading.DispatcherPriority.Loaded);
                                    }
                                }
                            },
                            context: "activating album selection window"
                        );
                    };

                    Album doubleClickedAlbum = null;
                    
                    // Handle double-click on album to open songs
                    viewModel.OnAlbumDoubleClicked += (album) =>
                    {
                        doubleClickedAlbum = album;
                        window.DialogResult = true;
                        window.Close();
                    };

                    // Handle confirm/cancel via commands
                    viewModel.ConfirmCommand = new Common.RelayCommand(() =>
                    {
                        window.DialogResult = true;
                        window.Close();
                    }, () => viewModel.GetSelectedItems().Count > 0);

                    viewModel.CancelCommand = new Common.RelayCommand(() =>
                    {
                        viewModel.CleanupPreviewFiles(); // Clean up preview files before closing
                        window.DialogResult = false;
                        window.Close();
                    });

                    // Enable back button for album selection (allows going back to source selection)
                    viewModel.ShowBackButton = true;
                    viewModel.BackCommand = new Common.RelayCommand(() =>
                    {
                        viewModel.CleanupPreviewFiles(); // Clean up preview files before closing
                        viewModel.BackWasPressed = true; // Signal that user wants to go back to source selection
                        window.DialogResult = false;
                        window.Close();
                    });

                    // Clean up preview files when window closes
                    window.Closing += (s, e) =>
                    {
                        viewModel.CleanupPreviewFiles();
                    };

                    LogDebug("Showing album selection dialog...");
                    var result = window.ShowDialog();
                    LogDebug($"Album selection dialog result: {result}");
                
                    if (result == true)
                    {
                        // If double-clicked, use that album
                        if (doubleClickedAlbum != null)
                        {
                            LogDebug($"Album double-clicked: {doubleClickedAlbum.Name}");
                            return doubleClickedAlbum;
                        }

                        var selected = viewModel.GetSelectedItems();
                        var album = selected.FirstOrDefault() as Album;
                        if (album != null)
                        {
                            LogDebug($"Album selected: {album.Name} (ID: {album.Id})");
                        }
                        else
                        {
                            LogDebug("No album selected from dialog");
                        }
                        return album;
                    }

                    // Return BackSignal when Back is pressed - signals caller to go back to source selection
                    if (viewModel.BackWasPressed)
                    {
                        LogDebug("Album selection dialog: user pressed back - returning to source selection");
                        return Album.BackSignal;
                    }

                    LogDebug("Album selection dialog cancelled");
                    return null;
                },
                defaultValue: null,
                context: $"showing album selection dialog for game '{game?.Name ?? "null"}' with source {source}",
                showUserMessage: true
            );
        }

        /// <summary>
        /// Shows song selection dialog and returns selected songs
        /// </summary>
        public List<Song> ShowSongSelectionDialog(Game game, Album album)
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();
            
            var viewModel = new DownloadDialogViewModel(
                _playniteApi,
                _downloadManager,
                _playbackService,
                game,
                album.Source,
                isSongSelection: true,
                album: album,
                fileService: _fileService,
                errorHandler: _errorHandler);

            var window = _playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = true,
                ShowCloseButton = true
            });

            window.Height = 500;
            window.Width = 700;
            window.Title = $"Select Songs from {album.Name}";
            window.ShowInTaskbar = !IsFullscreenMode; // Show in taskbar for Desktop mode accessibility
            window.Topmost = IsFullscreenMode; // Only set Topmost in Fullscreen mode to avoid blocking other apps
            
            // Set background color for fullscreen mode (fixes transparency issue)
            window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 33, 33));
            
            var view = new DownloadDialogView();
            window.Content = view;
            window.DataContext = viewModel;
            var ownerWindow = _playniteApi.Dialogs.GetCurrentAppWindow();
            window.Owner = ownerWindow;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            
            // Ensure window is activated and focused in fullscreen
            // Also trigger search to load songs when window loads
            window.Loaded += (s, e) =>
            {
                _errorHandler.Try(
                    () =>
                    {
                        window.Activate();
                        window.Focus();
                        LogDebug("Song selection window loaded and activated");
                        
                        // Trigger search after window is loaded to load songs from album
                        // This matches the pattern we use for album selection
                        if (album != null)
                        {
                            var app = System.Windows.Application.Current;
                            if (app?.Dispatcher != null)
                            {
                                app.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    _errorHandler.Try(
                                        () =>
                                        {
                                            LogDebug($"Triggering song search for album: {album.Name}");
                                            viewModel.PerformSearch();
                                        },
                                        context: "auto-search for songs after window load"
                                    );
                                }), System.Windows.Threading.DispatcherPriority.Loaded);
                            }
                        }
                    },
                    context: "activating song selection window"
                );
            };

            // Enable back button for song selection (allows going back to album selection)
            viewModel.ShowBackButton = true;
            viewModel.BackCommand = new Common.RelayCommand(() =>
            {
                viewModel.CleanupPreviewFiles(); // Clean up preview files before closing
                viewModel.BackWasPressed = true; // Signal that user wants to go back, not cancel entirely
                window.DialogResult = false;
                window.Close();
            });

            // Handle confirm/cancel via commands
            // For song selection, confirm triggers inline download instead of closing
            viewModel.OnDownloadComplete = (success) =>
            {
                // After successful download, trigger music refresh so music plays immediately
                // This addresses the issue where music doesn't play after download completes
                if (success && game != null && _playbackService != null)
                {
                    _errorHandler.Try(
                        () =>
                        {
                            LogDebug($"Download complete - triggering music refresh for game: {game.Name}");
                            // Get current settings to pass to PlayGameMusic
                            var settings = _settingsService?.Current;
                            _playbackService.PlayGameMusic(game, settings, forceReload: true);
                        },
                        context: $"refreshing music after download for '{game.Name}'"
                    );
                }
            };

            // Set up callback for auto-normalization and tag update after download
            viewModel.OnFilesDownloaded = (downloadedFiles) =>
            {
                AutoNormalizeDownloadedFiles(downloadedFiles);

                // Update the game's music status tag after download
                if (_tagService != null && game != null)
                {
                    try
                    {
                        _tagService.UpdateGameMusicTag(game);
                        LogDebug($"Updated music tag for game '{game.Name}' after download");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, $"Failed to update music tag for game '{game.Name}': {ex.Message}");
                    }
                }
            };

            viewModel.ConfirmCommand = new Common.RelayCommand(() =>
            {
                // For song selection, trigger inline download
                if (viewModel.GetSelectedItems().OfType<Song>().Any())
                {
                    viewModel.DownloadSelectedSongs();
                }
                else
                {
                    // For other dialogs (source/album), close normally
                    window.DialogResult = true;
                    window.Close();
                }
            }, () => viewModel.GetSelectedItems().Count > 0);

            viewModel.CancelCommand = new Common.RelayCommand(() =>
            {
                viewModel.CleanupPreviewFiles(); // Clean up preview files before closing
                window.DialogResult = false;
                window.Close();
            });

            // Clean up preview files when window closes
            window.Closing += (s, e) =>
            {
                viewModel.CleanupPreviewFiles();
            };

            var result = window.ShowDialog();

            // Return null when Back is pressed - signals caller to go back to album selection
            if (viewModel.BackWasPressed)
            {
                LogDebug("Song selection dialog: user pressed back - returning to album selection");
                return null;
            }

            // Return empty list for cancel/confirm/download complete - downloads are handled inline
            return new List<Song>();
        }

        /// <summary>
        /// Shows song selection dialog and returns selected songs (for batch download)
        /// Unlike ShowSongSelectionDialog, this returns the selected songs instead of downloading inline
        /// </summary>
        public List<Song> ShowSongSelectionDialogWithReturn(Game game, Album album)
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            LogDebug($"ShowSongSelectionDialogWithReturn called for game: {game?.Name ?? "null"}, album: {album?.Name ?? "null"}");

            DownloadDialogViewModel viewModel = _errorHandler.Try(
                () => new DownloadDialogViewModel(
                    _playniteApi,
                    _downloadManager,
                    _playbackService,
                    game,
                    album.Source,
                    isSongSelection: true,
                    album: album,
                    fileService: _fileService,
                    errorHandler: _errorHandler),
                defaultValue: null,
                context: $"creating song selection dialog for batch download (album: {album?.Name ?? "null"})",
                showUserMessage: true
            );

            if (viewModel == null)
            {
                return new List<Song>();
            }

            var window = _playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = true,
                ShowCloseButton = true
            });

            window.Height = 500;
            window.Width = 700;
            window.Title = $"Select Songs from {album.Name}";
            window.ShowInTaskbar = !IsFullscreenMode; // Show in taskbar for Desktop mode accessibility
            window.Topmost = IsFullscreenMode; // Only set Topmost in Fullscreen mode
            window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 33, 33));

            var view = new DownloadDialogView();
            window.Content = view;
            window.DataContext = viewModel;
            var ownerWindow = _playniteApi.Dialogs.GetCurrentAppWindow();
            if (ownerWindow != null)
            {
                window.Owner = ownerWindow;
            }
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Trigger search to load songs when window loads
            window.Loaded += (s, e) =>
            {
                _errorHandler.Try(
                    () =>
                    {
                        window.Activate();
                        window.Focus();
                        LogDebug("Batch song selection window loaded and activated");

                        if (album != null)
                        {
                            var app = System.Windows.Application.Current;
                            if (app?.Dispatcher != null)
                            {
                                app.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    _errorHandler.Try(
                                        () =>
                                        {
                                            LogDebug($"Triggering song search for album: {album.Name}");
                                            viewModel.PerformSearch();
                                        },
                                        context: "auto-search for songs in batch dialog"
                                    );
                                }), System.Windows.Threading.DispatcherPriority.Loaded);
                            }
                        }
                    },
                    context: "activating batch song selection window"
                );
            };

            List<Song> selectedSongs = new List<Song>();

            // Enable back button
            viewModel.ShowBackButton = true;
            viewModel.BackCommand = new Common.RelayCommand(() =>
            {
                viewModel.CleanupPreviewFiles();
                window.DialogResult = false;
                window.Close();
            });

            // Handle confirm - return selected songs instead of downloading inline
            viewModel.ConfirmCommand = new Common.RelayCommand(() =>
            {
                selectedSongs = viewModel.GetSelectedItems().OfType<Song>().ToList();
                if (selectedSongs.Count > 0)
                {
                    window.DialogResult = true;
                    window.Close();
                }
            }, () => viewModel.GetSelectedItems().Count > 0);

            viewModel.CancelCommand = new Common.RelayCommand(() =>
            {
                viewModel.CleanupPreviewFiles();
                window.DialogResult = false;
                window.Close();
            });

            window.Closing += (s, e) =>
            {
                viewModel.CleanupPreviewFiles();
            };

            var result = window.ShowDialog();
            
            if (result == true)
            {
                LogDebug($"User selected {selectedSongs.Count} songs for batch download");
                return selectedSongs;
            }

            return new List<Song>();
        }

        /// <summary>
        /// Shows download dialog for default music (no game context)
        /// Downloads directly to the specified default music path
        /// </summary>
        public bool ShowDefaultMusicDownloadDialog(string defaultMusicPath)
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            LogDebug("ShowDefaultMusicDownloadDialog called");

            if (_downloadManager == null)
            {
                _playniteApi.Dialogs.ShowMessage("Download manager not initialized. Please check extension settings.");
                return false;
            }

            // Step 1: Select source (KHInsider or YouTube)
            Source? sourceNullable = ShowSourceSelectionDialog();
            if (sourceNullable == null)
            {
                LogDebug("User cancelled source selection");
                return false;
            }

            var source = sourceNullable.Value;
            LogDebug($"User selected source: {source}");

            // Step 2: For default music, use a dummy game and let users search in the album dialog
            // This is more intuitive - users can type the game name in the search box
            Game dummyGame = new Game { Name = "", Id = Guid.NewGuid() }; // Empty name - user will search

            // Step 3: Select album (with search functionality - no auto-search, user types game name)
            Album album = ShowAlbumSelectionDialogForDefaultMusic(source);
            if (album == null)
            {
                LogDebug("User cancelled album selection");
                return false;
            }

            // Step 4: Select song (using custom dialog that returns selected songs)
            var selectedSong = ShowSongSelectionForDefaultMusic(dummyGame, album);
            if (selectedSong == null)
            {
                LogDebug("No song selected or download cancelled");
                return false;
            }

            return _errorHandler.Try(
                () =>
                {
                    // Sanitize filename for default music
                    var sanitizedName = selectedSong.Name;
                    foreach (var invalidChar in System.IO.Path.GetInvalidFileNameChars())
                    {
                        sanitizedName = sanitizedName.Replace(invalidChar, '_');
                    }
                    sanitizedName = sanitizedName.Replace("..", "_").Trim();

                    // Use the provided default music path, or create filename if it's a directory
                    string downloadPath;
                    if (System.IO.Directory.Exists(defaultMusicPath))
                    {
                        // If it's a directory, create filename
                        var fileName = $"{sanitizedName}.mp3";
                        downloadPath = System.IO.Path.Combine(defaultMusicPath, fileName);
                    }
                    else
                    {
                        // If it's a file path, use it directly (will overwrite)
                        downloadPath = defaultMusicPath;
                    }

                    LogDebug($"Downloading default music to: {downloadPath}");

                    // Show progress dialog
                    var progressOptions = new Playnite.SDK.GlobalProgressOptions(
                        $"Downloading default music: {selectedSong.Name}...",
                        true
                    );

                    bool downloadSuccess = false;
                    _playniteApi.Dialogs.ActivateGlobalProgress((progress) =>
                    {
                        var cancellationToken = progress.CancelToken;
                        downloadSuccess = _downloadManager.DownloadSong(selectedSong, downloadPath, cancellationToken);
                    }, progressOptions);

                    if (downloadSuccess && System.IO.File.Exists(downloadPath))
                    {
                        LogDebug($"Default music downloaded successfully: {downloadPath}");
                        _playniteApi.Dialogs.ShowMessage(
                            $"Default music downloaded successfully!\n\nFile: {System.IO.Path.GetFileName(downloadPath)}\nLocation: {downloadPath}",
                            "Download Complete");
                        return true;
                    }
                    else
                    {
                        Logger.Warn("Default music download failed or file not found");
                        _playniteApi.Dialogs.ShowErrorMessage(
                            "Failed to download default music. Please check the logs for details.",
                            "Download Failed");
                        return false;
                    }
                },
                defaultValue: false,
                context: $"downloading default music: {selectedSong?.Name ?? "unknown"}",
                showUserMessage: true
            );
        }

        /// <summary>
        /// Shows song selection dialog and returns the selected song (for default music downloads)
        /// Unlike ShowSongSelectionDialog, this returns the selected song instead of downloading inline
        /// </summary>
        private Song ShowSongSelectionForDefaultMusic(Game game, Album album)
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            LogDebug($"ShowSongSelectionForDefaultMusic called for game: {game?.Name ?? "null"}, album: {album?.Name ?? "null"}");

            DownloadDialogViewModel viewModel = _errorHandler.Try(
                () => new DownloadDialogViewModel(
                    _playniteApi,
                    _downloadManager,
                    _playbackService,
                    game,
                    album.Source,
                    isSongSelection: true,
                    album: album,
                    fileService: _fileService,
                    errorHandler: _errorHandler),
                defaultValue: null,
                context: $"creating download dialog for default music (album: {album?.Name ?? "null"})",
                showUserMessage: true
            );
            
            if (viewModel == null)
            {
                return null;
            }
            
            LogDebug("DownloadDialogViewModel created successfully");

            var window = _playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = true,
                ShowCloseButton = true
            });

            window.Height = 500;
            window.Width = 700;
            window.Title = $"Select Song for Default Music";
            window.ShowInTaskbar = !IsFullscreenMode; // Show in taskbar for Desktop mode accessibility
            window.Topmost = IsFullscreenMode; // Only set Topmost in Fullscreen mode
            window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 33, 33));

            var view = new DownloadDialogView();
            window.Content = view;
            window.DataContext = viewModel;
            var ownerWindow = _playniteApi.Dialogs.GetCurrentAppWindow();
            if (ownerWindow != null)
            {
                window.Owner = ownerWindow;
            }
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Trigger search to load songs when window loads
            window.Loaded += (s, e) =>
            {
                _errorHandler.Try(
                    () =>
                    {
                        window.Activate();
                        window.Focus();
                        LogDebug("Default music song selection window loaded and activated");
                        
                        // Trigger search after window is loaded to load songs from album
                        if (album != null)
                        {
                            var app = System.Windows.Application.Current;
                            if (app?.Dispatcher != null)
                            {
                                app.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    _errorHandler.Try(
                                        () =>
                                        {
                                            LogDebug($"Triggering song search for album: {album.Name}");
                                            viewModel.PerformSearch();
                                        },
                                        context: "auto-search for songs in default music dialog"
                                    );
                                }), System.Windows.Threading.DispatcherPriority.Loaded);
                            }
                        }
                    },
                    context: "activating default music song selection window"
                );
            };

            Song selectedSong = null;

            // Handle confirm - return selected song instead of downloading
            viewModel.ConfirmCommand = new Common.RelayCommand(() =>
            {
                var selected = viewModel.GetSelectedItems().OfType<Song>().ToList();
                if (selected.Count > 0)
                {
                    selectedSong = selected[0]; // Take first selected song
                    window.DialogResult = true;
                    window.Close();
                }
            }, () => viewModel.GetSelectedItems().Count > 0);

            viewModel.CancelCommand = new Common.RelayCommand(() =>
            {
                viewModel.CleanupPreviewFiles();
                window.DialogResult = false;
                window.Close();
            });

            window.Closing += (s, e) =>
            {
                viewModel.CleanupPreviewFiles();
            };

            var result = window.ShowDialog();
            return selectedSong;
        }

        /// <summary>
        /// Shows album selection dialog for default music downloads
        /// Unlike regular album selection, this doesn't auto-search - user types game name in search box
        /// </summary>
        private Album ShowAlbumSelectionDialogForDefaultMusic(Source source)
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();
            
            LogDebug($"ShowAlbumSelectionDialogForDefaultMusic called for source: {source}");

            // Create a dummy game with empty name - user will search
            Game dummyGame = new Game { Name = "", Id = Guid.NewGuid() };

            DownloadDialogViewModel viewModel = _errorHandler.Try(
                () => new DownloadDialogViewModel(
                    _playniteApi,
                    _downloadManager,
                    _playbackService,
                    dummyGame,
                    source,
                    isSongSelection: false,
                    album: null,
                    fileService: null,
                    errorHandler: _errorHandler),
                defaultValue: null,
                context: $"creating download dialog for default music with source {source}",
                showUserMessage: true
            );
            
            if (viewModel == null)
            {
                return null;
            }
            
            LogDebug("DownloadDialogViewModel created successfully");

            var window = _playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = true,
                ShowCloseButton = true
            });

            window.Height = 500;
            window.Width = 700;
            window.Title = source == Source.KHInsider
                ? "Search for Game Soundtrack (Default Music)"
                : "Enter YouTube URL (Default Music)";
            window.ShowInTaskbar = !IsFullscreenMode; // Show in taskbar for Desktop mode accessibility
            window.Topmost = IsFullscreenMode; // Only set Topmost in Fullscreen mode
            window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 33, 33));

            var view = new DownloadDialogView();
            window.Content = view;
            window.DataContext = viewModel;
            var ownerWindow = _playniteApi.Dialogs.GetCurrentAppWindow();
            if (ownerWindow != null)
            {
                window.Owner = ownerWindow;
            }
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Update title to be more descriptive
            viewModel.Title = source == Source.KHInsider
                ? "Enter game name to search for soundtracks"
                : "Enter YouTube playlist or video URL";

            // Don't auto-search - let user type in search box
            window.Loaded += (s, e) =>
            {
                _errorHandler.Try(
                    () =>
                    {
                        window.Activate();
                        window.Focus();
                        LogDebug("Default music album selection window loaded");
                        // Don't trigger auto-search - user will type in search box
                    },
                    context: "activating default music album selection window"
                );
            };

            Album selectedAlbum = null;

            // Handle double-click on album
            viewModel.OnAlbumDoubleClicked += (album) =>
            {
                selectedAlbum = album;
                window.DialogResult = true;
                window.Close();
            };

            // Handle confirm/cancel
            viewModel.ConfirmCommand = new Common.RelayCommand(() =>
            {
                var selected = viewModel.GetSelectedItems();
                if (selected.Count > 0 && selected[0] is Album album)
                {
                    selectedAlbum = album;
                    window.DialogResult = true;
                    window.Close();
                }
            }, () => viewModel.GetSelectedItems().Count > 0);

            viewModel.CancelCommand = new Common.RelayCommand(() =>
            {
                window.DialogResult = false;
                window.Close();
            });

            var result = window.ShowDialog();
            return selectedAlbum;
        }

        /// <summary>
        /// Shows a dialog to download audio from a specific YouTube URL
        /// </summary>
        /// <param name="game">The game to download music for</param>
        public void ShowDownloadFromUrlDialog(Game game)
        {
            if (game == null)
            {
                Logger.Warn("ShowDownloadFromUrlDialog called with null game");
                return;
            }

            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            LogDebug($"ShowDownloadFromUrlDialog called for game: {game.Name}");

            // Check if yt-dlp is configured
            var ytDlpPath = _settingsService.Current?.YtDlpPath;
            var ffmpegPath = _settingsService.Current?.FFmpegPath;

            if (string.IsNullOrWhiteSpace(ytDlpPath) || !System.IO.File.Exists(ytDlpPath))
            {
                _playniteApi.Dialogs.ShowErrorMessage(
                    "yt-dlp is not configured or not found.\n\n" +
                    "Please configure the yt-dlp path in UniPlaySong settings to use the Download From URL feature.",
                    "UniPlaySong - Configuration Required");
                return;
            }

            if (string.IsNullOrWhiteSpace(ffmpegPath) || !System.IO.File.Exists(ffmpegPath))
            {
                _playniteApi.Dialogs.ShowErrorMessage(
                    "FFmpeg is not configured or not found.\n\n" +
                    "Please configure the FFmpeg path in Settings → Audio Normalization (or Downloads tab) to use the Download From URL feature.",
                    "UniPlaySong - Configuration Required");
                return;
            }

            // Create the view model
            var viewModel = new ViewModels.DownloadFromUrlViewModel(
                _playniteApi,
                _downloadManager,
                _fileService,
                _settingsService,
                _errorHandler,
                _playbackService,
                game);

            // Create the window
            var window = _playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = false,
                ShowCloseButton = true
            });

            window.Height = 380;
            window.Width = 580;
            window.Title = $"Download From URL - {game.Name}";
            window.ShowInTaskbar = !IsFullscreenMode; // Show in taskbar for Desktop mode accessibility
            window.Topmost = IsFullscreenMode;
            window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 33, 33));
            window.ResizeMode = ResizeMode.NoResize;

            var view = new Views.DownloadFromUrlDialog();
            window.Content = view;
            window.DataContext = viewModel;

            var ownerWindow = _playniteApi.Dialogs.GetCurrentAppWindow();
            if (ownerWindow != null)
            {
                window.Owner = ownerWindow;
            }
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Handle window loaded
            window.Loaded += (s, e) =>
            {
                window.Activate();
                window.Focus();
            };

            // Handle download command
            viewModel.DownloadCommand = new Common.RelayCommand(async () =>
            {
                await viewModel.DownloadAsync();
            }, () => viewModel.IsValidUrl && !viewModel.IsDownloading);

            // Handle cancel command
            viewModel.CancelCommand = new Common.RelayCommand(() =>
            {
                viewModel.Cleanup();
                window.DialogResult = false;
                window.Close();
            });

            // Handle download complete
            viewModel.OnDownloadComplete = (success) =>
            {
                if (success)
                {
                    viewModel.Cleanup();
                    window.DialogResult = true;
                    window.Close();
                }
            };

            // Cleanup on window close
            window.Closing += (s, e) =>
            {
                viewModel.Cleanup();
            };

            var result = window.ShowDialog();

            if (result == true)
            {
                LogDebug($"Download from URL completed successfully for game: {game.Name}");
            }
            else
            {
                LogDebug($"Download from URL cancelled or failed for game: {game.Name}");
            }
        }

        /// <summary>
        /// Normalize an individual song (desktop mode)
        /// </summary>
        public void ShowNormalizeIndividualSongProgress(Game game, string selectedFile)
        {
            if (game == null || string.IsNullOrEmpty(selectedFile))
            {
                Logger.Warn("ShowNormalizeIndividualSongProgress called with null game or empty file");
                return;
            }

            LogDebug($"ShowNormalizeIndividualSongProgress called for game: {game.Name}, file: {selectedFile}");

            _errorHandler.Try(
                () =>
                {
                    // Get the plugin instance from Application.Current.Properties
                    if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                    {
                        var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                        if (plugin != null)
                        {
                            plugin.NormalizeSingleFile(game, selectedFile);
                        }
                        else
                        {
                            Logger.Warn("UniPlaySongPlugin found in Application.Current.Properties but is not UniPlaySong type");
                            _playniteApi.Dialogs.ShowErrorMessage("Plugin instance not available.", "UniPlaySong");
                        }
                    }
                    else
                    {
                        Logger.Warn("UniPlaySongPlugin not found in Application.Current.Properties");
                        _playniteApi.Dialogs.ShowErrorMessage("Plugin instance not available.", "UniPlaySong");
                    }
                },
                context: $"normalizing individual song '{System.IO.Path.GetFileName(selectedFile)}' for '{game.Name}'",
                showUserMessage: true
            );
        }

        /// <summary>
        /// Silence-trim an individual song (desktop mode)
        /// </summary>
        public void ShowTrimIndividualSongProgress(Game game, string selectedFile)
        {
            if (game == null || string.IsNullOrEmpty(selectedFile))
            {
                Logger.Warn("ShowTrimIndividualSongProgress called with null game or empty file");
                return;
            }

            LogDebug($"ShowTrimIndividualSongProgress called for game: {game.Name}, file: {selectedFile}");

            _errorHandler.Try(
                () =>
                {
                    // Get the plugin instance from Application.Current.Properties
                    if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                    {
                        var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                        if (plugin != null)
                        {
                            plugin.TrimSingleFile(game, selectedFile);
                        }
                        else
                        {
                            Logger.Warn("UniPlaySongPlugin found in Application.Current.Properties but is not UniPlaySong type");
                            _playniteApi.Dialogs.ShowErrorMessage("Plugin instance not available.", "UniPlaySong");
                        }
                    }
                    else
                    {
                        Logger.Warn("UniPlaySongPlugin not found in Application.Current.Properties");
                        _playniteApi.Dialogs.ShowErrorMessage("Plugin instance not available.", "UniPlaySong");
                    }
                },
                context: $"trimming individual song '{System.IO.Path.GetFileName(selectedFile)}' for '{game.Name}'",
                showUserMessage: true
            );
        }

        /// <summary>
        /// Automatically normalizes downloaded files if auto-normalize setting is enabled.
        /// Called after successful downloads. Public so controller dialog can also use it.
        /// </summary>
        public void AutoNormalizeDownloadedFiles(List<string> downloadedFiles)
        {
            if (downloadedFiles == null || downloadedFiles.Count == 0)
            {
                return;
            }

            // Check if auto-normalize is enabled
            var settings = _settingsService?.Current;
            if (settings == null || !settings.AutoNormalizeAfterDownload)
            {
                LogDebug("Auto-normalize is disabled, skipping post-download normalization");
                return;
            }

            // Check if normalization service is available
            if (_normalizationService == null)
            {
                Logger.Warn("Normalization service not available for auto-normalize");
                return;
            }

            // Check if FFmpeg is configured
            var ffmpegPath = settings.FFmpegPath;
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !System.IO.File.Exists(ffmpegPath))
            {
                Logger.Warn("FFmpeg not configured, skipping auto-normalize");
                return;
            }

            LogDebug($"Auto-normalizing {downloadedFiles.Count} downloaded file(s)...");

            _errorHandler.Try(
                () =>
                {
                    // Build normalization settings from user preferences
                    var normSettings = new Models.NormalizationSettings
                    {
                        FFmpegPath = ffmpegPath,
                        TargetLoudness = settings.NormalizationTargetLoudness,
                        TruePeak = settings.NormalizationTruePeak,
                        LoudnessRange = settings.NormalizationLoudnessRange,
                        AudioCodec = settings.NormalizationCodec,
                        NormalizationSuffix = settings.NormalizationSuffix,
                        TrimSuffix = settings.TrimSuffix,
                        SkipAlreadyNormalized = settings.SkipAlreadyNormalized,
                        DoNotPreserveOriginals = settings.DoNotPreserveOriginals
                    };

                    // Show progress dialog for normalization
                    var progressOptions = new Playnite.SDK.GlobalProgressOptions(
                        $"Auto-normalizing {downloadedFiles.Count} downloaded file(s)...",
                        true
                    );

                    _playniteApi.Dialogs.ActivateGlobalProgress((progress) =>
                    {
                        try
                        {
                            var cts = new System.Threading.CancellationTokenSource();

                            // Link to Playnite's cancel token
                            progress.CancelToken.Register(() => cts.Cancel());

                            // Track total for display (known upfront)
                            int totalFiles = downloadedFiles.Count;
                            int displayedIndex = 0;

                            var progressReporter = new Progress<Models.NormalizationProgress>(p =>
                            {
                                // Use CurrentIndex if available, otherwise track completed count
                                int currentIndex = p.CurrentIndex > 0 ? p.CurrentIndex : displayedIndex;
                                if (p.CurrentIndex > displayedIndex) displayedIndex = p.CurrentIndex;

                                progress.Text = $"Normalizing: {p.CurrentFile} ({currentIndex}/{totalFiles})";
                            });

                            // Run normalization synchronously within the progress dialog
                            var task = _normalizationService.NormalizeBulkAsync(
                                downloadedFiles,
                                normSettings,
                                progressReporter,
                                cts.Token);

                            task.Wait(cts.Token);

                            var result = task.Result;
                            LogDebug($"Auto-normalize complete: {result.SuccessCount} succeeded, {result.FailureCount} failed, {result.SkippedCount} skipped");
                        }
                        catch (OperationCanceledException)
                        {
                            LogDebug("Auto-normalize cancelled by user");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Error during auto-normalize");
                        }
                    }, progressOptions);
                },
                context: "auto-normalizing downloaded files",
                showUserMessage: false
            );
        }
    }
}


