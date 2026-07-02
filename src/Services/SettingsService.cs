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

        // True only when settings were genuinely read from disk (or no config exists — a real
        // first run). False when a config file EXISTS but failed to load: the in-memory defaults
        // are then a stand-in and must NEVER be auto-saved over the user's file. Automatic saves
        // (OnLibraryUpdated timestamps, startup migrations) check this; user-initiated saves
        // (settings dialog, imports) do not — an explicit save is user intent.
        // Root cause this guards against: a transient load failure on a .pext update-restart put
        // defaults in memory, and the OnLibraryUpdated timestamp save persisted them ~6s later —
        // silently wiping the user's settings (seen as "radio mode resets after updating").
        public bool SettingsLoadedFromDisk { get; private set; }

        public void LoadSettings()
        {
            try
            {
                var newSettings = _plugin.LoadPluginSettings<UniPlaySongSettings>();
                if (newSettings == null)
                {
                    // Retry once: on a .pext update-restart the config can be transiently unreadable.
                    System.Threading.Thread.Sleep(250);
                    newSettings = _plugin.LoadPluginSettings<UniPlaySongSettings>();
                }
                if (newSettings != null)
                {
                    MigrateSettings(newSettings);
                    SettingsLoadedFromDisk = true;
                    UpdateSettings(newSettings, source: "LoadSettings");
                }
                else
                {
                    var configPath = System.IO.Path.Combine(_plugin.GetPluginUserDataPath(), "config.json");
                    bool configExists = System.IO.File.Exists(configPath);
                    SettingsLoadedFromDisk = !configExists; // missing file = true first run, defaults are real
                    if (configExists)
                        _fileLogger?.Error("Settings file exists but failed to load — running on in-memory defaults; automatic saves disabled this session to protect the file.");
                    UpdateSettings(new UniPlaySongSettings(), source: "LoadSettings (default)");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading settings");
                _fileLogger?.Error($"Error loading settings: {ex.Message}", ex);
                SettingsLoadedFromDisk = false; // whatever is in memory, don't let it overwrite disk
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

        // v1.5.0: UseNativeMusicAsDefault deprecated alongside the NativeTheme source.
        // v1.5.2: validator no longer probes background.{ext} — that file belongs to
        // Playnite's SDL player. If the legacy flag is somehow still set after migration,
        // we silently clear it so the next save persists the cleanup.
        public bool ValidateNativeMusicFile(UniPlaySongSettings settings, bool showErrors = false)
        {
            if (settings?.UseNativeMusicAsDefault == true)
            {
                settings.UseNativeMusicAsDefault = false;
                _fileLogger?.Info("SettingsService: cleared legacy UseNativeMusicAsDefault (deprecated v1.5.0).");
            }
            return true;
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

        // One-time migrations for existing users when new defaults are added.
        // Each migration is idempotent — safe to run on every load.
        // TODO: Remove migration code after v1.3.9 — by then all active users will have the updated defaults.
        private void MigrateSettings(UniPlaySongSettings settings)
        {
            bool changed = false;

            // v1.3.8: Add Wallpaper Engine to default excluded apps
            var requiredExclusions = new[] { "wallpaper64", "wallpaper32", "webwallpaper32" };
            var currentExclusions = (settings.ExternalAudioExcludedApps ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();
            var currentLower = new HashSet<string>(currentExclusions.Select(s => s.ToLowerInvariant()));

            foreach (var exclusion in requiredExclusions)
            {
                if (!currentLower.Contains(exclusion))
                {
                    currentExclusions.Add(exclusion);
                    changed = true;
                }
            }

            if (changed)
            {
                settings.ExternalAudioExcludedApps = string.Join(", ", currentExclusions);
                _plugin.SavePluginSettings(settings);
                _fileLogger?.Info($"Settings migration: Added Wallpaper Engine to excluded apps: {settings.ExternalAudioExcludedApps}");
            }
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

