using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
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

