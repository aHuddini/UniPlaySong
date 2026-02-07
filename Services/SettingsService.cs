using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Centralized settings management: single source of truth with automatic propagation via events
    public class SettingsService
    {
        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly FileLogger _fileLogger;
        private readonly UniPlaySong _plugin;
        private UniPlaySongSettings _currentSettings;

        // Fired when settings are updated. Services subscribe for automatic updates.
        public event EventHandler<SettingsChangedEventArgs> SettingsChanged;

        // Fired when a specific setting property changes
        public event EventHandler<PropertyChangedEventArgs> SettingPropertyChanged;

        public UniPlaySongSettings Current => _currentSettings; // Always up-to-date; use instead of passing settings as params

        public SettingsService(IPlayniteAPI api, ILogger logger, FileLogger fileLogger, UniPlaySong plugin)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileLogger = fileLogger;
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            
            // Load initial settings
            LoadSettings();
        }

        public void LoadSettings()
        {
            try
            {
                var newSettings = _plugin.LoadPluginSettings<UniPlaySongSettings>();
                if (newSettings != null)
                {
                    UpdateSettings(newSettings, source: "LoadSettings");
                }
                else
                {
                    // Create default settings if none exist
                    var defaultSettings = new UniPlaySongSettings();
                    UpdateSettings(defaultSettings, source: "LoadSettings (default)");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading settings");
                _fileLogger?.Error($"Error loading settings: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Updates settings and notifies all subscribers
        /// This is the ONLY place settings are updated - ensures consistency
        /// </summary>
        /// <param name="newSettings">New settings to apply</param>
        /// <param name="source">Source of the update (for logging)</param>
        public void UpdateSettings(UniPlaySongSettings newSettings, string source = "Unknown")
        {
            if (newSettings == null)
            {
                _logger.Warn("UpdateSettings called with null settings");
                return;
            }

            // NOTE: UseNativeMusicAsDefault does NOT modify DefaultMusicPath
            // DefaultMusicPath remains separate and untouched - playback service handles native path directly

            // Unsubscribe from old settings
            if (_currentSettings != null)
            {
                _currentSettings.PropertyChanged -= OnSettingPropertyChanged;
            }

            // Store old settings for comparison
            var oldSettings = _currentSettings;
            _currentSettings = newSettings;

            // Subscribe to new settings property changes
            _currentSettings.PropertyChanged += OnSettingPropertyChanged;

            // Notify all subscribers that settings changed
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(oldSettings, newSettings, source));
            
            _logger.Debug($"SettingsService: Settings updated (source: {source})");
            _fileLogger?.Info($"SettingsService: Settings updated from {source}");
        }

        public List<string> ValidateSettings(UniPlaySongSettings settings)
        {
            var errors = new List<string>();

            if (settings == null)
            {
                errors.Add("Settings cannot be null");
                return errors;
            }

            // Volume validation
            if (settings.MusicVolume < Constants.MinMusicVolume || 
                settings.MusicVolume > Constants.MaxMusicVolume)
            {
                errors.Add($"Music volume must be between {Constants.MinMusicVolume} and {Constants.MaxMusicVolume}");
            }

            // Fade duration validation
            if (settings.FadeInDuration < Constants.MinFadeDuration || 
                settings.FadeInDuration > Constants.MaxFadeDuration)
            {
                errors.Add($"Fade-in duration must be between {Constants.MinFadeDuration} and {Constants.MaxFadeDuration} seconds");
            }

            if (settings.FadeOutDuration < Constants.MinFadeDuration || 
                settings.FadeOutDuration > Constants.MaxFadeDuration)
            {
                errors.Add($"Fade-out duration must be between {Constants.MinFadeDuration} and {Constants.MaxFadeDuration} seconds");
            }

            // File path validation
            if (!string.IsNullOrEmpty(settings.YtDlpPath) && !File.Exists(settings.YtDlpPath))
            {
                errors.Add($"yt-dlp path does not exist: {settings.YtDlpPath}");
            }

            if (!string.IsNullOrEmpty(settings.FFmpegPath) && !File.Exists(settings.FFmpegPath))
            {
                errors.Add($"ffmpeg path does not exist: {settings.FFmpegPath}");
            }

            if (settings.EnableDefaultMusic && 
                !string.IsNullOrEmpty(settings.DefaultMusicPath) && 
                !File.Exists(settings.DefaultMusicPath))
            {
                errors.Add($"Default music file does not exist: {settings.DefaultMusicPath}");
            }

            return errors;
        }

        /// <summary>
        /// Validates that native music file exists if UseNativeMusicAsDefault is enabled
        /// Does NOT modify DefaultMusicPath - it remains separate
        /// </summary>
        /// <param name="settings">Settings object to validate</param>
        /// <param name="showErrors">If true, shows error dialogs to user if native music file not found</param>
        /// <returns>True if native music file exists (or feature is disabled), false if enabled but file not found</returns>
        public bool ValidateNativeMusicFile(UniPlaySongSettings settings, bool showErrors = false)
        {
            // Only validate if UseNativeMusicAsDefault is enabled
            if (settings?.UseNativeMusicAsDefault != true)
            {
                return true; // Not applicable, return success
            }

            try
            {
                var nativeMusicPath = PlayniteThemeHelper.FindBackgroundMusicFile(_api);
                
                if (!string.IsNullOrEmpty(nativeMusicPath) && File.Exists(nativeMusicPath))
                {
                    _fileLogger?.Debug($"SettingsService: Native music file validated: {nativeMusicPath}");
                    return true;
                }
                else
                {
                    _fileLogger?.Warn("SettingsService: UseNativeMusicAsDefault enabled but native music file not found");
                    
                    if (showErrors)
                    {
                        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        var expectedAudioDir = Path.Combine(localAppData, "Playnite", "Themes", "Fullscreen", "Default", "audio");
                        var expectedPath = Path.Combine(expectedAudioDir, "background.*");
                        
                        var errorMsg = "Could not find Playnite background music file.\n\n" +
                                      "Expected location: " + expectedPath + "\n\n" +
                                      "Please ensure:\n" +
                                      "1. You're using a theme with background music\n" +
                                      "2. The file exists at: " + expectedAudioDir + "\n" +
                                      "3. Or use a custom default music file instead.";
                        
                        settings.UseNativeMusicAsDefault = false;
                        _api.Dialogs.ShowMessage(errorMsg, "Native Music Not Found");
                    }
                    else
                    {
                        settings.UseNativeMusicAsDefault = false;
                    }
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error validating native music file");
                _fileLogger?.Error($"SettingsService: Error validating native music file: {ex.Message}", ex);
                
                if (showErrors)
                {
                    var errorMsg = $"Error detecting native music file: {ex.Message}\n\n" +
                                  $"Please check the extension logs for more details.";
                    settings.UseNativeMusicAsDefault = false;
                    _api.Dialogs.ShowMessage(errorMsg, "Error");
                }
                else
                {
                    settings.UseNativeMusicAsDefault = false;
                }
                
                return false;
            }
        }

        private void OnSettingPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Validate native music file when UseNativeMusicAsDefault is enabled
            if (e.PropertyName == nameof(UniPlaySongSettings.UseNativeMusicAsDefault) && _currentSettings != null)
            {
                if (_currentSettings.UseNativeMusicAsDefault == true)
                {
                    // Validate that native music file exists (but don't modify DefaultMusicPath)
                    ValidateNativeMusicFile(_currentSettings, showErrors: false);
                }
            }
            
            SettingPropertyChanged?.Invoke(this, e);
        }
    }

    // Event args for settings changed event
    public class SettingsChangedEventArgs : EventArgs
    {
        public UniPlaySongSettings OldSettings { get; }
        public UniPlaySongSettings NewSettings { get; }
        public string Source { get; }

        public SettingsChangedEventArgs(UniPlaySongSettings oldSettings, UniPlaySongSettings newSettings, string source)
        {
            OldSettings = oldSettings;
            NewSettings = newSettings;
            Source = source;
        }
    }
}

