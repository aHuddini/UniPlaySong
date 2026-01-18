using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Downloaders;
using UniPlaySong.Models;
using UniPlaySong.ViewModels;
using UniPlaySong.Views;

// CS4014: BeginInvoke calls are intentionally fire-and-forget for UI updates from background threads
#pragma warning disable CS4014

namespace UniPlaySong.Services
{
    /// <summary>
    /// Service for showing download dialogs
    /// </summary>
    public class DownloadDialogService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "DownloadDialog";

        private readonly IPlayniteAPI _playniteApi;
        private readonly IDownloadManager _downloadManager;
        private readonly IMusicPlaybackService _playbackService;
        private readonly GameMusicFileService _fileService;
        private readonly SettingsService _settingsService;
        private readonly ErrorHandlerService _errorHandler;
        private INormalizationService _normalizationService;
        private GameMusicTagService _tagService;
        private SearchCacheService _searchCacheService;

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

        public void SetNormalizationService(INormalizationService normalizationService) =>
            _normalizationService = normalizationService;

        public void SetTagService(GameMusicTagService tagService) =>
            _tagService = tagService;

        public void SetSearchCacheService(SearchCacheService searchCacheService) =>
            _searchCacheService = searchCacheService;

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

        /// <summary>Shows source selection dialog (KHInsider, Zophar, YouTube)</summary>
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
                new Playnite.SDK.GenericItemOption("Zophar", "Download from Zophar (Video game music archive)"),
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
                        Logger.DebugIf(LogPrefix,$"User selected source option: {selectedOption.Name}");

                        if (selectedOption.Name == "KHInsider")
                        {
                            Logger.DebugIf(LogPrefix,"Returning Source.KHInsider");
                            return Source.KHInsider;
                        }
                        else if (selectedOption.Name == "Zophar")
                        {
                            Logger.DebugIf(LogPrefix,"Returning Source.Zophar");
                            return Source.Zophar;
                        }
                        else if (selectedOption.Name == "YouTube")
                        {
                            Logger.DebugIf(LogPrefix,"YouTube selected - validating configuration...");
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
                            
                            Logger.DebugIf(LogPrefix,"YouTube configuration validated successfully");
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
                Logger.DebugIf(LogPrefix,"Source selection dialog cancelled or closed");
            }

            return null;
        }

        /// <summary>Shows unified album selection (searches all sources)</summary>
        public Album ShowUnifiedAlbumSelectionDialog(Game game)
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            Logger.DebugIf(LogPrefix,$"ShowUnifiedAlbumSelectionDialog called for game: {game?.Name ?? "null"}");

            Logger.DebugIf(LogPrefix,"Creating unified DownloadDialogViewModel...");
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

            Logger.DebugIf(LogPrefix,"Unified DownloadDialogViewModel created successfully");

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
                                Logger.DebugIf(LogPrefix,"Unified album selection window loaded and activated");

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

                    Logger.DebugIf(LogPrefix,"Showing unified album selection window...");
                    var result = window.ShowDialog();
                    Logger.DebugIf(LogPrefix,$"Unified album selection window closed with result: {result}");

                    if (result == true)
                    {
                        var selected = viewModel.GetSelectedItems();
                        var album = selected.FirstOrDefault() as Album;
                        if (album != null)
                        {
                            Logger.DebugIf(LogPrefix,$"User selected album: {album.Name} from {album.Source}");
                            return album;
                        }
                    }

                    Logger.DebugIf(LogPrefix,"User cancelled unified album selection or no album selected");
                    return null;
                },
                defaultValue: null,
                context: $"showing unified album selection dialog for '{game?.Name ?? "null"}'",
                showUserMessage: true
            );
        }

        /// <summary>Shows album selection dialog</summary>
        public Album ShowAlbumSelectionDialog(Game game, Source source)
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();
            
            Logger.DebugIf(LogPrefix,$"ShowAlbumSelectionDialog called for game: {game?.Name ?? "null"}, source: {source}");
            
            // YouTube configuration is now validated in ShowSourceSelectionDialog before YouTube can be selected
            // So if we reach here with YouTube source, it should be valid
            if (source == Source.YouTube)
            {
                Logger.DebugIf(LogPrefix,"YouTube source confirmed - configuration already validated");
            }

            Logger.DebugIf(LogPrefix,"Creating DownloadDialogViewModel...");
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
            
            Logger.DebugIf(LogPrefix,"DownloadDialogViewModel created successfully");
            
            Logger.DebugIf(LogPrefix,"Creating album selection window...");

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
                                Logger.DebugIf(LogPrefix,"Album selection window loaded and activated");
                                
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

                    Logger.DebugIf(LogPrefix,"Showing album selection dialog...");
                    var result = window.ShowDialog();
                    Logger.DebugIf(LogPrefix,$"Album selection dialog result: {result}");
                
                    if (result == true)
                    {
                        // If double-clicked, use that album
                        if (doubleClickedAlbum != null)
                        {
                            Logger.DebugIf(LogPrefix,$"Album double-clicked: {doubleClickedAlbum.Name}");
                            return doubleClickedAlbum;
                        }

                        var selected = viewModel.GetSelectedItems();
                        var album = selected.FirstOrDefault() as Album;
                        if (album != null)
                        {
                            Logger.DebugIf(LogPrefix,$"Album selected: {album.Name} (ID: {album.Id})");
                        }
                        else
                        {
                            Logger.DebugIf(LogPrefix,"No album selected from dialog");
                        }
                        return album;
                    }

                    // Return BackSignal when Back is pressed - signals caller to go back to source selection
                    if (viewModel.BackWasPressed)
                    {
                        Logger.DebugIf(LogPrefix,"Album selection dialog: user pressed back - returning to source selection");
                        return Album.BackSignal;
                    }

                    Logger.DebugIf(LogPrefix,"Album selection dialog cancelled");
                    return null;
                },
                defaultValue: null,
                context: $"showing album selection dialog for game '{game?.Name ?? "null"}' with source {source}",
                showUserMessage: true
            );
        }

        /// <summary>Shows song selection dialog</summary>
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
                        Logger.DebugIf(LogPrefix,"Song selection window loaded and activated");
                        
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
                                            Logger.DebugIf(LogPrefix,$"Triggering song search for album: {album.Name}");
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
                // BUT: Don't interrupt preview playback - let the user finish previewing
                if (success && game != null && _playbackService != null && !viewModel.IsPreviewPlaying)
                {
                    _errorHandler.Try(
                        () =>
                        {
                            Logger.DebugIf(LogPrefix,$"Download complete - triggering music refresh for game: {game.Name}");
                            // Get current settings to pass to PlayGameMusic
                            var settings = _settingsService?.Current;
                            _playbackService.PlayGameMusic(game, settings, forceReload: true);
                        },
                        context: $"refreshing music after download for '{game.Name}'"
                    );
                }
                else if (success && viewModel.IsPreviewPlaying)
                {
                    Logger.DebugIf(LogPrefix,$"Download complete for '{game?.Name}' but preview is playing - not auto-starting game music");
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
                        Logger.DebugIf(LogPrefix,$"Updated music tag for game '{game.Name}' after download");
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
                Logger.DebugIf(LogPrefix,"Song selection dialog: user pressed back - returning to album selection");
                return null;
            }

            // Return empty list for cancel/confirm/download complete - downloads are handled inline
            return new List<Song>();
        }

        /// <summary>Shows song selection dialog, returns selections (for batch download)</summary>
        public List<Song> ShowSongSelectionDialogWithReturn(Game game, Album album)
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            Logger.DebugIf(LogPrefix,$"ShowSongSelectionDialogWithReturn called for game: {game?.Name ?? "null"}, album: {album?.Name ?? "null"}");

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
                        Logger.DebugIf(LogPrefix,"Batch song selection window loaded and activated");

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
                                            Logger.DebugIf(LogPrefix,$"Triggering song search for album: {album.Name}");
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
                Logger.DebugIf(LogPrefix,$"User selected {selectedSongs.Count} songs for batch download");
                return selectedSongs;
            }

            return new List<Song>();
        }

        /// <summary>Shows download dialog for default music (no game context)</summary>
        public bool ShowDefaultMusicDownloadDialog(string defaultMusicPath)
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            Logger.DebugIf(LogPrefix,"ShowDefaultMusicDownloadDialog called");

            if (_downloadManager == null)
            {
                _playniteApi.Dialogs.ShowMessage("Download manager not initialized. Please check extension settings.");
                return false;
            }

            // Step 1: Select source (KHInsider or YouTube)
            Source? sourceNullable = ShowSourceSelectionDialog();
            if (sourceNullable == null)
            {
                Logger.DebugIf(LogPrefix,"User cancelled source selection");
                return false;
            }

            var source = sourceNullable.Value;
            Logger.DebugIf(LogPrefix,$"User selected source: {source}");

            // Step 2: For default music, use a dummy game and let users search in the album dialog
            // This is more intuitive - users can type the game name in the search box
            Game dummyGame = new Game { Name = "", Id = Guid.NewGuid() }; // Empty name - user will search

            // Step 3: Select album (with search functionality - no auto-search, user types game name)
            Album album = ShowAlbumSelectionDialogForDefaultMusic(source);
            if (album == null)
            {
                Logger.DebugIf(LogPrefix,"User cancelled album selection");
                return false;
            }

            // Step 4: Select song (using custom dialog that returns selected songs)
            var selectedSong = ShowSongSelectionForDefaultMusic(dummyGame, album);
            if (selectedSong == null)
            {
                Logger.DebugIf(LogPrefix,"No song selected or download cancelled");
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

                    Logger.DebugIf(LogPrefix,$"Downloading default music to: {downloadPath}");

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
                        Logger.DebugIf(LogPrefix,$"Default music downloaded successfully: {downloadPath}");
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

        /// <summary>Shows song selection for default music downloads</summary>
        private Song ShowSongSelectionForDefaultMusic(Game game, Album album)
        {
            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            Logger.DebugIf(LogPrefix,$"ShowSongSelectionForDefaultMusic called for game: {game?.Name ?? "null"}, album: {album?.Name ?? "null"}");

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
            
            Logger.DebugIf(LogPrefix,"DownloadDialogViewModel created successfully");

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
                        Logger.DebugIf(LogPrefix,"Default music song selection window loaded and activated");
                        
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
                                            Logger.DebugIf(LogPrefix,$"Triggering song search for album: {album.Name}");
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
            
            Logger.DebugIf(LogPrefix,$"ShowAlbumSelectionDialogForDefaultMusic called for source: {source}");

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
            
            Logger.DebugIf(LogPrefix,"DownloadDialogViewModel created successfully");

            var window = _playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = true,
                ShowCloseButton = true
            });

            window.Height = 500;
            window.Width = 700;
            window.Title = source == Source.KHInsider ? "Search for Game Soundtrack (Default Music)" :
                           source == Source.Zophar ? "Search Zophar Archive (Default Music)" :
                           "Enter YouTube URL (Default Music)";
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
            viewModel.Title = source == Source.KHInsider ? "Enter game name to search for soundtracks" :
                              source == Source.Zophar ? "Enter game name to search Zophar archive" :
                              "Enter YouTube playlist or video URL";

            // Don't auto-search - let user type in search box
            window.Loaded += (s, e) =>
            {
                _errorHandler.Try(
                    () =>
                    {
                        window.Activate();
                        window.Focus();
                        Logger.DebugIf(LogPrefix,"Default music album selection window loaded");
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

            Logger.DebugIf(LogPrefix,$"ShowDownloadFromUrlDialog called for game: {game.Name}");

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
                Logger.DebugIf(LogPrefix,$"Download from URL completed successfully for game: {game.Name}");
            }
            else
            {
                Logger.DebugIf(LogPrefix,$"Download from URL cancelled or failed for game: {game.Name}");
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

            Logger.DebugIf(LogPrefix,$"ShowNormalizeIndividualSongProgress called for game: {game.Name}, file: {selectedFile}");

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

            Logger.DebugIf(LogPrefix,$"ShowTrimIndividualSongProgress called for game: {game.Name}, file: {selectedFile}");

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
                Logger.DebugIf(LogPrefix,"Auto-normalize is disabled, skipping post-download normalization");
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

            Logger.DebugIf(LogPrefix,$"Auto-normalizing {downloadedFiles.Count} downloaded file(s)...");

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
                            Logger.DebugIf(LogPrefix,$"Auto-normalize complete: {result.SuccessCount} succeeded, {result.FailureCount} failed, {result.SkippedCount} skipped");

                            // Refresh song count after normalization (for skip button visibility)
                            _playbackService?.RefreshSongCount();
                        }
                        catch (OperationCanceledException)
                        {
                            Logger.DebugIf(LogPrefix,"Auto-normalize cancelled by user");
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

        /// <summary>
        /// Shows the batch download progress dialog and performs parallel downloads
        /// </summary>
        /// <param name="games">Games to download music for</param>
        /// <param name="source">Download source</param>
        /// <param name="overwrite">Whether to overwrite existing music</param>
        /// <param name="maxConcurrentDownloads">Maximum concurrent downloads</param>
        /// <returns>List of downloaded file paths for auto-normalization</returns>
        public List<string> ShowBatchDownloadDialog(
            List<Game> games,
            Source source,
            bool overwrite,
            int maxConcurrentDownloads = 3)
        {
            var result = ShowBatchDownloadDialogWithResults(games, source, overwrite, maxConcurrentDownloads);
            return result?.DownloadedFiles ?? new List<string>();
        }

        /// <summary>
        /// Shows the batch download progress dialog and performs parallel downloads
        /// Returns full results including failed games for retry prompting
        /// </summary>
        public BatchDownloadResult ShowBatchDownloadDialogWithResults(
            List<Game> games,
            Source source,
            bool overwrite,
            int maxConcurrentDownloads = 3)
        {
            return ShowBatchDownloadDialogWithResults(games, source, overwrite, maxConcurrentDownloads, null);
        }

        /// <summary>
        /// Shows the batch download progress dialog and performs parallel downloads
        /// Returns full results including failed games for retry prompting
        /// Optionally resumes playback for the current game when its download completes
        /// </summary>
        /// <param name="games">Games to download music for</param>
        /// <param name="source">Download source</param>
        /// <param name="overwrite">Whether to overwrite existing music</param>
        /// <param name="maxConcurrentDownloads">Max concurrent downloads</param>
        /// <param name="currentGame">Currently selected game - playback resumes when this game's download completes</param>
        public BatchDownloadResult ShowBatchDownloadDialogWithResults(
            List<Game> games,
            Source source,
            bool overwrite,
            int maxConcurrentDownloads,
            Game currentGame)
        {
            var batchResult = new BatchDownloadResult();

            if (games == null || games.Count == 0)
            {
                _playniteApi.Dialogs.ShowMessage("No games selected.", "Batch Download");
                return batchResult;
            }

            // Track whether we've already resumed playback for the current game
            bool playbackResumedForCurrentGame = false;

            // Track successfully downloaded games for music queue during download
            var downloadedGamesForPlayback = new List<Game>();
            var downloadedGamesLock = new object();
            var random = new Random();
            bool isPlayingDownloadedMusic = false;
            Game lastPlayedGame = null; // Track last played to avoid immediate repeats

            Logger.Info($"[BatchDownloadDialog] Starting for {games.Count} games, source: {source}, concurrent: {maxConcurrentDownloads}, currentGame: {currentGame?.Name ?? "none"}");

            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            List<GameDownloadResult> allResults = null;
            var resultsReady = new System.Threading.ManualResetEventSlim(false);

            try
            {
                // Create the batch download service
                var batchService = new BatchDownloadService(_downloadManager, _fileService, _errorHandler);
                batchService.MaxConcurrentDownloads = maxConcurrentDownloads;

                // Create progress dialog
                var progressDialog = new Views.BatchDownloadProgressDialog();
                progressDialog.Initialize(games, _playniteApi);

                var window = DialogHelper.CreateFullscreenDialog(
                    _playniteApi,
                    "Batch Download Progress",
                    progressDialog,
                    width: 900,
                    height: 650,
                    isFullscreenMode: IsFullscreenMode);

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "batch download dialog close");

                // Handle re-download requests during review mode
                progressDialog.OnGameRedownloadRequested += (item) =>
                {
                    HandleRedownloadRequest(item, progressDialog);
                };

                // Handle music pause/play button click
                progressDialog.OnMusicPausePlayRequested += () =>
                {
                    if (_playbackService != null)
                    {
                        if (_playbackService.IsPaused)
                        {
                            Logger.Info("[BatchDownloadDialog] Resuming music playback");
                            _playbackService.Resume();
                        }
                        else
                        {
                            Logger.Info("[BatchDownloadDialog] Pausing music playback");
                            _playbackService.Pause();
                        }
                    }
                };

                // Handle Auto-Add More Songs request
                progressDialog.OnAutoAddSongsRequested += (songCount) =>
                {
                    HandleAutoAddMoreSongs(progressDialog, allResults, songCount);
                };

                // Handle window closing - capture results if not already done
                window.Closing += (s, e) =>
                {
                    Logger.Info($"[BatchDownloadDialog] Window closing, allResults populated: {allResults != null}");
                };

                // Helper to play a random game from the downloaded queue
                Action playRandomDownloadedGame = () =>
                {
                    Game gameToPlay = null;
                    lock (downloadedGamesLock)
                    {
                        if (downloadedGamesForPlayback.Count > 0)
                        {
                            // Pick a random game, avoiding immediate repeat if possible
                            var candidates = downloadedGamesForPlayback.Count > 1
                                ? downloadedGamesForPlayback.Where(g => g != lastPlayedGame).ToList()
                                : downloadedGamesForPlayback;

                            gameToPlay = candidates[random.Next(candidates.Count)];
                            lastPlayedGame = gameToPlay;
                        }
                    }

                    if (gameToPlay != null)
                    {
                        Logger.Info($"[BatchDownloadDialog] Playing: '{gameToPlay.Name}' (queue: {downloadedGamesForPlayback.Count})");
                        isPlayingDownloadedMusic = true;
                        // Must dispatch to UI thread - OnSongEnded fires from media player thread
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            _playbackService?.PlayGameMusic(gameToPlay, _settingsService?.Current, forceReload: true);
                        }));
                    }
                };

                // Enable SuppressAutoLoop so batch download controls when songs change
                _playbackService.SuppressAutoLoop = true;

                // Subscribe to OnSongEnded to queue next random song when current song finishes
                Action onSongEndedHandler = () =>
                {
                    if (isPlayingDownloadedMusic && !progressDialog.CancellationToken.IsCancellationRequested)
                    {
                        playRandomDownloadedGame();
                    }
                };
                _playbackService.OnSongEnded += onSongEndedHandler;

                // Start downloads asynchronously
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        // Progress callback to update the dialog and manage music queue
                        GameDownloadProgressCallback progressCallback = (game, gameName, status, message, albumName, sourceName) =>
                        {
                            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                // Use Game ID-based matching to handle duplicate game names correctly
                                progressDialog.UpdateGameStatusByGame(game, status, message, albumName, sourceName);

                                if (status != BatchDownloadStatus.Completed) return;

                                // Add to music queue - use the game object directly
                                if (game != null)
                                {
                                    lock (downloadedGamesLock)
                                    {
                                        if (!downloadedGamesForPlayback.Contains(game))
                                            downloadedGamesForPlayback.Add(game);
                                    }
                                }

                                // Priority: Play current game if it just downloaded
                                if (currentGame != null && !playbackResumedForCurrentGame &&
                                    game != null && game.Id == currentGame.Id)
                                {
                                    Logger.Info($"[BatchDownloadDialog] Current game '{gameName}' downloaded - playing");
                                    playbackResumedForCurrentGame = true;
                                    isPlayingDownloadedMusic = true;
                                    _playbackService?.PlayGameMusic(currentGame, _settingsService?.Current, forceReload: true);
                                }
                                // Otherwise start random queue if not already playing
                                else if (!isPlayingDownloadedMusic)
                                {
                                    Logger.Info($"[BatchDownloadDialog] First download '{gameName}' - starting music queue");
                                    playRandomDownloadedGame();
                                }
                            }));
                        };

                        var results = await batchService.DownloadMusicParallelAsync(
                            games,
                            source,
                            overwrite,
                            progressDialog.CancellationToken,
                            progressCallback);

                        // Store all results for later processing
                        allResults = results;

                        var successCount = results.Count(r => r.Success);
                        var failCount = results.Count(r => !r.Success && !r.WasSkipped);
                        var skipCount = results.Count(r => r.WasSkipped);

                        Logger.Info($"[BatchDownloadDialog] Download complete: {successCount} succeeded, {failCount} failed, {skipCount} skipped");

                        // Don't auto-close - let user review results and close manually
                        // The UI will show "Close" button and "Review Downloads" button when complete
                        Logger.Info("[BatchDownloadDialog] Downloads complete - waiting for user to close dialog");

                        // Force final UI update to ensure Close/Review buttons appear immediately
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            progressDialog.ForceUIUpdate();
                        }));

                        // Signal that results are ready (but don't close the dialog)
                        resultsReady.Set();
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Info("[BatchDownloadDialog] Cancelled by user");
                        resultsReady.Set();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "[BatchDownloadDialog] Error during batch download");
                        resultsReady.Set();
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            _playniteApi.Dialogs.ShowErrorMessage($"Error during batch download: {ex.Message}", "Batch Download Error");
                        }));
                    }
                });

                // Show dialog (blocks until user closes it)
                window.ShowDialog();

                // Cleanup: restore normal playback mode
                _playbackService.OnSongEnded -= onSongEndedHandler;
                _playbackService.SuppressAutoLoop = false;

                // Give a moment for any pending async operations to complete
                resultsReady.Wait(TimeSpan.FromMilliseconds(500));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[BatchDownloadDialog] Error showing dialog");
                _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "Batch Download Error");
            }

            // Process results after dialog closes
            Logger.Info($"[BatchDownloadDialog] Processing results after dialog close, allResults={allResults?.Count ?? -1}");

            if (allResults != null)
            {
                batchResult.SuccessCount = allResults.Count(r => r.Success);
                batchResult.FailedCount = allResults.Count(r => !r.Success && !r.WasSkipped);
                batchResult.SkippedCount = allResults.Count(r => r.WasSkipped);

                Logger.Info($"[BatchDownloadDialog] Results breakdown: success={batchResult.SuccessCount}, failed={batchResult.FailedCount}, skipped={batchResult.SkippedCount}");

                foreach (var result in allResults)
                {
                    if (result.Success && !string.IsNullOrEmpty(result.SongPath))
                    {
                        batchResult.DownloadedFiles.Add(result.SongPath);
                    }
                    else if (!result.Success && !result.WasSkipped)
                    {
                        batchResult.FailedGames.Add(result);
                        Logger.Info($"[BatchDownloadDialog] Adding failed game: '{result.Game?.Name}' - {result.ErrorMessage}");
                    }
                }

                Logger.Info($"[BatchDownloadDialog] Final: {batchResult.DownloadedFiles.Count} files, {batchResult.FailedGames.Count} failed games");
            }
            else
            {
                Logger.Warn("[BatchDownloadDialog] allResults is null - results may not have been captured");
            }

            // Resume playback for current game after batch completes (if not already resumed during batch)
            if (currentGame != null && !playbackResumedForCurrentGame)
            {
                // Check if the current game now has music (either it was in the batch or already had music)
                var musicDir = _fileService.GetGameMusicDirectory(currentGame);
                if (Directory.Exists(musicDir) && Directory.GetFiles(musicDir, "*.mp3").Length > 0)
                {
                    Logger.Info($"[BatchDownloadDialog] Batch complete - resuming playback for current game '{currentGame.Name}'");
                    var settings = _settingsService?.Current;
                    _playbackService?.PlayGameMusic(currentGame, settings, forceReload: true);
                }
                else
                {
                    Logger.Info($"[BatchDownloadDialog] Batch complete - current game '{currentGame.Name}' still has no music");
                }
            }

            return batchResult;
        }

        /// <summary>
        /// Prompts user for retry options after batch download and handles retries
        /// </summary>
        /// <param name="failedGames">List of failed game download results</param>
        /// <returns>List of additionally downloaded file paths from retries</returns>
        public List<string> PromptAndRetryFailedDownloads(List<GameDownloadResult> failedGames)
        {
            var additionalDownloads = new List<string>();

            if (failedGames == null || failedGames.Count == 0)
                return additionalDownloads;

            Logger.Info($"[RetryPrompt] {failedGames.Count} failed downloads to retry");

            // Build message
            var message = $"{failedGames.Count} game(s) failed to find music:\n\n";
            foreach (var failed in failedGames.Take(8))
            {
                message += $"• {failed.Game.Name}\n";
            }
            if (failedGames.Count > 8)
            {
                message += $"... and {failedGames.Count - 8} more\n";
            }
            message += "\nHow would you like to proceed?";

            // Show options dialog
            var options = new List<MessageBoxOption>
            {
                new MessageBoxOption("Auto-retry with broader search", true, false),
                new MessageBoxOption("Manual search for each game", false, false),
                new MessageBoxOption("Skip", false, false)
            };

            var selection = _playniteApi.Dialogs.ShowMessage(
                message,
                "Retry Failed Downloads",
                System.Windows.MessageBoxImage.Question,
                options);

            if (selection == null || selection.Title == "Skip")
            {
                Logger.Info("[RetryPrompt] User chose to skip retries");
                return additionalDownloads;
            }

            if (selection.Title == "Auto-retry with broader search")
            {
                Logger.Info("[RetryPrompt] User chose auto-retry with broader search");
                additionalDownloads = AutoRetryWithBroaderSearch(failedGames);
            }
            else if (selection.Title == "Manual search for each game")
            {
                Logger.Info("[RetryPrompt] User chose manual search");
                additionalDownloads = ManualRetryForFailedGames(failedGames);
            }

            return additionalDownloads;
        }

        /// <summary>
        /// Auto-retry failed downloads with broader/less restrictive search
        /// Uses the same Material Design batch download dialog for consistency
        /// </summary>
        private List<string> AutoRetryWithBroaderSearch(List<GameDownloadResult> failedGames)
        {
            var downloadedFiles = new List<string>();

            Logger.Info($"[BroaderRetry] Starting broader search retry for {failedGames.Count} games");

            // Extract Game objects from failed results
            var gamesToRetry = failedGames.Select(f => f.Game).Where(g => g != null).ToList();

            if (gamesToRetry.Count == 0)
            {
                Logger.Info("[BroaderRetry] No valid games to retry");
                return downloadedFiles;
            }

            // Use the Material Design batch download dialog with broader matching
            var batchResult = ShowBatchDownloadDialogWithBroaderSearch(gamesToRetry);

            if (batchResult != null)
            {
                downloadedFiles.AddRange(batchResult.DownloadedFiles);
            }

            Logger.Info($"[BroaderRetry] Complete: {downloadedFiles.Count} additional downloads");

            if (downloadedFiles.Count > 0)
            {
                _playniteApi.Dialogs.ShowMessage(
                    $"Broader search found music for {downloadedFiles.Count} additional game(s).",
                    "Retry Complete");
            }
            else
            {
                _playniteApi.Dialogs.ShowMessage(
                    "Broader search did not find any additional matches.\n\n" +
                    "You can try manual search for specific games.",
                    "Retry Complete");
            }

            return downloadedFiles;
        }

        /// <summary>
        /// Shows batch download dialog with broader/looser matching for retry operations
        /// </summary>
        private BatchDownloadResult ShowBatchDownloadDialogWithBroaderSearch(List<Game> games)
        {
            var batchResult = new BatchDownloadResult();

            if (games == null || games.Count == 0)
            {
                return batchResult;
            }

            Logger.Info($"[BroaderRetryDialog] Starting broader search for {games.Count} games");

            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            List<GameDownloadResult> allResults = null;
            var resultsReady = new System.Threading.ManualResetEventSlim(false);

            try
            {
                // Create progress dialog
                var progressDialog = new Views.BatchDownloadProgressDialog();
                progressDialog.Initialize(games, _playniteApi);

                var window = DialogHelper.CreateFullscreenDialog(
                    _playniteApi,
                    "Retry with Broader Search",
                    progressDialog,
                    width: 900,
                    height: 650,
                    isFullscreenMode: IsFullscreenMode);

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "broader retry dialog close");

                // Start downloads asynchronously with broader matching
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var results = new List<GameDownloadResult>();
                    var resultsLock = new object();

                    try
                    {
                        // Process games with concurrency control (same as normal batch)
                        var maxConcurrent = _settingsService.Current?.MaxConcurrentDownloads ?? 3;
                        using (var semaphore = new System.Threading.SemaphoreSlim(maxConcurrent))
                        {
                            var tasks = games.Select(async game =>
                            {
                                await semaphore.WaitAsync(progressDialog.CancellationToken);

                                try
                                {
                                    if (progressDialog.CancellationToken.IsCancellationRequested)
                                    {
                                        lock (resultsLock)
                                        {
                                            results.Add(new GameDownloadResult
                                            {
                                                Game = game,
                                                Success = false,
                                                WasSkipped = true,
                                                SkipReason = "Cancelled"
                                            });
                                        }
                                        return;
                                    }

                                    // Update progress
                                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                    {
                                        progressDialog.UpdateGameStatus(game.Name, BatchDownloadStatus.Downloading, "Broader search...", null, null);
                                    }));

                                    // Perform broader search and download
                                    var downloadPath = await System.Threading.Tasks.Task.Run(() =>
                                        RetryWithBroaderSearchInternal(game, progressDialog.CancellationToken));

                                    var result = new GameDownloadResult { Game = game };

                                    if (!string.IsNullOrEmpty(downloadPath))
                                    {
                                        result.Success = true;
                                        result.SongPath = downloadPath;

                                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                        {
                                            progressDialog.UpdateGameStatus(game.Name, BatchDownloadStatus.Completed, "Downloaded", null, null);
                                        }));
                                    }
                                    else
                                    {
                                        result.Success = false;
                                        result.ErrorMessage = "No match found";

                                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                        {
                                            progressDialog.UpdateGameStatus(game.Name, BatchDownloadStatus.Failed, "No match found", null, null);
                                        }));
                                    }

                                    lock (resultsLock) { results.Add(result); }
                                }
                                catch (OperationCanceledException)
                                {
                                    lock (resultsLock)
                                    {
                                        results.Add(new GameDownloadResult
                                        {
                                            Game = game,
                                            Success = false,
                                            WasSkipped = true,
                                            SkipReason = "Cancelled"
                                        });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error(ex, $"[BroaderRetry] Error for '{game.Name}': {ex.Message}");
                                    lock (resultsLock)
                                    {
                                        results.Add(new GameDownloadResult
                                        {
                                            Game = game,
                                            Success = false,
                                            ErrorMessage = ex.Message
                                        });
                                    }

                                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                    {
                                        progressDialog.UpdateGameStatus(game.Name, BatchDownloadStatus.Failed, ex.Message, null, null);
                                    }));
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }).ToList();

                            await System.Threading.Tasks.Task.WhenAll(tasks);
                        }

                        // Store results
                        allResults = results;

                        var successCount = results.Count(r => r.Success);
                        var failCount = results.Count(r => !r.Success && !r.WasSkipped);

                        Logger.Info($"[BroaderRetryDialog] Complete: {successCount} succeeded, {failCount} failed");

                        // Close dialog
                        System.Windows.Application.Current?.Dispatcher?.Invoke(new Action(() =>
                        {
                            window.Close();
                        }));

                        resultsReady.Set();
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Info("[BroaderRetryDialog] Cancelled by user");
                        allResults = results;
                        resultsReady.Set();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "[BroaderRetryDialog] Error during retry");
                        allResults = results;
                        resultsReady.Set();
                    }
                });

                // Show dialog (blocks until closed)
                window.ShowDialog();

                // Wait for results
                resultsReady.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[BroaderRetryDialog] Error showing dialog");
            }

            // Process results
            if (allResults != null)
            {
                batchResult.SuccessCount = allResults.Count(r => r.Success);
                batchResult.FailedCount = allResults.Count(r => !r.Success && !r.WasSkipped);
                batchResult.SkippedCount = allResults.Count(r => r.WasSkipped);

                foreach (var result in allResults)
                {
                    if (result.Success && !string.IsNullOrEmpty(result.SongPath))
                    {
                        batchResult.DownloadedFiles.Add(result.SongPath);
                    }
                    else if (!result.Success && !result.WasSkipped)
                    {
                        batchResult.FailedGames.Add(result);
                    }
                }
            }

            return batchResult;
        }

        /// <summary>
        /// Internal method to retry a single game with broader search parameters
        /// </summary>
        private string RetryWithBroaderSearchInternal(Game game, CancellationToken cancellationToken)
        {
            // Get albums with cache bypass to ensure fresh results
            var albums = _downloadManager.GetAlbumsForGame(game.Name, Source.All, cancellationToken, auto: true, skipCache: true);
            var albumsList = albums?.ToList() ?? new List<Album>();

            if (albumsList.Count == 0)
            {
                Logger.Info($"[BroaderRetry] No albums found for '{game.Name}' even with fresh search");
                return null;
            }

            // Use looser matching - pick first reasonable match
            var bestAlbum = _downloadManager.BestAlbumPickBroader(albumsList, game);
            if (bestAlbum == null)
            {
                Logger.Info($"[BroaderRetry] No suitable album even with broader matching for '{game.Name}'");
                return null;
            }

            Logger.Info($"[BroaderRetry] Found album '{bestAlbum.Name}' for '{game.Name}'");

            // Get songs
            var songs = _downloadManager.GetSongsFromAlbum(bestAlbum, cancellationToken);
            var songsList = songs?.ToList() ?? new List<Song>();

            if (songsList.Count == 0)
            {
                Logger.Info($"[BroaderRetry] Album '{bestAlbum.Name}' has no songs");
                return null;
            }

            // Pick first song (broader = less picky)
            var songToDownload = songsList.First();

            // Download
            var musicDir = _fileService.GetGameMusicDirectory(game);
            if (!System.IO.Directory.Exists(musicDir))
            {
                System.IO.Directory.CreateDirectory(musicDir);
            }

            var safeFileName = Common.StringHelper.CleanForPath(songToDownload.Name);
            var extension = System.IO.Path.GetExtension(songToDownload.Id);
            if (string.IsNullOrEmpty(extension) || extension == songToDownload.Id)
            {
                extension = ".mp3";
            }
            var downloadPath = System.IO.Path.Combine(musicDir, safeFileName + extension);

            var success = _downloadManager.DownloadSong(songToDownload, downloadPath, cancellationToken);

            return success && System.IO.File.Exists(downloadPath) ? downloadPath : null;
        }

        /// <summary>
        /// Manual retry - show batch manual download dialog for all failed games
        /// Uses the new BatchManualDownloadDialog for a unified experience
        /// </summary>
        private List<string> ManualRetryForFailedGames(List<GameDownloadResult> failedGames)
        {
            var downloadedFiles = new List<string>();

            if (failedGames == null || failedGames.Count == 0)
            {
                Logger.Info("[ManualRetry] No failed games to retry");
                return downloadedFiles;
            }

            // Extract Game objects from failed results
            var gamesToRetry = failedGames.Select(f => f.Game).Where(g => g != null).ToList();

            if (gamesToRetry.Count == 0)
            {
                Logger.Info("[ManualRetry] No valid games to retry");
                return downloadedFiles;
            }

            Logger.Info($"[ManualRetry] Opening batch manual download dialog for {gamesToRetry.Count} games");

            // Use the new batch manual download dialog
            var anySuccess = ShowBatchManualDownloadDialog(gamesToRetry);

            // Check for downloaded files after dialog closes
            foreach (var game in gamesToRetry)
            {
                var musicDir = _fileService.GetGameMusicDirectory(game);
                if (System.IO.Directory.Exists(musicDir))
                {
                    var audioFiles = System.IO.Directory.GetFiles(musicDir)
                        .Where(f => Constants.SupportedAudioExtensions.Contains(
                            System.IO.Path.GetExtension(f).ToLowerInvariant()))
                        .ToList();

                    foreach (var file in audioFiles)
                    {
                        if (!downloadedFiles.Contains(file))
                        {
                            downloadedFiles.Add(file);
                        }
                    }
                }
            }

            Logger.Info($"[ManualRetry] Complete - {downloadedFiles.Count} files downloaded");

            return downloadedFiles;
        }

        /// <summary>
        /// Shows the batch manual download dialog for multiple failed games.
        /// This provides a unified UI where users can search and download music for all failed games
        /// from a single dialog instead of opening individual dialogs for each game.
        /// </summary>
        /// <param name="failedGames">List of games that failed auto-download</param>
        /// <returns>True if any downloads were successful</returns>
        public bool ShowBatchManualDownloadDialog(List<Game> failedGames)
        {
            if (failedGames == null || failedGames.Count == 0)
            {
                Logger.DebugIf(LogPrefix, "ShowBatchManualDownloadDialog called with no games");
                return false;
            }

            Logger.Info($"[BatchManualDownload] Opening dialog for {failedGames.Count} failed games");

            // Pre-load Material Design assemblies before XAML parsing
            PreloadMaterialDesignAssemblies();

            var viewModel = new BatchManualDownloadViewModel(
                failedGames,
                _downloadManager,
                _playbackService,
                _fileService,
                _playniteApi,
                _errorHandler);

            var window = _playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = false,
                ShowCloseButton = true
            });

            window.Height = 550;
            window.Width = 750;
            window.Title = "Manual Download - Select Albums";
            window.ShowInTaskbar = !IsFullscreenMode;
            window.Topmost = IsFullscreenMode;

            // Set background color for fullscreen mode (fixes transparency issue)
            window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 33, 33));

            var view = new BatchManualDownloadDialog();
            window.Content = view;
            window.DataContext = viewModel;
            var ownerWindow = _playniteApi.Dialogs.GetCurrentAppWindow();
            window.Owner = ownerWindow;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Set up close handler
            viewModel.CloseDialog = dialogResult =>
            {
                window.DialogResult = dialogResult;
                window.Close();
            };

            window.Loaded += (s, e) =>
            {
                window.Activate();
                window.Focus();
            };

            var dialogOutcome = window.ShowDialog();
            Logger.Info($"[BatchManualDownload] Dialog closed - Success: {viewModel.SuccessCount}, Failed: {viewModel.FailedCount}");

            return dialogOutcome == true;
        }

        /// <summary>Handle Auto-Add More Songs - downloads additional songs as albums are found</summary>
        private void HandleAutoAddMoreSongs(Views.BatchDownloadProgressDialog progressDialog, List<GameDownloadResult> allResults, int songsPerGame)
        {
            if (allResults == null)
            {
                Logger.Warn("[AutoAddMoreSongs] No download results available");
                _playniteApi.Dialogs.ShowMessage("No download results available. Please wait for the initial download to complete.", "Auto-Add Songs");
                return;
            }

            // Get successful downloads that have album info
            var successfulResults = allResults.Where(r => r.Success && !string.IsNullOrEmpty(r.AlbumName)).ToList();

            // Also include skipped games (already have music) - try to find album info from cache or fresh search
            var skippedResults = allResults.Where(r => r.WasSkipped && r.Game != null && r.SkipReason == "Already has music").ToList();

            Logger.Info($"[AutoAddMoreSongs] Found {successfulResults.Count} successful downloads and {skippedResults.Count} skipped games");

            // Run the entire process in the background
            System.Threading.Tasks.Task.Run(async () =>
            {
                int totalAdded = 0;
                int gamesProcessed = 0;
                int gamesFailed = 0;

                // Track downloaded songs for playback queue
                var downloadedSongPaths = new List<string>();
                var downloadedSongsLock = new object();
                var isPlayingNewSongs = false;

                // Helper to play the next song from the queue
                Action playNextDownloadedSong = null;
                playNextDownloadedSong = () =>
                {
                    string songToPlay = null;
                    lock (downloadedSongsLock)
                    {
                        if (downloadedSongPaths.Count > 0)
                        {
                            songToPlay = downloadedSongPaths[0];
                            downloadedSongPaths.RemoveAt(0);
                        }
                        else
                        {
                            isPlayingNewSongs = false;
                        }
                    }

                    if (songToPlay != null && _playbackService != null)
                    {
                        Logger.Info($"[AutoAddMoreSongs] Playing newly downloaded: {Path.GetFileName(songToPlay)}");
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            _playbackService.LoadAndPlayFile(songToPlay);
                        }));
                    }
                };

                // Subscribe to song ended event to play next queued song
                Action onSongEndedHandler = () =>
                {
                    if (isPlayingNewSongs)
                    {
                        playNextDownloadedSong();
                    }
                };
                _playbackService.OnSongEnded += onSongEndedHandler;

                try
                {
                    // Process a single game result - search for album if needed, then download songs
                    Func<GameDownloadResult, bool, System.Threading.Tasks.Task> processGameAsync = async (result, needsAlbumSearch) =>
                    {
                        string albumName = result.AlbumName;
                        string albumId = result.AlbumId;
                        string sourceName = result.SourceName;

                        // If we need to search for album (skipped games), do it first
                        if (needsAlbumSearch)
                        {
                            progressDialog.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                progressDialog.UpdateGameStatusByGame(result.Game, BatchDownloadStatus.Downloading, "Searching for albums...");
                            }));

                            Album bestAlbum = null;

                            // Try cache first
                            if (_searchCacheService != null)
                            {
                                List<Album> cachedAlbums = null;
                                if (_searchCacheService.TryGetCachedAlbums(result.Game.Name, Source.KHInsider, out cachedAlbums) && cachedAlbums?.Count > 0)
                                {
                                    bestAlbum = _downloadManager.BestAlbumPick(cachedAlbums, result.Game);
                                    if (bestAlbum != null)
                                    {
                                        sourceName = "KHInsider";
                                        Logger.Info($"[AutoAddMoreSongs] Cache hit (KHInsider) for '{result.Game.Name}': {bestAlbum.Name}");
                                    }
                                }

                                if (bestAlbum == null && _searchCacheService.TryGetCachedAlbums(result.Game.Name, Source.YouTube, out cachedAlbums) && cachedAlbums?.Count > 0)
                                {
                                    bestAlbum = _downloadManager.BestAlbumPick(cachedAlbums, result.Game);
                                    if (bestAlbum != null)
                                    {
                                        sourceName = "YouTube";
                                        Logger.Info($"[AutoAddMoreSongs] Cache hit (YouTube) for '{result.Game.Name}': {bestAlbum.Name}");
                                    }
                                }
                            }

                            // Fresh search if no cache hit
                            if (bestAlbum == null)
                            {
                                try
                                {
                                    var searchAlbums = await System.Threading.Tasks.Task.Run(() =>
                                        _downloadManager.GetAlbumsForGame(result.Game.Name, Source.All, CancellationToken.None, auto: true));
                                    var searchAlbumsList = searchAlbums?.ToList() ?? new List<Album>();

                                    if (searchAlbumsList.Count > 0)
                                    {
                                        bestAlbum = _downloadManager.BestAlbumPick(searchAlbumsList, result.Game);
                                        if (bestAlbum != null)
                                        {
                                            sourceName = bestAlbum.Source.ToString();
                                            Logger.Info($"[AutoAddMoreSongs] Fresh search found album for '{result.Game.Name}': {bestAlbum.Name}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Warn($"[AutoAddMoreSongs] Error searching for '{result.Game.Name}': {ex.Message}");
                                }
                            }

                            if (bestAlbum == null)
                            {
                                progressDialog.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    progressDialog.UpdateGameStatusByGame(result.Game, BatchDownloadStatus.Skipped, "No albums found");
                                }));
                                return;
                            }

                            albumName = bestAlbum.Name;
                            albumId = bestAlbum.Id;
                            progressDialog.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                progressDialog.UpdateGameStatusByGame(result.Game, BatchDownloadStatus.Downloading, $"Found: {bestAlbum.Name}");
                            }));
                        }

                        // Now download songs for this game
                        progressDialog.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            progressDialog.UpdateGameStatusByGame(result.Game, BatchDownloadStatus.Downloading, $"Adding {songsPerGame} more songs...");
                        }));

                        var musicDir = _fileService.GetGameMusicDirectory(result.Game);
                        var existingSongs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (Directory.Exists(musicDir))
                        {
                            foreach (var file in Directory.GetFiles(musicDir))
                            {
                                var ext = Path.GetExtension(file).ToLowerInvariant();
                                if (Constants.SupportedAudioExtensions.Contains(ext))
                                {
                                    existingSongs.Add(Path.GetFileNameWithoutExtension(file));
                                }
                            }
                        }

                        int songsAdded = 0;
                        var source = GetSourceFromName(sourceName);

                        Album matchingAlbum = null;

                        // If we have an album ID (e.g., from UPS Search Hint), use it directly
                        if (!string.IsNullOrEmpty(albumId))
                        {
                            matchingAlbum = new Album
                            {
                                Id = albumId,
                                Name = albumName,
                                Source = source
                            };
                            Logger.Info($"[AutoAddMoreSongs] Using album ID directly for '{result.Game.Name}': {albumId}");
                        }
                        else
                        {
                            // Search using game name (not album name) to find matching albums
                            var albums = await System.Threading.Tasks.Task.Run(() =>
                                _downloadManager.GetAlbumsForGame(result.Game.Name, source, CancellationToken.None, auto: true));
                            var albumsList = albums?.ToList() ?? new List<Album>();

                            matchingAlbum = albumsList.FirstOrDefault(a =>
                                string.Equals(a.Name, albumName, StringComparison.OrdinalIgnoreCase));

                            if (matchingAlbum == null && albumsList.Count > 0)
                            {
                                matchingAlbum = albumsList.OrderByDescending(a =>
                                    FuzzySharp.Fuzz.Ratio(a.Name.ToLowerInvariant(), albumName.ToLowerInvariant()))
                                    .FirstOrDefault();
                            }
                        }

                        if (matchingAlbum != null)
                        {
                            var songs = await System.Threading.Tasks.Task.Run(() =>
                                _downloadManager.GetSongsFromAlbum(matchingAlbum, CancellationToken.None));
                            var songsList = songs?.ToList() ?? new List<Song>();

                            if (songsList.Count > 0)
                            {
                                var availableSongs = songsList.Where(s =>
                                    !existingSongs.Any(existing => SongNameMatches(existing, s.Name))).ToList();

                                var random = new Random(Guid.NewGuid().GetHashCode());
                                var songsToAdd = availableSongs.OrderBy(x => random.Next()).Take(songsPerGame).ToList();

                                foreach (var song in songsToAdd)
                                {
                                    try
                                    {
                                        if (!Directory.Exists(musicDir))
                                            Directory.CreateDirectory(musicDir);

                                        var safeFileName = Common.StringHelper.CleanForPath(song.Name);
                                        var extension = Path.GetExtension(song.Id);
                                        if (string.IsNullOrEmpty(extension) || extension == song.Id)
                                            extension = ".mp3";
                                        var downloadPath = Path.Combine(musicDir, safeFileName + extension);

                                        var downloadSuccess = await System.Threading.Tasks.Task.Run(() =>
                                            _downloadManager.DownloadSong(song, downloadPath, CancellationToken.None));

                                        if (downloadSuccess && File.Exists(downloadPath))
                                        {
                                            songsAdded++;
                                            Interlocked.Increment(ref totalAdded);
                                            existingSongs.Add(safeFileName);
                                            Logger.Info($"[AutoAddMoreSongs] Added '{song.Name}' for '{result.Game.Name}'");

                                            bool shouldStartPlayback = false;
                                            lock (downloadedSongsLock)
                                            {
                                                downloadedSongPaths.Add(downloadPath);
                                                if (!isPlayingNewSongs)
                                                {
                                                    isPlayingNewSongs = true;
                                                    shouldStartPlayback = true;
                                                }
                                            }

                                            if (shouldStartPlayback)
                                                playNextDownloadedSong();
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Warn($"[AutoAddMoreSongs] Failed to download song '{song.Name}': {ex.Message}");
                                    }
                                }
                            }
                        }

                        progressDialog.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (songsAdded > 0)
                            {
                                progressDialog.UpdateGameStatusByGame(result.Game, BatchDownloadStatus.Completed,
                                    $"+{songsAdded} songs added", albumName, sourceName);
                                progressDialog.MarkGameAsSongsAdded(result.Game);
                            }
                            else
                            {
                                progressDialog.UpdateGameStatusByGame(result.Game, BatchDownloadStatus.Completed,
                                    "No new songs available", albumName, sourceName);
                            }
                        }));

                        Interlocked.Increment(ref gamesProcessed);
                    };

                    // Process all games in parallel (up to 4 concurrent) - downloads start immediately as albums are found
                    const int MaxConcurrent = 4;
                    using (var semaphore = new SemaphoreSlim(MaxConcurrent))
                    {
                        // Combine successful results (no search needed) and skipped results (need album search)
                        var allTasks = new List<System.Threading.Tasks.Task>();

                        // Add tasks for successful results (already have album info)
                        foreach (var result in successfulResults)
                        {
                            allTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                            {
                                await semaphore.WaitAsync();
                                try
                                {
                                    await processGameAsync(result, false);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error(ex, $"[AutoAddMoreSongs] Error processing '{result.Game?.Name}': {ex.Message}");
                                    Interlocked.Increment(ref gamesFailed);
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }));
                        }

                        // Add tasks for skipped results (need to search for album first)
                        foreach (var skipped in skippedResults)
                        {
                            var result = new GameDownloadResult { Game = skipped.Game };
                            allTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                            {
                                await semaphore.WaitAsync();
                                try
                                {
                                    await processGameAsync(result, true);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error(ex, $"[AutoAddMoreSongs] Error processing '{result.Game?.Name}': {ex.Message}");
                                    Interlocked.Increment(ref gamesFailed);
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }));
                        }

                        if (allTasks.Count == 0)
                        {
                            Logger.Info("[AutoAddMoreSongs] No games to process");
                            progressDialog.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                _playniteApi.Dialogs.ShowMessage(
                                    "No games with album information found.\n\nTip: Click on individual games in Review Mode to manually search and download.",
                                    "Auto-Add Songs");
                            }));
                            return;
                        }

                        Logger.Info($"[AutoAddMoreSongs] Starting parallel processing for {allTasks.Count} games (downloads begin immediately as albums are found)");
                        await System.Threading.Tasks.Task.WhenAll(allTasks);
                    }

                    Logger.Info($"[AutoAddMoreSongs] Complete: {totalAdded} songs added to {gamesProcessed} games, {gamesFailed} failed");

                    progressDialog.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        progressDialog.HideAutoAddButton();
                        _playbackService?.RefreshSongCount();
                    }));
                }
                finally
                {
                    if (_playbackService != null)
                    {
                        _playbackService.OnSongEnded -= onSongEndedHandler;
                    }
                }
            });
        }

        /// <summary>
        /// Check if two song names match (accounting for cleaned filenames)
        /// </summary>
        private bool SongNameMatches(string existingFileName, string newSongName)
        {
            if (string.IsNullOrEmpty(existingFileName) || string.IsNullOrEmpty(newSongName))
                return false;

            var cleanedNew = Common.StringHelper.CleanForPath(newSongName);
            return string.Equals(existingFileName, cleanedNew, StringComparison.OrdinalIgnoreCase) ||
                   existingFileName.IndexOf(cleanedNew, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   cleanedNew.IndexOf(existingFileName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Get Source enum from display name
        /// </summary>
        private Source GetSourceFromName(string sourceName)
        {
            if (string.IsNullOrEmpty(sourceName))
                return Source.All;

            if (sourceName.IndexOf("KH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sourceName.IndexOf("Insider", StringComparison.OrdinalIgnoreCase) >= 0)
                return Source.KHInsider;

            if (sourceName.IndexOf("Zophar", StringComparison.OrdinalIgnoreCase) >= 0)
                return Source.Zophar;

            if (sourceName.IndexOf("YouTube", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sourceName.IndexOf("YT", StringComparison.OrdinalIgnoreCase) >= 0)
                return Source.YouTube;

            return Source.All;
        }

        private void HandleRedownloadRequest(BatchDownloadItem item, Views.BatchDownloadProgressDialog progressDialog)
        {
            if (item?.Game == null)
            {
                Logger.Warn("[HandleRedownloadRequest] Item or Game is null");
                return;
            }

            Logger.Info($"[HandleRedownloadRequest] Opening album search for: {item.GameName}");

            // Create a single-game batch manual download dialog
            // This gives us the big preview buttons and one-click album download behavior
            var singleGameList = new List<Game> { item.Game };
            var viewModel = new BatchManualDownloadViewModel(
                singleGameList,
                _downloadManager,
                _playbackService,
                _fileService,
                _playniteApi,
                _errorHandler)
            {
                // Enable single game mode to auto-close after download
                IsSingleGameMode = true
            };

            // Pre-load Material Design assemblies
            PreloadMaterialDesignAssemblies();

            var window = _playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = false,
                ShowCloseButton = true
            });

            window.Height = 500;
            window.Width = 700;
            window.Title = $"Re-download Music for: {item.GameName}";
            window.ShowInTaskbar = !IsFullscreenMode;
            window.Topmost = IsFullscreenMode;
            window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 33, 33));

            var view = new BatchManualDownloadDialog();
            window.Content = view;
            window.DataContext = viewModel;
            var ownerWindow = _playniteApi.Dialogs.GetCurrentAppWindow();
            window.Owner = ownerWindow;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Auto-select the game and go to album view on load
            window.Loaded += (s, e) =>
            {
                window.Activate();
                window.Focus();

                // Auto-select the game to immediately show album search
                var gameItem = viewModel.Games.FirstOrDefault();
                if (gameItem != null)
                {
                    // Simulate game selection to show album list
                    viewModel.SearchQuery = gameItem.GameName;

                    // Switch to album view
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        viewModel.IsGameListVisible = false;
                        viewModel.IsAlbumListVisible = true;
                        viewModel.SelectedGameItem = gameItem;

                        // Trigger search via the SearchCommand
                        if (viewModel.SearchCommand.CanExecute(null))
                        {
                            viewModel.SearchCommand.Execute(null);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            };

            // Set up close handler - when user clicks Complete/Cancel, close the dialog
            viewModel.CloseDialog = dialogResult =>
            {
                window.DialogResult = dialogResult;
                window.Close();
            };

            // Show the dialog (blocking)
            var result = window.ShowDialog();

            // Check if download was successful and update the progress dialog
            if (result == true || viewModel.SuccessCount > 0)
            {
                // Get the new music info from the downloaded game
                var musicDir = _fileService.GetGameMusicDirectory(item.Game);
                if (Directory.Exists(musicDir))
                {
                    var audioFiles = Directory.GetFiles(musicDir)
                        .Where(f => Constants.SupportedAudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToList();

                    if (audioFiles.Count > 0)
                    {
                        // Update the progress dialog item
                        progressDialog.UpdateItemAfterRedownload(item, Path.GetFileNameWithoutExtension(audioFiles.First()), "Manual");
                        Logger.Info($"[HandleRedownloadRequest] Successfully re-downloaded for: {item.GameName}");
                    }
                }
            }
            else
            {
                Logger.Info($"[HandleRedownloadRequest] Re-download cancelled or failed for: {item.GameName}");
            }
        }
    }
}


