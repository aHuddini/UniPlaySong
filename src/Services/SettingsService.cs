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
    public class SettingsService : ISettingsProvider
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

        // True only when settings were genuinely read from disk (or no config exists — a real first run). False when
        // a config file EXISTS but failed to load: the in-memory defaults are then a stand-in and must NEVER be
        // auto-saved over the user's file. Automatic saves (OnLibraryUpdated timestamps, startup migrations) check
        // this; user-initiated saves (settings dialog, imports) do not — an explicit save is user intent. A transient
        // load failure on a .pext update-restart would otherwise let the timestamp save wipe the user's settings.
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

        // The ONLY place settings are replaced; notifies all subscribers. source is for logging.
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

            // Carry forward runtime-only ([JsonIgnore]) state BEFORE the swap. A settings SAVE builds a fresh object and
            // these fields aren't serialized, so without this every save reset them: it blanked the now-playing display AND
            // reset the overlay/video gate flags (ThemeOverlayActive / VideoIsPlaying) that the Spotify effected-output
            // gate reads live in TargetExternalVolume(). An overlay-heavy theme would then re-strand one of them true on
            // the new object and mute the effected Spotify output until a Playnite restart. Copied while newSettings has no
            // PropertyChanged subscribers yet, so it fires no spurious notifications.
            if (oldSettings != null)
            {
                foreach (var prop in typeof(UniPlaySongSettings).GetProperties())
                {
                    if (!prop.CanRead || !prop.CanWrite) continue;
                    bool isJsonIgnore = false;
                    foreach (var attr in prop.GetCustomAttributes(true))
                        if (attr.GetType().Name == "JsonIgnoreAttribute") { isJsonIgnore = true; break; }
                    if (isJsonIgnore)
                    {
                        try { prop.SetValue(newSettings, prop.GetValue(oldSettings)); } catch { }
                    }
                }
            }

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

        // UseNativeMusicAsDefault is deprecated along with the NativeTheme source, and background.{ext} belongs to
        // Playnite's SDL player, so nothing is probed. A legacy flag still set is cleared so the next save persists it.
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
        private void MigrateSettings(UniPlaySongSettings settings)
        {
            bool changed = false;

            // System beep is not a sound choice: map it to the bundled jingle, or the sound silently stops and the UI
            // cannot show the stale value. The enum member stays as a numbering placeholder (see CelebrationSoundType)
            // so stored integers do not shift underneath existing configs.
            if (settings.CelebrationSoundType == CelebrationSoundType.SystemBeep_Removed)
            {
                settings.CelebrationSoundType = CelebrationSoundType.BundledJingle;
                changed = true;
            }

            if (settings.AbandonedSoundType == CelebrationSoundType.SystemBeep_Removed)
            {
                settings.AbandonedSoundType = CelebrationSoundType.BundledJingle;
                changed = true;
            }

            if (settings.AchievementSoundType == CelebrationSoundType.SystemBeep_Removed)
            {
                settings.AchievementSoundType = CelebrationSoundType.BundledJingle;
                changed = true;
            }

            if (settings.ControlUpDetectSoundType == CelebrationSoundType.SystemBeep_Removed)
            {
                settings.ControlUpDetectSoundType = CelebrationSoundType.BundledJingle;
                changed = true;
            }

            // Default excluded apps: Wallpaper Engine; Sunshine, whose audio-capture session mirrors system output so
            // UPS's own music reads back as "external audio" and pause-oscillates; SignalRGB, Rainmeter and YASB.
            var requiredExclusions = new[] { "wallpaper64", "wallpaper32", "webwallpaper32", "sunshine", "sunshinesvc", "signalrgb", "rainmeter", "yasb" };
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

            // A master SelectedAchievementJingle that isn't an achievement-pack file (e.g. a celebration-pack file) leaves
            // the ComboBox blank, so reset it to the bundled default. AchievementSoundPack needs no migration: its backing
            // field defaults to PAStarterPack. Idempotent.
            var achievementFiles = new HashSet<string>(
                BundledJingleService.GetAchievementJingles().Select(j => j.File),
                StringComparer.OrdinalIgnoreCase);
            if (achievementFiles.Count > 0)
            {
                const string def = BundledJingleService.DefaultAchievementJingle;
                var cur = settings.SelectedAchievementJingle;
                if (!string.IsNullOrEmpty(cur) && !achievementFiles.Contains(cur))
                {
                    settings.SelectedAchievementJingle = achievementFiles.Contains(def) ? def : cur;
                    changed = true;
                }
            }

            if (changed)
            {
                settings.ExternalAudioExcludedApps = string.Join(", ", currentExclusions);
                _plugin.SavePluginSettings(settings);
                _fileLogger?.Info($"Settings migration: excluded apps are now: {settings.ExternalAudioExcludedApps}");
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

