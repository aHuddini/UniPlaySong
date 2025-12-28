using Playnite.SDK;
using System;
using System.Collections.Generic;
using UniPlaySong.Common;

namespace UniPlaySong
{
    public class UniPlaySongSettings : ObservableObject
    {
        private bool enableMusic = true;
        private AudioState musicState = AudioState.Always;
        private bool skipFirstSelectionAfterModeSwitch = false;
        private bool themeCompatibleSilentSkip = false;
        private bool pauseOnTrailer = true;
        private int musicVolume = Constants.DefaultMusicVolume;
        private double fadeInDuration = Constants.DefaultFadeInDuration;
        private double fadeOutDuration = Constants.DefaultFadeOutDuration;
        private string ytDlpPath = string.Empty;
        private string ffmpegPath = string.Empty;
        private bool videoIsPlaying = false;
        private bool enablePreviewMode = false;
        private int previewDuration = Constants.DefaultPreviewDuration;
        private bool enableDebugLogging = false;
        private bool pauseOnFocusLoss = false;
        private bool pauseOnMinimize = false;

        public bool EnableMusic
        {
            get => enableMusic;
            set { enableMusic = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// When to play music (Never, Desktop, Fullscreen, Always)
        /// </summary>
        public AudioState MusicState
        {
            get => musicState;
            set { musicState = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Skip first game selection after switching to fullscreen mode
        /// </summary>
        public bool SkipFirstSelectionAfterModeSwitch
        {
            get => skipFirstSelectionAfterModeSwitch;
            set 
            { 
                skipFirstSelectionAfterModeSwitch = value; 
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsThemeCompatibleSilentSkipEnabled)); // Update enabled state
                // If enabling SkipFirstSelectionAfterModeSwitch, disable ThemeCompatibleSilentSkip
                if (value && themeCompatibleSilentSkip)
                {
                    themeCompatibleSilentSkip = false;
                    OnPropertyChanged(nameof(ThemeCompatibleSilentSkip));
                }
            }
        }

        /// <summary>
        /// Theme Compatible Login Skip: Waits for keyboard/controller input (Enter/Space/Escape) before playing music.
        /// Designed for themes with login/welcome screens. Music starts when user presses a key to dismiss the login screen.
        /// Note: Disabled when "Do not play music on startup" is enabled.
        /// </summary>
        public bool ThemeCompatibleSilentSkip
        {
            get => themeCompatibleSilentSkip;
            set 
            { 
                themeCompatibleSilentSkip = value; 
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsThemeCompatibleSilentSkipEnabled)); // Update enabled state
            }
        }

        /// <summary>
        /// Gets whether ThemeCompatibleSilentSkip checkbox should be enabled in UI.
        /// Disabled when SkipFirstSelectionAfterModeSwitch is enabled.
        /// </summary>
        public bool IsThemeCompatibleSilentSkipEnabled
        {
            get => !skipFirstSelectionAfterModeSwitch;
        }

        /// <summary>
        /// Pause music when trailers/videos are playing
        /// </summary>
        public bool PauseOnTrailer
        {
            get => pauseOnTrailer;
            set { pauseOnTrailer = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Music volume (0-100)
        /// </summary>
        public int MusicVolume
        {
            get => musicVolume;
            set { musicVolume = Math.Max(Constants.MinMusicVolume, Math.Min(Constants.MaxMusicVolume, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Fade-in duration in seconds (0.05 - 10.0)
        /// Controls how long music takes to fade in when starting or resuming
        /// </summary>
        public double FadeInDuration
        {
            get => fadeInDuration;
            set { fadeInDuration = Math.Round(Math.Max(Constants.MinFadeDuration, Math.Min(Constants.MaxFadeDuration, value)), 2); OnPropertyChanged(); }
        }

        /// <summary>
        /// Fade-out duration in seconds (0.05 - 10.0)
        /// Controls how long music takes to fade out when switching or pausing
        /// </summary>
        public double FadeOutDuration
        {
            get => fadeOutDuration;
            set { fadeOutDuration = Math.Round(Math.Max(Constants.MinFadeDuration, Math.Min(Constants.MaxFadeDuration, value)), 2); OnPropertyChanged(); }
        }

        /// <summary>
        /// Path to yt-dlp executable (for YouTube downloads)
        /// </summary>
        public string YtDlpPath
        {
            get => ytDlpPath;
            set { ytDlpPath = value ?? string.Empty; OnPropertyChanged(); }
        }

        /// <summary>
        /// Path to ffmpeg executable (for YouTube downloads)
        /// </summary>
        public string FFmpegPath
        {
            get => ffmpegPath;
            set { ffmpegPath = value ?? string.Empty; OnPropertyChanged(); }
        }

        private bool useFirefoxCookies = false;

        /// <summary>
        /// Use cookies from Firefox browser for YouTube downloads
        /// When enabled, uses simplified yt-dlp command with --cookies-from-browser firefox
        /// </summary>
        public bool UseFirefoxCookies
        {
            get => useFirefoxCookies;
            set { useFirefoxCookies = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Tracks if a video is currently playing
        /// </summary>
        public bool VideoIsPlaying
        {
            get => videoIsPlaying;
            set { videoIsPlaying = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Enable preview mode for game-specific music
        /// When enabled, songs will restart after the specified preview duration instead of playing continuously
        /// </summary>
        public bool EnablePreviewMode
        {
            get => enablePreviewMode;
            set { enablePreviewMode = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Preview duration in seconds (15-300 seconds)
        /// Controls how long each game music track plays before restarting
        /// Only applies when EnablePreviewMode is true and does not affect default music
        /// </summary>
        public int PreviewDuration
        {
            get => previewDuration;
            set { previewDuration = Math.Max(Constants.MinPreviewDuration, Math.Min(Constants.MaxPreviewDuration, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Enable verbose debug logging to file.
        /// When disabled, only errors and important events are logged.
        /// Enable this for troubleshooting issues with the extension.
        /// </summary>
        public bool EnableDebugLogging
        {
            get => enableDebugLogging;
            set { enableDebugLogging = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Pause music when Playnite loses focus (switching to another application).
        /// Music will resume when Playnite regains focus.
        /// </summary>
        public bool PauseOnFocusLoss
        {
            get => pauseOnFocusLoss;
            set { pauseOnFocusLoss = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Pause music when Playnite is minimized to taskbar or system tray.
        /// Music will resume when Playnite is restored.
        /// </summary>
        public bool PauseOnMinimize
        {
            get => pauseOnMinimize;
            set { pauseOnMinimize = value; OnPropertyChanged(); }
        }

        // Default Music Support
        private bool enableDefaultMusic = false;
        private string defaultMusicPath = string.Empty;
        private string backupCustomMusicPath = string.Empty; // Backup of custom path when using native as default
        private bool suppressPlayniteBackgroundMusic = true;
        private bool useNativeMusicAsDefault = false;

        /// <summary>
        /// Enable default music fallback when no game music is found
        /// </summary>
        public bool EnableDefaultMusic
        {
            get => enableDefaultMusic;
            set 
            { 
                enableDefaultMusic = value; 
                OnPropertyChanged();
                // Notify that UseNativeMusicAsDefault enabled state may have changed
                OnPropertyChanged(nameof(IsUseNativeMusicAsDefaultEnabled));
            }
        }

        /// <summary>
        /// Path to default music file (MP3, WAV, FLAC, etc.)
        /// Only used when UseNativeMusicAsDefault is false
        /// </summary>
        public string DefaultMusicPath
        {
            get => defaultMusicPath;
            set { defaultMusicPath = value ?? string.Empty; OnPropertyChanged(); }
        }

        /// <summary>
        /// Suppress Playnite's native background music in fullscreen mode
        /// When unchecked (false), native music will play when no game music and no default music is available
        /// Mutually exclusive with UseNativeMusicAsDefault
        /// </summary>
        public bool SuppressPlayniteBackgroundMusic
        {
            get => suppressPlayniteBackgroundMusic;
            set 
            { 
                suppressPlayniteBackgroundMusic = value;
                OnPropertyChanged();
                // If suppression is enabled, disable "use native as default" (mutually exclusive)
                if (value && useNativeMusicAsDefault)
                {
                    useNativeMusicAsDefault = false;
                    OnPropertyChanged(nameof(UseNativeMusicAsDefault));
                }
                OnPropertyChanged(nameof(IsUseNativeMusicAsDefaultEnabled));
            }
        }

        /// <summary>
        /// Use Playnite's native "Default" theme music instead of custom default music file
        /// Only applies when EnableDefaultMusic is true
        /// When enabled: Uses Playnite's vanilla default theme background music, prevents use of custom default music
        /// When disabled: Custom DefaultMusicPath file is used (native music is suppressed)
        /// Mutually exclusive with SuppressPlayniteBackgroundMusic
        /// </summary>
        public bool UseNativeMusicAsDefault
        {
            get => useNativeMusicAsDefault;
            set 
            { 
                useNativeMusicAsDefault = value;
                OnPropertyChanged();
                // If using native as default, disable suppression (mutually exclusive)
                if (value && suppressPlayniteBackgroundMusic)
                {
                    suppressPlayniteBackgroundMusic = false;
                    OnPropertyChanged(nameof(SuppressPlayniteBackgroundMusic));
                }
                OnPropertyChanged(nameof(IsSuppressNativeMusicEnabled));
            }
        }

        /// <summary>
        /// Backup of custom default music path when UseNativeMusicAsDefault is enabled
        /// Used to restore the custom path when UseNativeMusicAsDefault is disabled
        /// </summary>
        public string BackupCustomMusicPath
        {
            get => backupCustomMusicPath;
            set { backupCustomMusicPath = value ?? string.Empty; OnPropertyChanged(); }
        }

        /// <summary>
        /// Determines if "Suppress Native Music" checkbox should be enabled
        /// Disabled when "Use Native Music as Default" is enabled (mutually exclusive)
        /// </summary>
        public bool IsSuppressNativeMusicEnabled => !useNativeMusicAsDefault;

        /// <summary>
        /// Determines if "Use Playnite Native 'Default' Theme Music" checkbox should be enabled
        /// Disabled when "Suppress Native Music" is enabled (mutually exclusive)
        /// Also requires EnableDefaultMusic to be true
        /// </summary>
        public bool IsUseNativeMusicAsDefaultEnabled => enableDefaultMusic && !suppressPlayniteBackgroundMusic;

        // Search Cache Settings
        private bool enableSearchCache = true;
        private int searchCacheDurationDays = 7;

        /// <summary>
        /// Enable search result caching to optimize KHInsider → YouTube fallback
        /// When enabled, search results are cached for the specified duration
        /// </summary>
        public bool EnableSearchCache
        {
            get => enableSearchCache;
            set { enableSearchCache = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Search cache duration in days (1-30)
        /// Controls how long search results are cached before expiring
        /// </summary>
        public int SearchCacheDurationDays
        {
            get => searchCacheDurationDays;
            set { searchCacheDurationDays = Math.Max(1, Math.Min(30, value)); OnPropertyChanged(); }
        }

        // Audio Normalization Settings
        private double normalizationTargetLoudness = -16.0;
        private double normalizationTruePeak = -1.5;
        private double normalizationLoudnessRange = 11.0;
        private string normalizationCodec = "libmp3lame";
        private string normalizationSuffix = "-normalized";
        private bool skipAlreadyNormalized = true;
        private bool doNotPreserveOriginals = false;

        // Audio Trimming Settings
        private string trimSuffix = "-trimmed";

        /// <summary>
        /// Enable audio normalization for consistent volume levels
        /// When enabled, audio files can be normalized to EBU R128 standard
        /// </summary>

        /// <summary>
        /// Target loudness in LUFS (EBU R128 standard is -16 LUFS)
        /// Range: -30 to -10 LUFS
        /// </summary>
        public double NormalizationTargetLoudness
        {
            get => normalizationTargetLoudness;
            set { normalizationTargetLoudness = Math.Max(-30.0, Math.Min(-10.0, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// True peak limit in dBTP (EBU R128 standard is -1.0 to -1.5 dBTP)
        /// Range: -3.0 to 0.0 dBTP
        /// </summary>
        public double NormalizationTruePeak
        {
            get => normalizationTruePeak;
            set { normalizationTruePeak = Math.Max(-3.0, Math.Min(0.0, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Loudness range in LU (EBU R128 standard is 7-18 LU, default 11)
        /// Range: 1 to 20 LU
        /// </summary>
        public double NormalizationLoudnessRange
        {
            get => normalizationLoudnessRange;
            set { normalizationLoudnessRange = Math.Max(1.0, Math.Min(20.0, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Audio codec to use for normalization output
        /// Default: libmp3lame (maintains MP3 format)
        /// </summary>
        public string NormalizationCodec
        {
            get => normalizationCodec;
            set { normalizationCodec = value ?? "libmp3lame"; OnPropertyChanged(); }
        }

        /// <summary>
        /// Suffix to append to normalized file names (e.g., "-normalized")
        /// Normalized files are always created with this suffix, preserving original files
        /// </summary>
        public string NormalizationSuffix
        {
            get => normalizationSuffix;
            set { normalizationSuffix = value ?? "-normalized"; OnPropertyChanged(); }
        }

        /// <summary>
        /// Skip files that are already normalized
        /// When enabled, files with the normalization suffix are skipped during bulk operations
        /// </summary>
        public bool SkipAlreadyNormalized
        {
            get => skipAlreadyNormalized;
            set { skipAlreadyNormalized = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Do not preserve original files (space saver mode)
        /// When enabled, original files are normalized directly and replaced (no backup/preservation)
        /// When disabled, original files are moved to PreservedOriginals folder
        /// </summary>
        public bool DoNotPreserveOriginals
        {
            get => doNotPreserveOriginals;
            set { doNotPreserveOriginals = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Suffix to append to trimmed file names (e.g., "-trimmed")
        /// Trimmed files are created with this suffix when preserving originals
        /// </summary>
        public string TrimSuffix
        {
            get => trimSuffix;
            set { trimSuffix = value ?? "-trimmed"; OnPropertyChanged(); }
        }

        // Song Randomization Settings
        private bool randomizeOnEverySelect = false;
        private bool randomizeOnMusicEnd = true;

        /// <summary>
        /// Randomize song selection when selecting a different game
        /// When enabled, a random song will be selected each time you select a game (after primary song plays)
        /// </summary>
        public bool RandomizeOnEverySelect
        {
            get => randomizeOnEverySelect;
            set { randomizeOnEverySelect = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Randomize song selection when current song ends
        /// When enabled, after the current song finishes, a new random song will be selected from available songs
        /// </summary>
        public bool RandomizeOnMusicEnd
        {
            get => randomizeOnMusicEnd;
            set { randomizeOnMusicEnd = value; OnPropertyChanged(); }
        }

        // YouTube Channel Whitelist Settings
        private bool enableYouTubeChannelWhitelist = true;
        private List<string> whitelistedYouTubeChannelIds = new List<string>
        {
            // GilvaSunner - Well-known for high-quality game music rips
            "UCfSN UCFt4IRa-lKRYUhvEg",
            // BrawlBRSTMs3 - Extensive library of video game music
            "UC9l8PCqbv1x7qwU_1oiSR3A",
            // GiIvaSunner (with uppercase i) - Another variant
            "UC9ecwl3FTG66jIKA9JRDtmg",
            // OST Composure - Video game soundtrack uploads
            "UCWD1McJJZnMjJwBh2JY-xfg",
            // Add more reliable channels here as you discover them
        };

        /// <summary>
        /// Enable YouTube channel whitelist for auto-download mode
        /// When enabled, only playlists from whitelisted channels will be considered for auto-selection
        /// Manual mode is not affected by this setting
        /// </summary>
        public bool EnableYouTubeChannelWhitelist
        {
            get => enableYouTubeChannelWhitelist;
            set { enableYouTubeChannelWhitelist = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// List of whitelisted YouTube channel IDs
        /// These channels are known to provide reliable game soundtrack uploads
        /// </summary>
        public List<string> WhitelistedYouTubeChannelIds
        {
            get => whitelistedYouTubeChannelIds;
            set { whitelistedYouTubeChannelIds = value ?? new List<string>(); OnPropertyChanged(); }
        }
    }
}
