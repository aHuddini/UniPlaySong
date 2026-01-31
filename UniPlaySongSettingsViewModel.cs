using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong
{
    public class UniPlaySongSettingsViewModel : ObservableObject, ISettings
    {
        private readonly UniPlaySong plugin;
        private UniPlaySongSettings settings;

        public UniPlaySongSettingsViewModel(UniPlaySong plugin)
        {
            this.plugin = plugin;
            
            var savedSettings = plugin.LoadPluginSettings<UniPlaySongSettings>();
            if (savedSettings != null)
            {
                settings = savedSettings;
                // Validate native music file if UseNativeMusicAsDefault is enabled
                // DefaultMusicPath is NOT modified - it remains separate and untouched
                if (settings.UseNativeMusicAsDefault == true)
                {
                    var settingsService = plugin.GetSettingsService();
                    settingsService?.ValidateNativeMusicFile(settings, showErrors: false);
                }
            }
            else
            {
                settings = new UniPlaySongSettings();
            }
            
            // Subscribe to property changes to handle UseNativeMusicAsDefault changes
            if (settings != null)
            {
                settings.PropertyChanged += OnSettingsPropertyChanged;
            }
        }
        
        private void OnSettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UniPlaySongSettings.UseNativeMusicAsDefault))
            {
                // When UseNativeMusicAsDefault changes, validate native music file exists
                // DefaultMusicPath is NOT modified - it remains separate
                if (settings?.UseNativeMusicAsDefault == true)
                {
                    var settingsService = plugin.GetSettingsService();
                    settingsService?.ValidateNativeMusicFile(settings, showErrors: true);
                }
            }
            // Note: ShowNowPlayingInTopPanel and ShowDesktopMediaControls now use SetRestartRequired command
            // which sets IsRestartRequired on the Playnite settings window for safe restart handling
        }

        /// <summary>
        /// Command that sets IsRestartRequired on the Playnite settings window.
        /// This triggers Playnite's built-in restart prompt when settings are saved.
        /// Bound to checkboxes that require restart (like top panel settings).
        /// </summary>
        public static ICommand SetRestartRequired => new Common.RelayCommand<object>((sender) =>
        {
            try
            {
                if (sender is FrameworkElement element)
                {
                    // Walk up the visual tree to find the settings window
                    var window = FindParentWindow(element);
                    if (window?.DataContext != null)
                    {
                        // Use reflection to set IsRestartRequired property on the window's DataContext
                        // This is a Playnite SDK property not exposed publicly but accessible via reflection
                        var property = window.DataContext.GetType().GetProperty("IsRestartRequired");
                        if (property != null && property.CanWrite)
                        {
                            property.SetValue(window.DataContext, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Error(ex, "Error setting IsRestartRequired");
            }
        });

        /// <summary>
        /// Finds the parent Window of a given element by walking up the visual tree.
        /// </summary>
        private static Window FindParentWindow(DependencyObject element)
        {
            while (element != null)
            {
                if (element is Window window)
                    return window;
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }


        public IPlayniteAPI PlayniteApi => plugin.PlayniteApi;

        /// <summary>
        /// Creates a new settings object with updated property value
        /// This ensures property change notifications are properly triggered
        /// </summary>
        private UniPlaySongSettings CreateSettingsWithUpdate(Action<UniPlaySongSettings> updateAction)
        {
            if (settings == null)
            {
                return new UniPlaySongSettings();
            }

            var newSettings = new UniPlaySongSettings
            {
                EnableMusic = settings.EnableMusic,
                MusicState = settings.MusicState,
                SkipFirstSelectionAfterModeSwitch = settings.SkipFirstSelectionAfterModeSwitch,
                ThemeCompatibleSilentSkip = settings.ThemeCompatibleSilentSkip,
                PauseOnTrailer = settings.PauseOnTrailer,
                MusicVolume = settings.MusicVolume,
                FadeInDuration = settings.FadeInDuration,
                FadeOutDuration = settings.FadeOutDuration,
                YtDlpPath = settings.YtDlpPath,
                FFmpegPath = settings.FFmpegPath,
                EnableDefaultMusic = settings.EnableDefaultMusic,
                DefaultMusicPath = settings.DefaultMusicPath,
                BackupCustomMusicPath = settings.BackupCustomMusicPath,
                SuppressPlayniteBackgroundMusic = settings.SuppressPlayniteBackgroundMusic,
                UseNativeMusicAsDefault = settings.UseNativeMusicAsDefault,
                NormalizationTargetLoudness = settings.NormalizationTargetLoudness,
                NormalizationTruePeak = settings.NormalizationTruePeak,
                NormalizationLoudnessRange = settings.NormalizationLoudnessRange,
                NormalizationCodec = settings.NormalizationCodec,
                NormalizationSuffix = settings.NormalizationSuffix,
                SkipAlreadyNormalized = settings.SkipAlreadyNormalized,
                DoNotPreserveOriginals = settings.DoNotPreserveOriginals
            };

            updateAction(newSettings);
            return newSettings;
        }

        public ICommand BrowseForYtDlpFile => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    var filePath = PlayniteApi.Dialogs.SelectFile("yt-dlp|yt-dlp.exe");
                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        Settings = CreateSettingsWithUpdate(s => s.YtDlpPath = filePath);
                    }
                },
                context: "selecting yt-dlp file",
                showUserMessage: true
            );
        });

        public ICommand BrowseForFFmpegFile => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    var filePath = PlayniteApi.Dialogs.SelectFile("ffmpeg|ffmpeg.exe");
                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        Settings = CreateSettingsWithUpdate(s => s.FFmpegPath = filePath);
                    }
                },
                context: "selecting FFmpeg file",
                showUserMessage: true
            );
        });


        public ICommand BrowseForDefaultMusicFile => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    var filePath = PlayniteApi.Dialogs.SelectFile("Audio Files|*.mp3;*.wav;*.flac;*.wma;*.aif;*.m4a;*.aac;*.mid");
                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        Settings = CreateSettingsWithUpdate(s => s.DefaultMusicPath = filePath);
                    }
                },
                context: "selecting default music file",
                showUserMessage: true
            );
        });

        public ICommand DownloadDefaultMusic => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    var downloadService = plugin.GetDownloadDialogService();
                    if (downloadService == null)
                    {
                        throw new InvalidOperationException("Download service not available. Please check extension settings.");
                    }

                    // Determine download path - use current DefaultMusicPath if set, otherwise use default directory
                    string downloadPath = Settings.DefaultMusicPath;
                    
                    if (string.IsNullOrWhiteSpace(downloadPath))
                    {
                    // Use a default location in extension data path
                    var extensionDataPath = PlayniteApi.Paths.ExtensionsDataPath;
                    var defaultMusicDir = Path.Combine(extensionDataPath, Constants.ExtensionFolderName, Constants.DefaultMusicFolderName);
                    Directory.CreateDirectory(defaultMusicDir);
                    downloadPath = defaultMusicDir; // Will be set to full file path after download
                    }
                    else if (File.Exists(downloadPath))
                    {
                        // If it's a file path, use the directory instead (will create new filename)
                        downloadPath = Path.GetDirectoryName(downloadPath);
                    }
                    // If it's a directory, keep it as is

                    // Show download dialog
                    var success = downloadService.ShowDefaultMusicDownloadDialog(downloadPath);
                    
                    if (success)
                    {
                        // Update DefaultMusicPath if download was successful
                        // Find the most recently downloaded file in the directory
                        if (Directory.Exists(downloadPath))
                        {
                            var files = Directory.GetFiles(downloadPath, "*.mp3")
                                .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                                .ToList();
                            
                            if (files.Count > 0)
                            {
                                Settings = CreateSettingsWithUpdate(s => s.DefaultMusicPath = files[0]); // Use most recent download
                            }
                        }
                    }
                },
                context: "downloading default music",
                showUserMessage: true
            );
        });

        public ICommand NormalizeAllMusicCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    plugin.NormalizeAllMusicFiles();
                },
                context: "normalizing all music files",
                showUserMessage: true
            );
        });

        public ICommand NormalizeSelectedGamesCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    var selectedGames = plugin.PlayniteApi.MainView.SelectedGames;
                    if (selectedGames == null || selectedGames.Count() == 0)
                    {
                        plugin.PlayniteApi.Dialogs.ShowMessage("Please select one or more games first.", "No Games Selected");
                        return;
                    }
                    plugin.NormalizeSelectedGames(selectedGames.ToList());
                },
                context: "normalizing selected games",
                showUserMessage: true
            );
        });

        public ICommand TrimAllMusicCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    plugin.TrimAllMusicFiles();
                },
                context: "trimming all music files",
                showUserMessage: true
            );
        });

        public ICommand TrimSelectedGamesCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    var selectedGames = plugin.PlayniteApi.MainView.SelectedGames;
                    if (selectedGames == null || selectedGames.Count() == 0)
                    {
                        plugin.PlayniteApi.Dialogs.ShowMessage("Please select one or more games first.", "No Games Selected");
                        return;
                    }
                    plugin.TrimSelectedGames(selectedGames.ToList());
                },
                context: "trimming selected games",
                showUserMessage: true
            );
        });

        public ICommand RestoreNormalizedFilesCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    var result = plugin.PlayniteApi.Dialogs.ShowMessage(
                        "This will restore original files from the PreservedOriginals folder.\n\n" +
                        "Normalized files (files with normalization suffix) will be deleted and original files will be moved back to your game music folders.\n\n" +
                        "Note: This only works if files were preserved (space saver mode was disabled). Files normalized with space saver mode cannot be restored.\n\n" +
                        "Do you want to restore all original files?",
                        "Restore Original Files",
                        System.Windows.MessageBoxButton.YesNo);
                    
                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        plugin.RestoreNormalizedFiles();
                    }
                },
                context: "deleting normalized files",
                showUserMessage: true
            );
        });

        public ICommand DeletePreservedOriginalsCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    var result = plugin.PlayniteApi.Dialogs.ShowMessage(
                        "⚠️ WARNING: This will permanently delete ALL files in the PreservedOriginals folder!\n\n" +
                        "This action cannot be undone. All preserved original files will be lost.\n\n" +
                        "This is useful for freeing up disk space after you've confirmed your normalized files work correctly.\n\n" +
                        "Are you absolutely sure you want to delete all preserved originals?",
                        "Delete Preserved Originals",
                        System.Windows.MessageBoxButton.YesNo);

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        plugin.DeletePreservedOriginals();
                    }
                },
                context: "deleting preserved originals",
                showUserMessage: true
            );
        });

        public ICommand OpenPreservedOriginalsFolderCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    // Use the same path as normalization/trim services: ConfigurationPath + ExtraMetadata + UniPlaySong + PreservedOriginals
                    var preservedOriginalsPath = Path.Combine(
                        PlayniteApi.Paths.ConfigurationPath,
                        Constants.ExtraMetadataFolderName,
                        Constants.ExtensionFolderName,
                        Constants.PreservedOriginalsFolderName);

                    if (!Directory.Exists(preservedOriginalsPath))
                    {
                        Directory.CreateDirectory(preservedOriginalsPath);
                    }

                    System.Diagnostics.Process.Start("explorer.exe", preservedOriginalsPath);
                },
                context: "opening preserved originals folder",
                showUserMessage: true
            );
        });

        public ICommand ClearSearchCache => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    var cacheService = plugin.GetSearchCacheService();
                    if (cacheService == null)
                    {
                        PlayniteApi.Dialogs.ShowMessage("Cache service not available.", "UniPlaySong");
                        return;
                    }

                    var result = PlayniteApi.Dialogs.ShowMessage(
                        "Are you sure you want to clear all cached search results?\n\n" +
                        "This will remove all cached album searches from KHInsider and YouTube. " +
                        "Future searches will be slower until the cache is rebuilt.",
                        "Clear Search Cache",
                        System.Windows.MessageBoxButton.YesNo);

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        cacheService.ClearCache();
                        UpdateCacheStats();
                        PlayniteApi.Dialogs.ShowMessage(
                            "Search cache cleared successfully.",
                            "UniPlaySong");
                    }
                },
                context: "clearing search cache",
                showUserMessage: true
            );
        });

        public ICommand DownloadMusicForAllGamesCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    plugin.DownloadMusicForAllGamesFromSettings();
                },
                context: "downloading music for all games",
                showUserMessage: true
            );
        });

        private string _cacheStatsText = "Cache: Loading...";
        public string CacheStatsText
        {
            get => _cacheStatsText;
            set
            {
                _cacheStatsText = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Display-only property showing current song title | duration.
        /// Not serialized to settings file. Uses same metadata reading as the top panel.
        /// </summary>
        public string CurrentSongDisplay
        {
            get
            {
                try
                {
                    var playbackService = plugin.GetPlaybackService();
                    if (playbackService == null || !playbackService.IsPlaying)
                        return "(No song playing)";

                    var currentPath = playbackService.CurrentSongPath;
                    if (string.IsNullOrEmpty(currentPath))
                        return "(No song playing)";

                    // If playing default music, don't show song info
                    if (playbackService.IsPlayingDefaultMusic)
                        return "(Default music)";

                    if (!System.IO.File.Exists(currentPath))
                        return "(File not found)";

                    // Read embedded metadata from file (same as SongMetadataService)
                    string title = null;
                    string artist = null;
                    var duration = System.TimeSpan.Zero;

                    try
                    {
                        using (var file = TagLib.File.Create(currentPath))
                        {
                            // Get title from embedded metadata
                            title = file.Tag?.Title;

                            // Get artist from embedded metadata (first performer)
                            if (file.Tag?.Performers != null && file.Tag.Performers.Length > 0)
                            {
                                artist = file.Tag.Performers[0];
                            }

                            // Get duration
                            duration = file.Properties?.Duration ?? System.TimeSpan.Zero;
                        }
                    }
                    catch { }

                    // If no embedded title, fall back to parsing filename
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        DeskMediaControl.SongTitleCleaner.ParseFilename(currentPath, out title, out string filenameArtist);

                        // Use filename-extracted artist if no embedded artist
                        if (string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(filenameArtist))
                        {
                            artist = filenameArtist;
                        }
                    }

                    // Format display text (same as SongInfo.DisplayText)
                    return DeskMediaControl.SongTitleCleaner.FormatDisplayText(title, artist, duration);
                }
                catch
                {
                    return "(Unable to read song info)";
                }
            }
        }

        #region Search Hints Database Properties and Commands

        private string _hintsDatabaseStatus = "Loading...";
        public string HintsDatabaseStatus
        {
            get => _hintsDatabaseStatus;
            set
            {
                _hintsDatabaseStatus = value;
                OnPropertyChanged();
            }
        }

        private bool _hasDownloadedHints = false;
        public bool HasDownloadedHints
        {
            get => _hasDownloadedHints;
            set
            {
                _hasDownloadedHints = value;
                OnPropertyChanged();
            }
        }

        public ICommand DownloadHintsFromGitHub => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    var hintsService = plugin.GetSearchHintsService();
                    if (hintsService == null)
                    {
                        PlayniteApi.Dialogs.ShowMessage("Search hints service not available.", "UniPlaySong");
                        return;
                    }

                    HintsDatabaseStatus = "Downloading...";

                    var success = hintsService.DownloadHintsFromGitHub();

                    if (success)
                    {
                        UpdateHintsDatabaseStatus();
                        PlayniteApi.Dialogs.ShowMessage(
                            "Search hints database downloaded successfully from GitHub.\n\n" +
                            "The new hints will be used for future auto-download operations.",
                            "Download Complete");
                    }
                    else
                    {
                        UpdateHintsDatabaseStatus();
                        PlayniteApi.Dialogs.ShowErrorMessage(
                            "Failed to download search hints from GitHub.\n\n" +
                            "Please check your internet connection and try again.",
                            "Download Failed");
                    }
                },
                context: "downloading hints from GitHub",
                showUserMessage: true
            );
        });

        public ICommand OpenHintsDatabaseFolder => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    var hintsService = plugin.GetSearchHintsService();
                    if (hintsService == null)
                    {
                        PlayniteApi.Dialogs.ShowMessage("Search hints service not available.", "UniPlaySong");
                        return;
                    }

                    var folderPath = hintsService.GetAutoSearchDatabasePath();

                    // Create folder if it doesn't exist
                    if (!System.IO.Directory.Exists(folderPath))
                    {
                        System.IO.Directory.CreateDirectory(folderPath);
                    }

                    System.Diagnostics.Process.Start("explorer.exe", folderPath);
                },
                context: "opening hints database folder",
                showUserMessage: true
            );
        });

        public ICommand RevertToBundledHints => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    var hintsService = plugin.GetSearchHintsService();
                    if (hintsService == null)
                    {
                        PlayniteApi.Dialogs.ShowMessage("Search hints service not available.", "UniPlaySong");
                        return;
                    }

                    var result = PlayniteApi.Dialogs.ShowMessage(
                        "Are you sure you want to delete the downloaded hints database?\n\n" +
                        "The extension will revert to using the bundled hints that shipped with the extension.",
                        "Revert to Bundled Hints",
                        System.Windows.MessageBoxButton.YesNo);

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        hintsService.DeleteDownloadedHints();
                        UpdateHintsDatabaseStatus();
                        PlayniteApi.Dialogs.ShowMessage(
                            "Downloaded hints deleted. Now using bundled hints.",
                            "UniPlaySong");
                    }
                },
                context: "reverting to bundled hints",
                showUserMessage: true
            );
        });

        public void UpdateHintsDatabaseStatus()
        {
            try
            {
                var hintsService = plugin.GetSearchHintsService();
                if (hintsService == null)
                {
                    HintsDatabaseStatus = "Service not available";
                    HasDownloadedHints = false;
                    return;
                }

                HasDownloadedHints = hintsService.HasDownloadedHints();

                if (HasDownloadedHints)
                {
                    HintsDatabaseStatus = hintsService.GetDownloadedHintsStatus();
                }
                else
                {
                    HintsDatabaseStatus = "Using bundled database (not downloaded from GitHub)";
                }
            }
            catch (Exception ex)
            {
                HintsDatabaseStatus = $"Error: {ex.Message}";
                HasDownloadedHints = false;
            }
        }

        #endregion

        #region Migration Properties and Commands

        private string _migrationPlayniteSoundStatus = "Not scanned";
        public string MigrationPlayniteSoundStatus
        {
            get => _migrationPlayniteSoundStatus;
            set
            {
                _migrationPlayniteSoundStatus = value;
                OnPropertyChanged();
            }
        }

        private string _migrationUniPlaySongStatus = "Not scanned";
        public string MigrationUniPlaySongStatus
        {
            get => _migrationUniPlaySongStatus;
            set
            {
                _migrationUniPlaySongStatus = value;
                OnPropertyChanged();
            }
        }

        public ICommand RefreshMigrationStatusCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () => RefreshMigrationStatus(),
                context: "refreshing migration status",
                showUserMessage: true
            );
        });

        private void RefreshMigrationStatus()
        {
            var migrationService = plugin.GetMigrationService();
            if (migrationService == null)
            {
                MigrationPlayniteSoundStatus = "Service not available";
                MigrationUniPlaySongStatus = "Service not available";
                return;
            }

            var summary = migrationService.GetMigrationSummary();

            MigrationPlayniteSoundStatus = summary.PlayniteSoundGameCount > 0
                ? $"{summary.PlayniteSoundGameCount} games, {summary.PlayniteSoundFileCount} files"
                : "No music found";

            MigrationUniPlaySongStatus = summary.UniPlaySongGameCount > 0
                ? $"{summary.UniPlaySongGameCount} games, {summary.UniPlaySongFileCount} files"
                : "No music found";
        }

        public ICommand MigrateFromPlayniteSoundCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () => ExecuteMigrateFromPlayniteSound(),
                context: "migrating from PlayniteSound",
                showUserMessage: true
            );
        });

        private void ExecuteMigrateFromPlayniteSound()
        {
            var migrationService = plugin.GetMigrationService();
            if (migrationService == null)
            {
                PlayniteApi.Dialogs.ShowMessage("Migration service not available.", "UniPlaySong");
                return;
            }

            var games = migrationService.ScanPlayniteSoundGames();

            if (games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    "No music files found in PlayniteSound directories.\n\n" +
                    "PlayniteSound stores music in:\n" +
                    "%APPDATA%\\Playnite\\ExtraMetadata\\Games\\{GameId}\\Music Files\\",
                    "No Music Found");
                return;
            }

            var totalFiles = games.Sum(g => g.FileCount);
            var result = PlayniteApi.Dialogs.ShowMessage(
                $"Found {games.Count} games with {totalFiles} music files in PlayniteSound.\n\n" +
                "This will copy all music files to UniPlaySong format.\n" +
                "Existing files in UniPlaySong will be skipped (not overwritten).\n\n" +
                "Do you want to proceed?",
                "Import from PlayniteSound",
                System.Windows.MessageBoxButton.YesNo);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            // Show progress dialog and run migration
            plugin.RunMigrationWithProgress(
                "Importing from PlayniteSound",
                async (progress, token) => await migrationService.MigrateAllFromPlayniteSoundAsync(progress, token));

            // Refresh status after migration
            RefreshMigrationStatus();
        }

        public ICommand MigrateFromPlayniteSoundAndDeleteCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () => ExecuteMigrateFromPlayniteSoundAndDelete(),
                context: "migrating from PlayniteSound and deleting originals",
                showUserMessage: true
            );
        });

        private void ExecuteMigrateFromPlayniteSoundAndDelete()
        {
            var migrationService = plugin.GetMigrationService();
            if (migrationService == null)
            {
                PlayniteApi.Dialogs.ShowMessage("Migration service not available.", "UniPlaySong");
                return;
            }

            var games = migrationService.ScanPlayniteSoundGames();

            if (games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    "No music files found in PlayniteSound directories.\n\n" +
                    "PlayniteSound stores music in:\n" +
                    "%APPDATA%\\Playnite\\ExtraMetadata\\Games\\{GameId}\\Music Files\\",
                    "No Music Found");
                return;
            }

            var totalFiles = games.Sum(g => g.FileCount);

            // Warning dialog with clear message about deletion
            var result = PlayniteApi.Dialogs.ShowMessage(
                $"Found {games.Count} games with {totalFiles} music files in PlayniteSound.\n\n" +
                "This will:\n" +
                "1. Copy all music files to UniPlaySong format\n" +
                "2. DELETE the original PlayniteSound music files\n\n" +
                "WARNING: Original PlayniteSound music files will be permanently deleted!\n" +
                "This cannot be undone.\n\n" +
                "Do you want to proceed?",
                "Import from PlayniteSound & Delete",
                System.Windows.MessageBoxButton.YesNo);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            // Second confirmation
            var confirm = PlayniteApi.Dialogs.ShowMessage(
                "FINAL CONFIRMATION\n\n" +
                $"After import, {totalFiles} music files in PlayniteSound will be DELETED.\n\n" +
                "Are you sure you want to continue?",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo);

            if (confirm != System.Windows.MessageBoxResult.Yes)
                return;

            // Step 1: Import
            plugin.RunMigrationWithProgress(
                "Importing from PlayniteSound",
                async (progress, token) => await migrationService.MigrateAllFromPlayniteSoundAsync(progress, token));

            // Step 2: Delete PlayniteSound files
            plugin.RunDeleteWithProgress(
                "Deleting PlayniteSound Music",
                async (progress, token) => await migrationService.DeletePlayniteSoundMusicAsync(progress, token));

            // Show completion message
            var summary = migrationService.GetMigrationSummary();
            PlayniteApi.Dialogs.ShowMessage(
                $"Migration complete!\n\n" +
                $"UniPlaySong now has: {summary.UniPlaySongGameCount} games, {summary.UniPlaySongFileCount} files\n" +
                $"PlayniteSound remaining: {summary.PlayniteSoundGameCount} games, {summary.PlayniteSoundFileCount} files",
                "Import & Delete Complete");

            // Refresh status after migration
            RefreshMigrationStatus();
        }

        public ICommand MigrateToPlayniteSoundCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () => ExecuteMigrateToPlayniteSound(),
                context: "migrating to PlayniteSound",
                showUserMessage: true
            );
        });

        private void ExecuteMigrateToPlayniteSound()
        {
            var migrationService = plugin.GetMigrationService();
            if (migrationService == null)
            {
                PlayniteApi.Dialogs.ShowMessage("Migration service not available.", "UniPlaySong");
                return;
            }

            var games = migrationService.ScanUniPlaySongGames();

            if (games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    "No music files found in UniPlaySong directories.\n\n" +
                    "UniPlaySong stores music in:\n" +
                    "%APPDATA%\\Playnite\\ExtraMetadata\\UniPlaySong\\Games\\{GameId}\\",
                    "No Music Found");
                return;
            }

            var totalFiles = games.Sum(g => g.FileCount);
            var result = PlayniteApi.Dialogs.ShowMessage(
                $"Found {games.Count} games with {totalFiles} music files in UniPlaySong.\n\n" +
                "This will copy all music files to PlayniteSound format.\n" +
                "Existing files in PlayniteSound will be skipped (not overwritten).\n\n" +
                "Do you want to proceed?",
                "Export to PlayniteSound",
                System.Windows.MessageBoxButton.YesNo);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            // Show progress dialog and run migration
            plugin.RunMigrationWithProgress(
                "Exporting to PlayniteSound",
                async (progress, token) => await migrationService.MigrateAllToPlayniteSoundAsync(progress, token));

            // Refresh status after migration
            RefreshMigrationStatus();
        }

        #endregion

        #region Cleanup Properties and Commands

        private string _cleanupStorageInfo = "Loading...";
        public string CleanupStorageInfo
        {
            get => _cleanupStorageInfo;
            set
            {
                _cleanupStorageInfo = value;
                OnPropertyChanged();
            }
        }

        public ICommand RefreshCleanupStorageInfoCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () => RefreshCleanupStorageInfo(),
                context: "refreshing storage info",
                showUserMessage: true
            );
        });

        private void RefreshCleanupStorageInfo()
        {
            var (gameCount, fileCount, totalBytes, preservedCount, preservedBytes) = plugin.GetStorageInfo();

            var totalMB = totalBytes / 1024.0 / 1024.0;
            var preservedMB = preservedBytes / 1024.0 / 1024.0;
            var combinedMB = (totalBytes + preservedBytes) / 1024.0 / 1024.0;

            CleanupStorageInfo = $"Games: {gameCount}\n" +
                                 $"Music Files: {fileCount} ({totalMB:F2} MB)\n" +
                                 $"Preserved Originals: {preservedCount} ({preservedMB:F2} MB)\n" +
                                 $"Total Storage: {combinedMB:F2} MB";
        }

        public ICommand DeleteAllMusicCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () => ExecuteDeleteAllMusic(),
                context: "deleting all music",
                showUserMessage: true
            );
        });

        private void ExecuteDeleteAllMusic()
        {
            var (gameCount, fileCount, totalBytes, preservedCount, preservedBytes) = plugin.GetStorageInfo();
            var totalFiles = fileCount + preservedCount;

            if (totalFiles == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("No music files to delete.", "UniPlaySong");
                return;
            }

            var result = PlayniteApi.Dialogs.ShowMessage(
                $"This will permanently delete:\n\n" +
                $"- {fileCount} music files across {gameCount} games\n" +
                $"- {preservedCount} preserved original files\n\n" +
                $"This action cannot be undone. Are you sure?",
                "Delete All Music",
                System.Windows.MessageBoxButton.YesNo);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            // Second confirmation for safety
            var confirm = PlayniteApi.Dialogs.ShowMessage(
                "FINAL WARNING: All music files will be permanently deleted.\n\n" +
                "Type 'DELETE' in your mind and click Yes to confirm.",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo);

            if (confirm != System.Windows.MessageBoxResult.Yes)
                return;

            var (deletedFiles, deletedFolders, success) = plugin.DeleteAllMusic();

            if (success)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    $"Deleted {deletedFiles} files in {deletedFolders} game folders.",
                    "Delete Complete");
            }
            else
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Some files could not be deleted. Check the log for details.",
                    "Delete Failed");
            }

            RefreshCleanupStorageInfo();
        }

        public ICommand CleanupOrphanedMusicCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () => ExecuteCleanupOrphanedMusic(),
                context: "cleaning up orphaned music",
                showUserMessage: true
            );
        });

        private void ExecuteCleanupOrphanedMusic()
        {
            var orphanCount = plugin.CountOrphanedMusicFolders();

            if (orphanCount == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    "No orphaned music folders found. All music folders belong to games in your library.",
                    "UniPlaySong");
                return;
            }

            var result = PlayniteApi.Dialogs.ShowMessage(
                $"Found {orphanCount} orphaned music folder(s) belonging to games no longer in your library.\n\n" +
                $"Do you want to delete them?",
                "Clean Up Orphaned Music",
                System.Windows.MessageBoxButton.YesNo);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            var (deletedFolders, deletedFiles, success) = plugin.CleanupOrphanedMusic();

            if (success)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    $"Cleaned up {deletedFolders} orphaned folder(s) ({deletedFiles} files).",
                    "Cleanup Complete");
            }
            else
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Some folders could not be deleted. Check the log for details.",
                    "Cleanup Failed");
            }

            RefreshCleanupStorageInfo();
        }

        public ICommand ResetSettingsCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () => ExecuteResetSettings(),
                context: "resetting settings",
                showUserMessage: true
            );
        });

        private void ExecuteResetSettings()
        {
            var result = PlayniteApi.Dialogs.ShowMessage(
                "This will reset all UniPlaySong settings to their default values.\n\n" +
                "Your music files will NOT be deleted.\n" +
                "Tool paths (FFmpeg, yt-dlp) will be preserved.\n\n" +
                "Do you want to proceed?",
                "Reset Settings",
                System.Windows.MessageBoxButton.YesNo);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            var success = plugin.ResetSettingsToDefaults();

            if (success)
            {
                // Reload settings to update UI
                var settingsService = plugin.GetSettingsService();
                settingsService?.LoadSettings();
                Settings = settingsService?.Current;

                PlayniteApi.Dialogs.ShowMessage(
                    "Settings have been reset to defaults.\n\n" +
                    "Please close and reopen the settings dialog to see the changes.",
                    "Reset Complete");
            }
            else
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Failed to reset settings. Check the log for details.",
                    "Reset Failed");
            }
        }

        public ICommand FactoryResetCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () => ExecuteFactoryReset(),
                context: "factory reset",
                showUserMessage: true
            );
        });

        private void ExecuteFactoryReset()
        {
            var (gameCount, fileCount, totalBytes, preservedCount, preservedBytes) = plugin.GetStorageInfo();
            var totalFiles = fileCount + preservedCount;

            var result = PlayniteApi.Dialogs.ShowMessage(
                "FACTORY RESET will permanently:\n\n" +
                $"- Delete {fileCount} music files\n" +
                $"- Delete {preservedCount} preserved originals\n" +
                $"- Remove {gameCount} game folders\n" +
                "- Clear the search cache\n" +
                "- Reset all settings to defaults\n\n" +
                "This action CANNOT be undone!\n\n" +
                "Are you absolutely sure?",
                "Factory Reset - WARNING",
                System.Windows.MessageBoxButton.YesNo);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            // Second confirmation
            var confirm = PlayniteApi.Dialogs.ShowMessage(
                "FINAL CONFIRMATION\n\n" +
                "You are about to completely reset UniPlaySong.\n" +
                "ALL downloaded music will be PERMANENTLY DELETED.\n\n" +
                "Click Yes to proceed with factory reset.",
                "Factory Reset - Final Confirmation",
                System.Windows.MessageBoxButton.YesNo);

            if (confirm != System.Windows.MessageBoxResult.Yes)
                return;

            var (deletedFiles, deletedFolders, success) = plugin.FactoryReset();

            if (success)
            {
                // Reload settings to update UI
                var settingsService = plugin.GetSettingsService();
                settingsService?.LoadSettings();
                Settings = settingsService?.Current;

                PlayniteApi.Dialogs.ShowMessage(
                    $"Factory reset complete!\n\n" +
                    $"- Deleted {deletedFiles} files\n" +
                    $"- Removed {deletedFolders} game folders\n" +
                    $"- Settings reset to defaults\n\n" +
                    "Please close and reopen the settings dialog.",
                    "Factory Reset Complete");
            }
            else
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Factory reset encountered errors. Check the log for details.",
                    "Factory Reset Failed");
            }

            RefreshCleanupStorageInfo();
        }

        public ICommand DeleteLongSongsCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () => ExecuteDeleteLongSongs(),
                context: "deleting long songs",
                showUserMessage: true
            );
        });

        private void ExecuteDeleteLongSongs()
        {
            const int maxMinutes = 10;

            // Scan with progress dialog
            List<(string filePath, TimeSpan duration, long fileSize, string gameFolder)> longSongs = null;
            bool scanCancelled = false;

            var scanOptions = new GlobalProgressOptions("Scanning for long songs...", true)
            {
                IsIndeterminate = false
            };

            PlayniteApi.Dialogs.ActivateGlobalProgress((args) =>
            {
                longSongs = plugin.GetLongSongs(maxMinutes, args);
                scanCancelled = args.CancelToken.IsCancellationRequested;
            }, scanOptions);

            if (scanCancelled || longSongs == null)
            {
                return;
            }

            if (longSongs.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    $"No songs longer than {maxMinutes} minutes found.",
                    "UniPlaySong");
                return;
            }

            // Calculate total size (already captured during scan)
            long totalBytes = longSongs.Sum(s => s.fileSize);
            var totalMB = totalBytes / 1024.0 / 1024.0;

            // Build preview list (show up to 10 files)
            var previewList = string.Join("\n", longSongs
                .Take(10)
                .Select(s => $"  - {System.IO.Path.GetFileName(s.filePath)} ({s.duration:hh\\:mm\\:ss})"));
            if (longSongs.Count > 10)
            {
                previewList += $"\n  ... and {longSongs.Count - 10} more files";
            }

            var result = PlayniteApi.Dialogs.ShowMessage(
                $"Found {longSongs.Count} songs longer than {maxMinutes} minutes ({totalMB:F1} MB total):\n\n" +
                $"{previewList}\n\n" +
                "Delete these files?",
                "Delete Long Songs",
                System.Windows.MessageBoxButton.YesNo);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            // Delete with progress dialog
            int deletedFiles = 0;
            long freedBytes = 0;
            bool deleteSuccess = false;

            var deleteOptions = new GlobalProgressOptions("Deleting long songs...", true)
            {
                IsIndeterminate = false
            };

            PlayniteApi.Dialogs.ActivateGlobalProgress((args) =>
            {
                var deleteResult = plugin.DeleteLongSongs(longSongs, args);
                deletedFiles = deleteResult.deletedFiles;
                freedBytes = deleteResult.freedBytes;
                deleteSuccess = deleteResult.success;
            }, deleteOptions);

            var freedMB = freedBytes / 1024.0 / 1024.0;

            if (deleteSuccess && deletedFiles > 0)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    $"Deleted {deletedFiles} songs, freed {freedMB:F1} MB.",
                    "Delete Complete");
            }
            else if (deletedFiles == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    "No files were deleted (operation may have been cancelled).",
                    "Delete Cancelled");
            }
            else
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Some files could not be deleted. Check the log for details.",
                    "Delete Failed");
            }

            RefreshCleanupStorageInfo();
        }

        #endregion

        #region Music Status Tag Commands

        public ICommand ScanAndTagAllGamesCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () => ExecuteScanAndTagAllGames(),
                context: "scanning and tagging games",
                showUserMessage: true
            );
        });

        private void ExecuteScanAndTagAllGames()
        {
            var tagService = plugin.GetGameMusicTagService();
            if (tagService == null)
            {
                PlayniteApi.Dialogs.ShowMessage("Tag service not available.", "UniPlaySong");
                return;
            }

            var games = PlayniteApi.Database.Games.ToList();
            if (games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("No games found in library.", "UniPlaySong");
                return;
            }

            var result = PlayniteApi.Dialogs.ShowMessage(
                $"This will scan {games.Count} games and update their music status tags.\n\n" +
                "Games will be tagged with:\n" +
                "- '[UPS] Has Music' - if they have downloaded music\n" +
                "- '[UPS] No Music' - if they don't have music\n\n" +
                "You can then use Playnite's filter to find games by these tags.\n\n" +
                "Do you want to proceed?",
                "Scan & Tag Games",
                System.Windows.MessageBoxButton.YesNo);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            // Run with progress dialog
            plugin.RunTagScanWithProgress(tagService);
        }

        public ICommand RemoveAllMusicTagsCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () => ExecuteRemoveAllMusicTags(),
                context: "removing music tags",
                showUserMessage: true
            );
        });

        private void ExecuteRemoveAllMusicTags()
        {
            var tagService = plugin.GetGameMusicTagService();
            if (tagService == null)
            {
                PlayniteApi.Dialogs.ShowMessage("Tag service not available.", "UniPlaySong");
                return;
            }

            var result = PlayniteApi.Dialogs.ShowMessage(
                "This will remove all UniPlaySong music status tags from your games.\n\n" +
                "The tags '[UPS] Has Music' and '[UPS] No Music' will be removed from all games,\n" +
                "and deleted from the tag database.\n\n" +
                "Do you want to proceed?",
                "Remove All Music Tags",
                System.Windows.MessageBoxButton.YesNo);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            tagService.RemoveAllMusicTags();

            PlayniteApi.Dialogs.ShowMessage(
                "All music status tags have been removed.",
                "Tags Removed");
        }

        #endregion

        #region Toast Notification Test Commands

        public ICommand ShowTestToastCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    // Sync current settings to DialogHelper before showing test
                    DialogHelper.SyncToastSettings(settings);

                    // Show a test success toast
                    DialogHelper.ShowSuccessToast(
                        PlayniteApi,
                        "This is a test toast notification.\nLine 2 shows spacing.",
                        "Test Toast");
                },
                context: "showing test toast",
                showUserMessage: false
            );
        });

        public ICommand ShowTestErrorToastCommand => new Common.RelayCommand<object>((a) =>
        {
            var errorHandler = plugin.GetErrorHandlerService();
            errorHandler?.Try(
                () =>
                {
                    // Sync current settings to DialogHelper before showing test
                    DialogHelper.SyncToastSettings(settings);

                    // Show a test error toast
                    DialogHelper.ShowErrorToast(
                        PlayniteApi,
                        "This is a test error toast.\nError details would appear here.",
                        "Test Error");
                },
                context: "showing test error toast",
                showUserMessage: false
            );
        });

        /// <summary>
        /// Command to set the toast tint color from a preset hex value
        /// </summary>
        public ICommand SetToastColorCommand => new Common.RelayCommand<string>((hexColor) =>
        {
            if (!string.IsNullOrEmpty(hexColor) && settings != null)
            {
                settings.ToastBlurTintColor = hexColor;
                // Trigger property changed for RGB values
                OnPropertyChanged(nameof(ToastColorRed));
                OnPropertyChanged(nameof(ToastColorGreen));
                OnPropertyChanged(nameof(ToastColorBlue));
            }
        });

        /// <summary>
        /// Red component (0-255) of the toast tint color
        /// </summary>
        public int ToastColorRed
        {
            get
            {
                if (settings == null || string.IsNullOrEmpty(settings.ToastBlurTintColor))
                    return 30; // 0x1E
                try
                {
                    var hex = settings.ToastBlurTintColor.TrimStart('#');
                    if (hex.Length >= 2)
                        return Convert.ToInt32(hex.Substring(0, 2), 16);
                }
                catch { }
                return 30;
            }
            set
            {
                var r = Math.Max(0, Math.Min(255, value));
                var g = ToastColorGreen;
                var b = ToastColorBlue;
                if (settings != null)
                {
                    settings.ToastBlurTintColor = $"{r:X2}{g:X2}{b:X2}";
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Green component (0-255) of the toast tint color
        /// </summary>
        public int ToastColorGreen
        {
            get
            {
                if (settings == null || string.IsNullOrEmpty(settings.ToastBlurTintColor))
                    return 30; // 0x1E
                try
                {
                    var hex = settings.ToastBlurTintColor.TrimStart('#');
                    if (hex.Length >= 4)
                        return Convert.ToInt32(hex.Substring(2, 2), 16);
                }
                catch { }
                return 30;
            }
            set
            {
                var r = ToastColorRed;
                var g = Math.Max(0, Math.Min(255, value));
                var b = ToastColorBlue;
                if (settings != null)
                {
                    settings.ToastBlurTintColor = $"{r:X2}{g:X2}{b:X2}";
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Blue component (0-255) of the toast tint color
        /// </summary>
        public int ToastColorBlue
        {
            get
            {
                if (settings == null || string.IsNullOrEmpty(settings.ToastBlurTintColor))
                    return 30; // 0x1E
                try
                {
                    var hex = settings.ToastBlurTintColor.TrimStart('#');
                    if (hex.Length >= 6)
                        return Convert.ToInt32(hex.Substring(4, 2), 16);
                }
                catch { }
                return 30;
            }
            set
            {
                var r = ToastColorRed;
                var g = ToastColorGreen;
                var b = Math.Max(0, Math.Min(255, value));
                if (settings != null)
                {
                    settings.ToastBlurTintColor = $"{r:X2}{g:X2}{b:X2}";
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Command to adjust the brightness of the current tint color.
        /// Positive values lighten, negative values darken.
        /// </summary>
        public ICommand AdjustBrightnessCommand => new Common.RelayCommand<string>((deltaStr) =>
        {
            if (settings == null || string.IsNullOrEmpty(deltaStr)) return;

            if (int.TryParse(deltaStr, out int delta))
            {
                // Get current RGB values
                int r = ToastColorRed;
                int g = ToastColorGreen;
                int b = ToastColorBlue;

                // Adjust each channel by delta, clamping to 0-255
                r = Math.Max(0, Math.Min(255, r + delta));
                g = Math.Max(0, Math.Min(255, g + delta));
                b = Math.Max(0, Math.Min(255, b + delta));

                // Update the color
                settings.ToastBlurTintColor = $"{r:X2}{g:X2}{b:X2}";

                // Notify all RGB properties changed
                OnPropertyChanged(nameof(ToastColorRed));
                OnPropertyChanged(nameof(ToastColorGreen));
                OnPropertyChanged(nameof(ToastColorBlue));
            }
        });

        // ===== Toast Border Color Settings =====

        /// <summary>
        /// Command to set the toast border color from a preset hex value
        /// </summary>
        public ICommand SetToastBorderColorCommand => new Common.RelayCommand<string>((hexColor) =>
        {
            if (!string.IsNullOrEmpty(hexColor) && settings != null)
            {
                settings.ToastBorderColor = hexColor;
                // Trigger property changed for RGB values
                OnPropertyChanged(nameof(ToastBorderRed));
                OnPropertyChanged(nameof(ToastBorderGreen));
                OnPropertyChanged(nameof(ToastBorderBlue));
            }
        });

        /// <summary>
        /// Red component (0-255) of the toast border color
        /// </summary>
        public int ToastBorderRed
        {
            get
            {
                if (settings == null || string.IsNullOrEmpty(settings.ToastBorderColor))
                    return 42; // 0x2A
                try
                {
                    var hex = settings.ToastBorderColor.TrimStart('#');
                    if (hex.Length >= 2)
                        return Convert.ToInt32(hex.Substring(0, 2), 16);
                }
                catch { }
                return 42;
            }
            set
            {
                var r = Math.Max(0, Math.Min(255, value));
                var g = ToastBorderGreen;
                var b = ToastBorderBlue;
                if (settings != null)
                {
                    settings.ToastBorderColor = $"{r:X2}{g:X2}{b:X2}";
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Green component (0-255) of the toast border color
        /// </summary>
        public int ToastBorderGreen
        {
            get
            {
                if (settings == null || string.IsNullOrEmpty(settings.ToastBorderColor))
                    return 42; // 0x2A
                try
                {
                    var hex = settings.ToastBorderColor.TrimStart('#');
                    if (hex.Length >= 4)
                        return Convert.ToInt32(hex.Substring(2, 2), 16);
                }
                catch { }
                return 42;
            }
            set
            {
                var r = ToastBorderRed;
                var g = Math.Max(0, Math.Min(255, value));
                var b = ToastBorderBlue;
                if (settings != null)
                {
                    settings.ToastBorderColor = $"{r:X2}{g:X2}{b:X2}";
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Blue component (0-255) of the toast border color
        /// </summary>
        public int ToastBorderBlue
        {
            get
            {
                if (settings == null || string.IsNullOrEmpty(settings.ToastBorderColor))
                    return 42; // 0x2A
                try
                {
                    var hex = settings.ToastBorderColor.TrimStart('#');
                    if (hex.Length >= 6)
                        return Convert.ToInt32(hex.Substring(4, 2), 16);
                }
                catch { }
                return 42;
            }
            set
            {
                var r = ToastBorderRed;
                var g = ToastBorderGreen;
                var b = Math.Max(0, Math.Min(255, value));
                if (settings != null)
                {
                    settings.ToastBorderColor = $"{r:X2}{g:X2}{b:X2}";
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Command to adjust the brightness of the border color.
        /// Positive values lighten, negative values darken.
        /// </summary>
        public ICommand AdjustBorderBrightnessCommand => new Common.RelayCommand<string>((deltaStr) =>
        {
            if (settings == null || string.IsNullOrEmpty(deltaStr)) return;

            if (int.TryParse(deltaStr, out int delta))
            {
                // Get current RGB values
                int r = ToastBorderRed;
                int g = ToastBorderGreen;
                int b = ToastBorderBlue;

                // Adjust each channel by delta, clamping to 0-255
                r = Math.Max(0, Math.Min(255, r + delta));
                g = Math.Max(0, Math.Min(255, g + delta));
                b = Math.Max(0, Math.Min(255, b + delta));

                // Update the color
                settings.ToastBorderColor = $"{r:X2}{g:X2}{b:X2}";

                // Notify all RGB properties changed
                OnPropertyChanged(nameof(ToastBorderRed));
                OnPropertyChanged(nameof(ToastBorderGreen));
                OnPropertyChanged(nameof(ToastBorderBlue));
            }
        });

        #endregion

        private void UpdateCacheStats()
        {
            try
            {
                var cacheService = plugin.GetSearchCacheService();
                if (cacheService == null)
                {
                    CacheStatsText = "Cache: Not available";
                    return;
                }

                var stats = cacheService.GetCacheStats();
                var sizeMB = stats.SizeBytes / 1024.0 / 1024.0;
                CacheStatsText = $"Cache: {stats.GameCount} games, {stats.EntryCount} entries, {sizeMB:F2} MB";
            }
            catch (Exception ex)
            {
                CacheStatsText = $"Cache: Error loading stats";
                LogManager.GetLogger().Error(ex, "Error updating cache stats");
            }
        }

        public UniPlaySongSettings Settings
        {
            get => settings;
            set
            {
                // Unsubscribe from old settings
                if (settings != null)
                {
                    settings.PropertyChanged -= OnSettingsPropertyChanged;
                }
                
                // Subscribe to new settings
                if (value != null)
                {
                    value.PropertyChanged += OnSettingsPropertyChanged;
                }
                
                settings = value;
                OnPropertyChanged();
            }
        }

        public void BeginEdit()
        {
            // Called when settings view is opened
            UpdateCacheStats();
            UpdateHintsDatabaseStatus();
            RefreshMigrationStatus();
        }

        public void CancelEdit()
        {
            // Called when user cancels editing
            var savedSettings = plugin.LoadPluginSettings<UniPlaySongSettings>();
            if (savedSettings != null)
            {
                settings = savedSettings;
                // DefaultMusicPath is NOT modified - it remains separate and untouched
            }
        }

        public void EndEdit()
        {
            // Called when user finishes editing
            var errorHandler = plugin.GetErrorHandlerService();
            
            errorHandler?.Try(
                () =>
                {
                    // Validate settings before saving
                    if (!VerifySettings(out var errors))
                    {
                        var errorMessage = "Settings validation failed:\n\n" + string.Join("\n", errors);
                        throw new InvalidOperationException(errorMessage);
                    }

                    // Validate native music file if UseNativeMusicAsDefault is enabled before saving
                    // DefaultMusicPath is NOT modified - it remains separate
                    if (settings?.UseNativeMusicAsDefault == true && settings?.EnableDefaultMusic == true)
                    {
                        var settingsService = plugin.GetSettingsService();
                        settingsService?.ValidateNativeMusicFile(settings, showErrors: true);
                    }
                    
                    plugin.SavePluginSettings(settings);

                    // Sync toast settings to DialogHelper
                    DialogHelper.SyncToastSettings(settings);

                    // Notify plugin that settings were saved so it can reload them
                    plugin.OnSettingsSaved();
                },
                context: "saving settings",
                showUserMessage: true
            );
        }

        public bool VerifySettings(out List<string> errors)
        {
            var settingsService = plugin.GetSettingsService();
            if (settingsService != null)
            {
                errors = settingsService.ValidateSettings(settings);
                return errors.Count == 0;
            }
            
            // Fallback validation if SettingsService is not available
            errors = new List<string>();
            if (settings == null)
            {
                errors.Add("Settings cannot be null");
                return false;
            }
            return true;
        }
    }
}

