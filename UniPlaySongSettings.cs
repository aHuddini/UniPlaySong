using Playnite.SDK;
using System;
using System.Collections.Generic;
using UniPlaySong.Common;

namespace UniPlaySong
{
    /// <summary>
    /// Reverb effect presets based on Audacity's factory presets.
    /// Each preset configures Room Size, Pre-delay, Reverberance, HF Damping,
    /// Tone Low, Tone High, Wet Gain, Dry Gain, and Stereo Width.
    /// </summary>
    public enum ReverbPreset
    {
        Custom = 0,
        // General purpose (Audacity)
        Acoustic,
        Ambience,
        Artificial,
        Clean,
        Modern,
        // Vocals (Audacity)
        VocalI,
        VocalII,
        DanceVocal,
        ModernVocal,
        VoiceTail,
        // Room sizes (Audacity)
        Bathroom,
        SmallRoomBright,
        SmallRoomDark,
        MediumRoom,
        LargeRoom,
        ChurchHall,
        Cathedral,
        BigCave,
        // UniPlaySong custom presets - Living/Entertainment environments
        LivingRoom,         // Cozy living room TV viewing
        HomeTheater,        // Home theater movie night
        LateNightTV,        // Subtle late night ambience
        LoungeCafe,         // Relaxed cafe/lounge atmosphere
        JazzClub,           // Intimate jazz club
        NightClub,          // Club/dance floor energy
        ConcertHall         // Live concert experience
    }

    /// <summary>
    /// Preset effect chain orderings.
    /// Each preset defines a fixed, safe order for applying effects.
    /// </summary>
    public enum EffectChainPreset
    {
        /// <summary>Standard: High-Pass → Low-Pass → Reverb (Recommended)</summary>
        Standard = 0,
        /// <summary>Filters after reverb: Reverb → High-Pass → Low-Pass</summary>
        ReverbFirst = 1,
        /// <summary>Low-Pass → High-Pass → Reverb</summary>
        LowPassFirst = 2,
        /// <summary>Low-Pass → Reverb → High-Pass</summary>
        LowPassThenReverb = 3,
        /// <summary>High-Pass → Reverb → Low-Pass</summary>
        HighPassThenReverb = 4,
        /// <summary>Reverb → Low-Pass → High-Pass</summary>
        ReverbThenLowPass = 5
    }

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
        private bool themeOverlayActive = false;
        private bool enablePreviewMode = false;
        private int previewDuration = Constants.DefaultPreviewDuration;
        private bool enableDebugLogging = false;
        private bool pauseOnFocusLoss = false;
        private bool pauseOnMinimize = true;
        private bool pauseWhenInSystemTray = true;
        private bool showNowPlayingInTopPanel = false;
        private bool showDesktopMediaControls = false;

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
        /// Tracks if a video is currently playing (set by MediaElementsMonitor)
        /// </summary>
        public bool VideoIsPlaying
        {
            get => videoIsPlaying;
            set { videoIsPlaying = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Tracks if a theme overlay is active (set by MusicControl from theme Tag bindings).
        /// This is separate from VideoIsPlaying to prevent MediaElementsMonitor from overriding
        /// theme pause requests. Music is paused if EITHER VideoIsPlaying OR ThemeOverlayActive is true.
        /// </summary>
        public bool ThemeOverlayActive
        {
            get => themeOverlayActive;
            set { themeOverlayActive = value; OnPropertyChanged(); }
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
        /// Pause music when Playnite is minimized to taskbar.
        /// Music will resume when Playnite is restored.
        /// </summary>
        public bool PauseOnMinimize
        {
            get => pauseOnMinimize;
            set { pauseOnMinimize = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Pause music when Playnite is hidden in the system tray.
        /// Music will resume when Playnite is restored from the tray.
        /// </summary>
        public bool PauseWhenInSystemTray
        {
            get => pauseWhenInSystemTray;
            set { pauseWhenInSystemTray = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Show "Now Playing" song info in the Desktop top panel bar.
        /// Displays song title, artist (if available), and duration next to the play/pause buttons.
        /// </summary>
        public bool ShowNowPlayingInTopPanel
        {
            get => showNowPlayingInTopPanel;
            set { showNowPlayingInTopPanel = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Show media control buttons (play/pause and skip) in the Desktop top panel bar.
        /// When disabled, only the Now Playing text is shown (if enabled).
        /// </summary>
        public bool ShowDesktopMediaControls
        {
            get => showDesktopMediaControls;
            set { showDesktopMediaControls = value; OnPropertyChanged(); }
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
        private bool autoNormalizeAfterDownload = false;

        // Audio Trimming Settings
        private string trimSuffix = "-trimmed";
        private string preciseTrimSuffix = "-ptrimmed";

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
        /// Automatically normalize downloaded music files.
        /// When enabled, music files will be normalized to EBU R128 standard after downloading.
        /// Uses the configured normalization settings (target loudness, true peak, etc.)
        /// </summary>
        public bool AutoNormalizeAfterDownload
        {
            get => autoNormalizeAfterDownload;
            set { autoNormalizeAfterDownload = value; OnPropertyChanged(); }
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

        /// <summary>
        /// Suffix to append to precise-trimmed file names (e.g., "-ptrimmed")
        /// Used by the waveform-based precise trim feature
        /// </summary>
        public string PreciseTrimSuffix
        {
            get => preciseTrimSuffix;
            set { preciseTrimSuffix = value ?? "-ptrimmed"; OnPropertyChanged(); }
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

        // Auto-Download on Library Update Settings
        private bool autoDownloadOnLibraryUpdate = true;
        private DateTime lastAutoLibUpdateAssetsDownload = DateTime.MinValue;

        /// <summary>
        /// Automatically download music for newly added games when library is updated.
        /// When enabled, music will be automatically downloaded for games added since the last check.
        /// Uses BestAlbumPick and BestSongPick to select the most relevant music.
        /// </summary>
        public bool AutoDownloadOnLibraryUpdate
        {
            get => autoDownloadOnLibraryUpdate;
            set { autoDownloadOnLibraryUpdate = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Timestamp of the last automatic library update asset download.
        /// Used to track which games need music downloaded.
        /// </summary>
        public DateTime LastAutoLibUpdateAssetsDownload
        {
            get => lastAutoLibUpdateAssetsDownload;
            set { lastAutoLibUpdateAssetsDownload = value; OnPropertyChanged(); }
        }

        private int maxConcurrentDownloads = 3;

        /// <summary>
        /// Maximum number of concurrent downloads during batch operations.
        /// Higher values speed up batch downloads but may overwhelm servers.
        /// Range: 1-5, Default: 3
        /// </summary>
        public int MaxConcurrentDownloads
        {
            get => maxConcurrentDownloads;
            set { maxConcurrentDownloads = Math.Max(1, Math.Min(5, value)); OnPropertyChanged(); }
        }

        // Music Status Tag Settings
        private bool autoTagOnLibraryUpdate = true;

        /// <summary>
        /// Automatically update music status tags when library is updated.
        /// When enabled, games will be tagged with "[UPS] Has Music" or "[UPS] No Music"
        /// based on whether they have downloaded music. These tags can be used for filtering.
        /// </summary>
        public bool AutoTagOnLibraryUpdate
        {
            get => autoTagOnLibraryUpdate;
            set { autoTagOnLibraryUpdate = value; OnPropertyChanged(); }
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

        // Live Effects Settings
        private bool liveEffectsEnabled = false;
        private bool lowPassEnabled = false;
        private bool highPassEnabled = false;
        private bool reverbEnabled = false;
        private int lowPassCutoff = 8000;
        private int highPassCutoff = 80;
        private int reverbWetGain = -6;   // dB (-20 to 10) - Audacity allows up to +10
        private int reverbDryGain = 0;    // dB (-20 to 0)
        private int reverbRoomSize = 75;  // % (0-100)
        private int reverbReverberance = 50; // % (0-100) - reverb tail length (Audacity parameter)
        private int reverbDamping = 50;   // % (0-100) - HF damping
        private int reverbToneLow = 100;  // % (0-100) - bass content of reverb (Audacity parameter)
        private int reverbToneHigh = 100; // % (0-100) - treble content of reverb (Audacity parameter)
        private int reverbPreDelay = 10;  // ms (0-200) - delay before reverb starts
        private int reverbStereoWidth = 100; // % (0-100) - stereo spread
        private bool makeupGainEnabled = false;
        private int makeupGain = 0;       // dB (-6 to +12) - output gain after effects
        private ReverbPreset selectedReverbPreset = ReverbPreset.Custom;
        private EffectChainPreset effectChainPreset = EffectChainPreset.Standard;

        // Advanced Reverb Tuning (expert mode)
        private bool advancedReverbTuningEnabled = false;
        private int reverbWetGainMultiplier = 3;     // 1-25 (displayed as 0.01-0.25, stored as 1-25 for int slider)
        private int reverbAllpassFeedback = 50;     // 30-70 (displayed as 0.30-0.70)
        private int reverbHfDampingMin = 20;        // 10-40 (displayed as 0.10-0.40)
        private int reverbHfDampingMax = 50;        // 30-70 (displayed as 0.30-0.70)

        /// <summary>
        /// Enable live audio effects processing.
        /// When enabled, uses NAudio-based player with real-time effects chain.
        /// When disabled, uses standard WPF MediaPlayer for playback.
        /// Note: Toggling this setting will restart the current song.
        /// </summary>
        public bool LiveEffectsEnabled
        {
            get => liveEffectsEnabled;
            set { liveEffectsEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Enable low-pass filter effect (removes high frequencies).
        /// Only works when LiveEffectsEnabled is true.
        /// Use to create a "muffled" or "underwater" sound effect.
        /// </summary>
        public bool LowPassEnabled
        {
            get => lowPassEnabled;
            set { lowPassEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Enable high-pass filter effect (removes low frequencies).
        /// Only works when LiveEffectsEnabled is true.
        /// Use to reduce bass/rumble or create a "thin" sound.
        /// </summary>
        public bool HighPassEnabled
        {
            get => highPassEnabled;
            set { highPassEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Enable reverb effect (adds spacial echo/ambiance).
        /// Only works when LiveEffectsEnabled is true.
        /// Creates a sense of space or room around the sound.
        /// </summary>
        public bool ReverbEnabled
        {
            get => reverbEnabled;
            set { reverbEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Low-pass filter cutoff frequency in Hz.
        /// Range: 200 - 20000 Hz. Default: 8000 Hz (fairly open).
        /// Lower values = more muffled sound.
        /// </summary>
        public int LowPassCutoff
        {
            get => lowPassCutoff;
            set { lowPassCutoff = Math.Max(200, Math.Min(20000, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// High-pass filter cutoff frequency in Hz.
        /// Range: 20 - 2000 Hz. Default: 80 Hz (minimal bass cut).
        /// Higher values = more bass removed.
        /// </summary>
        public int HighPassCutoff
        {
            get => highPassCutoff;
            set { highPassCutoff = Math.Max(20, Math.Min(2000, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Reverb wet (effect) gain in dB.
        /// Range: -20 to +10 dB. Default: -6 dB.
        /// Controls the volume of the reverb effect independently from dry signal.
        /// Positive values create more pronounced reverb effects.
        /// </summary>
        public int ReverbWetGain
        {
            get => reverbWetGain;
            set { reverbWetGain = Math.Max(-20, Math.Min(10, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Reverb dry (original) gain in dB.
        /// Range: -20 to 0 dB. Default: 0 dB (full volume).
        /// Controls the volume of the original signal independently from reverb.
        /// </summary>
        public int ReverbDryGain
        {
            get => reverbDryGain;
            set { reverbDryGain = Math.Max(-20, Math.Min(0, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Reverb room size percentage.
        /// Range: 0 - 100%. Default: 75%.
        /// Controls the size of the virtual room (larger = longer decay).
        /// </summary>
        public int ReverbRoomSize
        {
            get => reverbRoomSize;
            set { reverbRoomSize = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Reverb reverberance (tail length) percentage.
        /// Range: 0 - 100%. Default: 50%.
        /// Controls how long the reverb tail continues after sound ends.
        /// Higher = longer, more "live" sounding reverb.
        /// </summary>
        public int ReverbReverberance
        {
            get => reverbReverberance;
            set { reverbReverberance = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Reverb high-frequency damping percentage.
        /// Range: 0 - 100%. Default: 50%.
        /// Controls how quickly high frequencies decay (higher = more muffled reverb tail).
        /// </summary>
        public int ReverbDamping
        {
            get => reverbDamping;
            set { reverbDamping = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Reverb tone low (bass) percentage.
        /// Range: 0 - 100%. Default: 100%.
        /// Controls bass content of reverb. Lower = less boomy reverb.
        /// </summary>
        public int ReverbToneLow
        {
            get => reverbToneLow;
            set { reverbToneLow = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Reverb tone high (treble) percentage.
        /// Range: 0 - 100%. Default: 100%.
        /// Controls treble content of reverb. Lower = darker, less bright reverb.
        /// </summary>
        public int ReverbToneHigh
        {
            get => reverbToneHigh;
            set { reverbToneHigh = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Reverb pre-delay in milliseconds.
        /// Range: 0 - 200 ms. Default: 10 ms.
        /// Delays the onset of reverb, creating sense of distance/space.
        /// </summary>
        public int ReverbPreDelay
        {
            get => reverbPreDelay;
            set { reverbPreDelay = Math.Max(0, Math.Min(200, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Reverb stereo width percentage.
        /// Range: 0 - 100%. Default: 100%.
        /// Controls the stereo spread of the reverb (0 = mono, 100 = full stereo).
        /// </summary>
        public int ReverbStereoWidth
        {
            get => reverbStereoWidth;
            set { reverbStereoWidth = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Enable makeup gain (output amplification).
        /// Only works when LiveEffectsEnabled is true.
        /// </summary>
        public bool MakeupGainEnabled
        {
            get => makeupGainEnabled;
            set { makeupGainEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Makeup gain applied after all effects in dB.
        /// Range: -6 to +12 dB. Default: 0 dB.
        /// Use to compensate for volume changes caused by effects.
        /// Only applied when MakeupGainEnabled is true.
        /// </summary>
        public int MakeupGain
        {
            get => makeupGain;
            set { makeupGain = Math.Max(-6, Math.Min(12, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Currently selected reverb preset.
        /// When set to a preset other than Custom, automatically applies the preset values.
        /// Changes to individual reverb parameters will set this to Custom.
        /// </summary>
        public ReverbPreset SelectedReverbPreset
        {
            get => selectedReverbPreset;
            set
            {
                if (selectedReverbPreset != value)
                {
                    selectedReverbPreset = value;
                    OnPropertyChanged();
                    // Apply preset values when a non-Custom preset is selected
                    if (value != ReverbPreset.Custom)
                    {
                        ApplyReverbPreset(value);
                    }
                }
            }
        }

        /// <summary>
        /// Selected effect chain preset determining the order in which effects are applied.
        /// Default: Standard (High-Pass → Low-Pass → Reverb)
        /// </summary>
        public EffectChainPreset EffectChainPreset
        {
            get => effectChainPreset;
            set
            {
                if (effectChainPreset != value)
                {
                    effectChainPreset = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EffectChainOrderDisplay));
                }
            }
        }

        /// <summary>
        /// Gets a display-friendly string representing the current effect chain order.
        /// </summary>
        public string EffectChainOrderDisplay
        {
            get
            {
                switch (effectChainPreset)
                {
                    case EffectChainPreset.Standard:
                        return "High-Pass → Low-Pass → Reverb";
                    case EffectChainPreset.ReverbFirst:
                        return "Reverb → High-Pass → Low-Pass";
                    case EffectChainPreset.LowPassFirst:
                        return "Low-Pass → High-Pass → Reverb";
                    case EffectChainPreset.LowPassThenReverb:
                        return "Low-Pass → Reverb → High-Pass";
                    case EffectChainPreset.HighPassThenReverb:
                        return "High-Pass → Reverb → Low-Pass";
                    case EffectChainPreset.ReverbThenLowPass:
                        return "Reverb → Low-Pass → High-Pass";
                    default:
                        return "High-Pass → Low-Pass → Reverb";
                }
            }
        }

        // ===== Advanced Reverb Tuning Properties =====

        /// <summary>
        /// Enable advanced reverb tuning controls.
        /// WARNING: These settings can produce very loud output that may damage hearing!
        /// Only enable if you understand the reverb algorithm parameters.
        /// </summary>
        public bool AdvancedReverbTuningEnabled
        {
            get => advancedReverbTuningEnabled;
            set { advancedReverbTuningEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Wet gain multiplier (1-25, representing 0.01-0.25).
        /// Controls overall reverb intensity. Higher = more pronounced reverb.
        /// Default: 8 (0.08). Range: 1-25.
        /// WARNING: Values above 15 can cause very loud output!
        /// </summary>
        public int ReverbWetGainMultiplier
        {
            get => reverbWetGainMultiplier;
            set { reverbWetGainMultiplier = Math.Max(1, Math.Min(25, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// All-pass filter feedback coefficient (30-70, representing 0.30-0.70).
        /// Controls reverb diffusion/smoothness. Higher = smoother, more blended.
        /// Default: 50 (0.50). Lower values create more distinct echoes.
        /// </summary>
        public int ReverbAllpassFeedback
        {
            get => reverbAllpassFeedback;
            set { reverbAllpassFeedback = Math.Max(30, Math.Min(70, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Minimum HF damping coefficient (10-40, representing 0.10-0.40).
        /// Controls brightness at 0% damping setting. Lower = brighter reverb.
        /// Default: 20 (0.20).
        /// </summary>
        public int ReverbHfDampingMin
        {
            get => reverbHfDampingMin;
            set { reverbHfDampingMin = Math.Max(10, Math.Min(40, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Maximum HF damping coefficient (30-70, representing 0.30-0.70).
        /// Controls darkness at 100% damping setting. Higher = darker reverb.
        /// Default: 50 (0.50).
        /// </summary>
        public int ReverbHfDampingMax
        {
            get => reverbHfDampingMax;
            set { reverbHfDampingMax = Math.Max(30, Math.Min(70, value)); OnPropertyChanged(); }
        }

        // ===== Toast Notification Settings =====
        private bool enableToastAcrylicBlur = true;
        private int toastBlurOpacity = 94;  // 0-255, stored as int for slider
        private string toastBlurTintColor = "000521";  // Hex RGB without #
        private int toastBlurMode = 1;  // 0 = Basic blur, 1 = Acrylic blur (with noise texture)
        private double toastCornerRadius = 0;  // 0 = square corners (avoids blur corner artifacts)
        private double toastWidth = 420;
        private double toastMinHeight = 90;
        private double toastMaxHeight = 180;
        private int toastDurationMs = 4000;
        private int toastEdgeMargin = 30;
        private string toastBorderColor = "2A2A2A";  // Hex RGB without # - darker border that blends better
        private double toastBorderThickness = 1;  // Border thickness in pixels - thinner looks cleaner

        /// <summary>
        /// Enable acrylic blur effect on toast notifications.
        /// Uses Windows DWM APIs for native blur - requires Windows 10 1803+.
        /// Falls back gracefully on older systems.
        /// </summary>
        public bool EnableToastAcrylicBlur
        {
            get => enableToastAcrylicBlur;
            set { enableToastAcrylicBlur = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Toast blur opacity (0-255). Higher = more opaque/darker, lower = more transparent.
        /// Default: 221 (87% opacity). Recommended range: 150-240.
        /// </summary>
        public int ToastBlurOpacity
        {
            get => toastBlurOpacity;
            set { toastBlurOpacity = Math.Max(0, Math.Min(255, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Toast blur tint color (RGB hex without #). This colors the blur effect.
        /// Default: "1E1E1E" (dark gray). Format: "RRGGBB"
        /// </summary>
        public string ToastBlurTintColor
        {
            get => toastBlurTintColor;
            set { toastBlurTintColor = value ?? "1E1E1E"; OnPropertyChanged(); }
        }

        /// <summary>
        /// Toast blur mode: 0 = Basic blur (less intense), 1 = Acrylic blur (more intense with noise texture).
        /// Acrylic blur requires Windows 10 1803+.
        /// Default: 1 (Acrylic)
        /// </summary>
        public int ToastBlurMode
        {
            get => toastBlurMode;
            set { toastBlurMode = Math.Max(0, Math.Min(1, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Toast corner radius in pixels. Set to 0 for square corners.
        /// Default: 8
        /// </summary>
        public double ToastCornerRadius
        {
            get => toastCornerRadius;
            set { toastCornerRadius = Math.Max(0, Math.Min(32, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Toast width in pixels.
        /// Default: 420
        /// </summary>
        public double ToastWidth
        {
            get => toastWidth;
            set { toastWidth = Math.Max(200, Math.Min(800, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Toast minimum height in pixels.
        /// Default: 90
        /// </summary>
        public double ToastMinHeight
        {
            get => toastMinHeight;
            set { toastMinHeight = Math.Max(50, Math.Min(300, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Toast maximum height in pixels.
        /// Default: 180
        /// </summary>
        public double ToastMaxHeight
        {
            get => toastMaxHeight;
            set { toastMaxHeight = Math.Max(100, Math.Min(500, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Toast display duration in milliseconds.
        /// Default: 4000 (4 seconds). Range: 1000-10000
        /// </summary>
        public int ToastDurationMs
        {
            get => toastDurationMs;
            set { toastDurationMs = Math.Max(1000, Math.Min(10000, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Margin from screen edge for toast positioning (in pixels).
        /// Default: 30
        /// </summary>
        public int ToastEdgeMargin
        {
            get => toastEdgeMargin;
            set { toastEdgeMargin = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Toast border color (RGB hex without #). This is the outer border color.
        /// Default: "2A2A2A" (dark gray that blends with blur background)
        /// </summary>
        public string ToastBorderColor
        {
            get => toastBorderColor;
            set { toastBorderColor = value ?? "2A2A2A"; OnPropertyChanged(); }
        }

        /// <summary>
        /// Toast border thickness in pixels.
        /// Default: 1 (thin border that's subtle). Range: 0-5
        /// </summary>
        public double ToastBorderThickness
        {
            get => toastBorderThickness;
            set { toastBorderThickness = Math.Max(0, Math.Min(5, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Applies a reverb preset by setting all reverb parameters.
        /// Presets are based on Audacity's exact factory preset values.
        /// Format: RoomSize, PreDelay, Reverberance, HfDamping, ToneLow, ToneHigh, WetGain, DryGain, StereoWidth
        /// </summary>
        public void ApplyReverbPreset(ReverbPreset preset)
        {
            switch (preset)
            {
                // === General Purpose ===
                case ReverbPreset.Acoustic:
                    // Acoustic - natural acoustic sound
                    ReverbRoomSize = 50;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 75;
                    ReverbDamping = 100;
                    ReverbToneLow = 21;
                    ReverbToneHigh = 100;
                    ReverbWetGain = -14;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 80;
                    break;

                case ReverbPreset.Ambience:
                    // Ambience - spacious atmospheric effect (very pronounced)
                    ReverbRoomSize = 100;
                    ReverbPreDelay = 55;
                    ReverbReverberance = 100;
                    ReverbDamping = 50;
                    ReverbToneLow = 53;
                    ReverbToneHigh = 38;
                    ReverbWetGain = 0;
                    ReverbDryGain = -10;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.Artificial:
                    // Artificial - synthetic/electronic reverb character
                    ReverbRoomSize = 81;
                    ReverbPreDelay = 99;
                    ReverbReverberance = 23;
                    ReverbDamping = 62;
                    ReverbToneLow = 16;
                    ReverbToneHigh = 19;
                    ReverbWetGain = -4;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.Clean:
                    // Clean - subtle, transparent reverb
                    ReverbRoomSize = 50;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 75;
                    ReverbDamping = 100;
                    ReverbToneLow = 55;
                    ReverbToneHigh = 100;
                    ReverbWetGain = -18;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 75;
                    break;

                case ReverbPreset.Modern:
                    // Modern - contemporary production style
                    ReverbRoomSize = 50;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 75;
                    ReverbDamping = 100;
                    ReverbToneLow = 55;
                    ReverbToneHigh = 100;
                    ReverbWetGain = -15;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 75;
                    break;

                // === Vocals ===
                case ReverbPreset.VocalI:
                    // Vocal I - subtle vocal reverb
                    ReverbRoomSize = 70;
                    ReverbPreDelay = 20;
                    ReverbReverberance = 40;
                    ReverbDamping = 99;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 50;
                    ReverbWetGain = -12;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 70;
                    break;

                case ReverbPreset.VocalII:
                    // Vocal II - more pronounced vocal reverb
                    ReverbRoomSize = 50;
                    ReverbPreDelay = 0;
                    ReverbReverberance = 50;
                    ReverbDamping = 99;
                    ReverbToneLow = 50;
                    ReverbToneHigh = 100;
                    ReverbWetGain = -1;
                    ReverbDryGain = -1;
                    ReverbStereoWidth = 70;
                    break;

                case ReverbPreset.DanceVocal:
                    // Dance Vocal - modern dance/EDM vocal effect
                    ReverbRoomSize = 90;
                    ReverbPreDelay = 2;
                    ReverbReverberance = 60;
                    ReverbDamping = 77;
                    ReverbToneLow = 30;
                    ReverbToneHigh = 51;
                    ReverbWetGain = -10;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.ModernVocal:
                    // Modern Vocal - contemporary vocal production
                    ReverbRoomSize = 66;
                    ReverbPreDelay = 27;
                    ReverbReverberance = 77;
                    ReverbDamping = 8;
                    ReverbToneLow = 0;
                    ReverbToneHigh = 51;
                    ReverbWetGain = -10;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 68;
                    break;

                case ReverbPreset.VoiceTail:
                    // Voice Tail - long reverb tail for vocals
                    ReverbRoomSize = 66;
                    ReverbPreDelay = 27;
                    ReverbReverberance = 100;
                    ReverbDamping = 8;
                    ReverbToneLow = 0;
                    ReverbToneHigh = 51;
                    ReverbWetGain = -6;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 68;
                    break;

                // === Room Sizes ===
                case ReverbPreset.Bathroom:
                    // Bathroom - small reflective space
                    ReverbRoomSize = 16;
                    ReverbPreDelay = 8;
                    ReverbReverberance = 80;
                    ReverbDamping = 0;
                    ReverbToneLow = 0;
                    ReverbToneHigh = 100;
                    ReverbWetGain = -6;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.SmallRoomBright:
                    // Small Room Bright - bright reflections
                    ReverbRoomSize = 30;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 50;
                    ReverbDamping = 50;
                    ReverbToneLow = 50;
                    ReverbToneHigh = 100;
                    ReverbWetGain = -1;
                    ReverbDryGain = -1;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.SmallRoomDark:
                    // Small Room Dark - muffled reflections
                    ReverbRoomSize = 30;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 50;
                    ReverbDamping = 50;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 0;
                    ReverbWetGain = -1;
                    ReverbDryGain = -1;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.MediumRoom:
                    // Medium Room - balanced room sound
                    ReverbRoomSize = 75;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 40;
                    ReverbDamping = 50;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 70;
                    ReverbWetGain = -1;
                    ReverbDryGain = -1;
                    ReverbStereoWidth = 70;
                    break;

                case ReverbPreset.LargeRoom:
                    // Large Room - spacious room
                    ReverbRoomSize = 85;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 40;
                    ReverbDamping = 50;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 80;
                    ReverbWetGain = 0;
                    ReverbDryGain = -6;
                    ReverbStereoWidth = 90;
                    break;

                case ReverbPreset.ChurchHall:
                    // Church Hall - large reverberant space
                    ReverbRoomSize = 90;
                    ReverbPreDelay = 32;
                    ReverbReverberance = 60;
                    ReverbDamping = 50;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 50;
                    ReverbWetGain = 0;
                    ReverbDryGain = -12;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.Cathedral:
                    // Cathedral - massive cathedral space
                    ReverbRoomSize = 90;
                    ReverbPreDelay = 16;
                    ReverbReverberance = 90;
                    ReverbDamping = 50;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 0;
                    ReverbWetGain = 0;
                    ReverbDryGain = -20;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.BigCave:
                    // Big Cave - cavernous space (very pronounced)
                    ReverbRoomSize = 100;
                    ReverbPreDelay = 55;
                    ReverbReverberance = 100;
                    ReverbDamping = 50;
                    ReverbToneLow = 53;
                    ReverbToneHigh = 38;
                    ReverbWetGain = 5;
                    ReverbDryGain = -3;
                    ReverbStereoWidth = 100;
                    break;

                // ===== UniPlaySong Custom Presets - Living/Entertainment =====

                case ReverbPreset.LivingRoom:
                    // Living Room - cozy TV viewing environment
                    // Subtle, warm reverb that doesn't overwhelm dialogue
                    ReverbRoomSize = 35;
                    ReverbPreDelay = 8;
                    ReverbReverberance = 30;
                    ReverbDamping = 65;
                    ReverbToneLow = 70;
                    ReverbToneHigh = 60;
                    ReverbWetGain = -12;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 60;
                    break;

                case ReverbPreset.HomeTheater:
                    // Home Theater - movie night experience
                    // Wider soundstage with controlled tail for cinematic feel
                    ReverbRoomSize = 55;
                    ReverbPreDelay = 15;
                    ReverbReverberance = 45;
                    ReverbDamping = 55;
                    ReverbToneLow = 80;
                    ReverbToneHigh = 70;
                    ReverbWetGain = -8;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 85;
                    break;

                case ReverbPreset.LateNightTV:
                    // Late Night TV - subtle, intimate ambience
                    // Very restrained reverb, perfect for quiet viewing
                    ReverbRoomSize = 25;
                    ReverbPreDelay = 5;
                    ReverbReverberance = 20;
                    ReverbDamping = 75;
                    ReverbToneLow = 60;
                    ReverbToneHigh = 40;
                    ReverbWetGain = -15;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 50;
                    break;

                case ReverbPreset.LoungeCafe:
                    // Lounge/Cafe - relaxed background music atmosphere
                    // Warm, smooth reverb like a cozy cafe
                    ReverbRoomSize = 40;
                    ReverbPreDelay = 12;
                    ReverbReverberance = 35;
                    ReverbDamping = 60;
                    ReverbToneLow = 85;
                    ReverbToneHigh = 55;
                    ReverbWetGain = -10;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 70;
                    break;

                case ReverbPreset.JazzClub:
                    // Jazz Club - intimate live performance feel
                    // Tight, controlled reverb with warmth
                    ReverbRoomSize = 45;
                    ReverbPreDelay = 18;
                    ReverbReverberance = 40;
                    ReverbDamping = 45;
                    ReverbToneLow = 90;
                    ReverbToneHigh = 65;
                    ReverbWetGain = -6;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 75;
                    break;

                case ReverbPreset.NightClub:
                    // Night Club - energetic dance floor
                    // Punchy with controlled low-end, wider stereo
                    ReverbRoomSize = 60;
                    ReverbPreDelay = 25;
                    ReverbReverberance = 55;
                    ReverbDamping = 40;
                    ReverbToneLow = 70;
                    ReverbToneHigh = 80;
                    ReverbWetGain = -4;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 95;
                    break;

                case ReverbPreset.ConcertHall:
                    // Concert Hall - live music experience
                    // Large, natural acoustic space
                    ReverbRoomSize = 80;
                    ReverbPreDelay = 22;
                    ReverbReverberance = 65;
                    ReverbDamping = 35;
                    ReverbToneLow = 95;
                    ReverbToneHigh = 75;
                    ReverbWetGain = -2;
                    ReverbDryGain = -3;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.Custom:
                default:
                    // Custom - don't change anything
                    break;
            }
        }
    }
}
