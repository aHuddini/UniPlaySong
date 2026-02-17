using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Windows.Media;
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
        ConcertHall,        // Live concert experience
        // UniPlaySong custom presets - Creative/Stylistic
        Dreamy,             // Ethereal, lush wash — pairs well with slow effect
        LoFi,               // Muffled, tape-like warmth — lo-fi hip hop aesthetic
        Underwater,         // Deep, submerged, surreal atmosphere
        SciFiCorridor,      // Metallic, spacious, futuristic echo
        VinylWarmth,        // Subtle warm coloring — pairs with slow for full vinyl feel
        Vaporwave           // The classic slowed+reverb aesthetic — huge, wet, dreamy
    }

    /// <summary>
    /// High-level style presets that configure all live effects at once.
    /// Each style creatively combines HP, LP, reverb, slow, and gain settings.
    /// </summary>
    public enum StylePreset
    {
        None = 0,
        // --- Huddini Styles ---
        HuddiniRehearsal,   // Wide stereo, rich reverb, live rehearsal room feel
        HuddiniBrightRoom,  // Open, spacious large room with wide stereo and bright tone
        HuddiniRetroRadio,  // Narrow bandpass, bathroom reverb, bitcrushed — old AM radio
        HuddiniLoBit,       // Dark lo-fi bandpass, heavy bitcrusher, LoFi reverb
        HuddiniSlowedDream, // Deep slow + vaporwave reverb, wide stereo, dreamy
        HuddiniCaveLake,    // Submerged cave — heavy slow, underwater reverb, chorus drift
        HuddiniHoneyRoom,   // Bright sci-fi reverb, subtle bitcrush, wide stereo, slight slow
        // --- No slow effect ---
        CleanBoost,         // Pure volume boost, no effects
        WarmFMRadio,        // Gentle low-pass + subtle reverb, FM station feel
        BrightAiry,         // High-pass cleanup + bright spacious reverb, light & open
        Telephone,          // Aggressive bandpass (HP + LP), tinny phone-call effect
        ConcertLive,        // Concert reverb + slight HP cleanup, live show feel
        MuffledNextRoom,    // Heavy low-pass + small room reverb, music from another room
        // --- With slow effect ---
        LoFiChill,          // Dark low-pass + slow + lo-fi reverb, study beats vibe
        SlowedReverb,       // The classic: slow + big wet reverb
        VinylNight,         // Gentle slow + warm reverb + soft low-pass, old record player
        UnderwaterDream,    // Heavy low-pass + slow + deep submerged reverb
        Cyberpunk           // Sci-fi reverb + HP + slight slow, dark futuristic city
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

    /// <summary>
    /// Visualizer tuning presets — curated combinations of all viz parameters.
    /// Selecting a preset overwrites all visualizer settings; user can then tweak.
    /// </summary>
    /// <summary>
    /// Visualizer bar color themes.
    /// </summary>
    public enum VizColorTheme
    {
        Dynamic = 0,    // Game Art V1: sampled from game artwork, natural tones
        Classic,        // White (original look — solid frozen brush)
        Neon,           // Cyan → Magenta gradient
        Fire,           // Orange → Red gradient
        Ocean,          // Teal → Blue gradient
        Sunset,         // Yellow → Pink gradient
        Matrix,         // Dark green → Bright green gradient
        Ice,            // White → Light blue gradient
        Synthwave,      // Deep purple → Hot pink gradient
        Ember,          // Dark ember → Bright amber gradient
        Abyss,          // Deep navy → Aqua gradient
        Solar,          // Warm rust → Golden yellow gradient
        Vapor,          // Mint green → Lavender (replaced Terminal)
        Frost,          // Steel blue → Frost white gradient
        DynamicVivid,   // Vibrant Vibes: aggressive color separation, vivid creative gradients
        Aurora,         // Teal → Lime green
        Coral,          // Deep coral → Peach gold
        Plasma,         // Electric indigo → Hot magenta-red
        Toxic,          // Acid yellow-green → Deep purple
        Cherry,         // Dark cherry → Rose pink
        Midnight,       // Dark indigo → Bright cyan
        DynamicAlt      // Alt Algo: v7 center-weighted + bucket merging + diversity bonus
    }

    public enum DefaultMusicSource
    {
        CustomFile,     // User-selected music file
        NativeTheme,    // Playnite's built-in theme music
        BundledPreset   // Bundled ambient tracks shipped with the plugin
    }

    public enum CelebrationSoundType
    {
        SystemBeep,     // Windows system sound
        BundledJingle,  // Jingle preset shipped with the plugin
        CustomFile      // User-selected audio file (.wav recommended)
    }

    public enum CelebrationToastTheme
    {
        Gold = 0,        // Default — amber/gold (#FFC107)
        RoyalPurple,     // Purple/violet
        Emerald,         // Green
        Ruby,            // Red
        IceBlue,         // Blue/cyan
        Sunset,          // Orange/warm
        Platinum         // Silver/white
    }

    public enum ProgressBarPosition
    {
        AfterSkipButton,    // Between skip button and visualizer
        AfterVisualizer,    // Between visualizer and now playing
        AfterNowPlaying,    // Rightmost position in top panel
        BelowNowPlaying     // Embedded inside now playing panel (below scrolling text)
    }

    public enum VizPreset
    {
        Custom = 0,
        Default,        // Factory defaults — balanced starting point
        Smooth,         // Gentle, flowing bars — low gravity, heavy smoothing
        Punchy,         // Snappy beats, fast attack — good for hip-hop/EDM
        Cinematic,      // Wide, slow-moving — film score / orchestral
        Minimal,        // Subtle, understated — low gain, high compression
        Reactive        // Maximum responsiveness — minimal smoothing, high gain
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
        private bool pauseOnGameStart = false;
        private bool pauseOnSystemLock = false;
        private bool pauseOnExternalAudio = false;
        private int externalAudioDebounceSeconds = 3;
        private bool showNowPlayingInTopPanel = false;
        private bool hideNowPlayingForDefaultMusic = false;
        private bool showDefaultMusicIndicator = false;
        private bool showProgressBar = false;
        private ProgressBarPosition progressBarPosition = ProgressBarPosition.AfterSkipButton;
        private bool showDesktopMediaControls = false;
        private bool showSpectrumVisualizer = true;
        private bool autoDeleteMusicOnGameRemoval = true;

        // Gamification
        private bool enableCompletionCelebration = true;
        private CelebrationSoundType celebrationSoundType = CelebrationSoundType.BundledJingle;
        private string selectedCelebrationJingle = "Streets of Rage 1 - Sega Genesis - Level Clear.mp3";
        private string celebrationSoundPath = string.Empty;
        private bool showCelebrationToast = true;
        private int celebrationToastDurationSeconds = 6;
        private CelebrationToastTheme celebrationToastTheme = CelebrationToastTheme.Gold;
        private bool applyLiveEffectsToJingles = true;

        // Download notifications
        private bool playSoundOnDownloadComplete = false;

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
        /// Tracks if a video is currently playing (set by MediaElementsMonitor).
        /// Runtime-only state — excluded from serialization so it always starts false.
        /// </summary>
        [JsonIgnore]
        public bool VideoIsPlaying
        {
            get => videoIsPlaying;
            set { videoIsPlaying = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Tracks if a theme overlay is active (set by MusicControl from theme Tag bindings).
        /// This is separate from VideoIsPlaying to prevent MediaElementsMonitor from overriding
        /// theme pause requests. Music is paused if EITHER VideoIsPlaying OR ThemeOverlayActive is true.
        /// Runtime-only state — excluded from serialization so it always starts false.
        /// </summary>
        [JsonIgnore]
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

        // Song List Cache Settings (opt-in performance feature)
        private bool enableSongListCache = false;

        /// <summary>
        /// Enable song list caching to optimize game selection performance.
        /// When enabled, directory scans are cached in-memory for the current Playnite session.
        /// Cache is automatically reset when Playnite restarts.
        /// </summary>
        public bool EnableSongListCache
        {
            get => enableSongListCache;
            set { enableSongListCache = value; OnPropertyChanged(); }
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

        // Pause music when a game is launched. Music resumes when the game closes.
        public bool PauseOnGameStart
        {
            get => pauseOnGameStart;
            set { pauseOnGameStart = value; OnPropertyChanged(); }
        }

        // Pause music when Windows session is locked (Win+L). Music resumes on unlock.
        public bool PauseOnSystemLock
        {
            get => pauseOnSystemLock;
            set { pauseOnSystemLock = value; OnPropertyChanged(); }
        }

        // Pause music when another application starts playing audio. Resumes when external audio stops.
        public bool PauseOnExternalAudio
        {
            get => pauseOnExternalAudio;
            set { pauseOnExternalAudio = value; OnPropertyChanged(); }
        }

        // How many seconds external audio must persist before pausing, and silence before resuming (1-10).
        public int ExternalAudioDebounceSeconds
        {
            get => externalAudioDebounceSeconds;
            set { externalAudioDebounceSeconds = Math.Max(1, Math.Min(10, value)); OnPropertyChanged(); }
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

        // When enabled, hides the Now Playing panel when the selected game has no downloaded music
        // (i.e. when default/fallback music is playing instead of game-specific music)
        public bool HideNowPlayingForDefaultMusic
        {
            get => hideNowPlayingForDefaultMusic;
            set { hideNowPlayingForDefaultMusic = value; OnPropertyChanged(); }
        }

        // When enabled, shows "[Default]" prefix in Now Playing when default/fallback music is playing
        public bool ShowDefaultMusicIndicator
        {
            get => showDefaultMusicIndicator;
            set { showDefaultMusicIndicator = value; OnPropertyChanged(); }
        }

        // Show a thin progress bar indicating playback position in the Desktop top panel
        public bool ShowProgressBar
        {
            get => showProgressBar;
            set { showProgressBar = value; OnPropertyChanged(); }
        }

        // Where to place the progress bar in the top panel
        public ProgressBarPosition ProgressBarPosition
        {
            get => progressBarPosition;
            set { progressBarPosition = value; OnPropertyChanged(); }
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

        /// <summary>
        /// Show a spectrum visualizer in the Desktop top panel bar.
        /// Displays real-time frequency bars next to the media controls (requires Live Effects enabled).
        /// </summary>
        public bool ShowSpectrumVisualizer
        {
            get => showSpectrumVisualizer;
            set { showSpectrumVisualizer = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Automatically delete music files when their associated games are removed from Playnite.
        /// </summary>
        public bool AutoDeleteMusicOnGameRemoval
        {
            get => autoDeleteMusicOnGameRemoval;
            set { autoDeleteMusicOnGameRemoval = value; OnPropertyChanged(); }
        }

        // Play a sound when a game's completion status changes to "Completed"
        public bool EnableCompletionCelebration
        {
            get => enableCompletionCelebration;
            set { enableCompletionCelebration = value; OnPropertyChanged(); }
        }

        public CelebrationSoundType CelebrationSoundType
        {
            get => celebrationSoundType;
            set { celebrationSoundType = value; OnPropertyChanged(); }
        }

        // Filename of selected bundled jingle (from Jingles folder)
        public string SelectedCelebrationJingle
        {
            get => selectedCelebrationJingle;
            set { selectedCelebrationJingle = value; OnPropertyChanged(); }
        }

        // Path to custom celebration sound file (.wav recommended for overlay playback)
        public string CelebrationSoundPath
        {
            get => celebrationSoundPath;
            set { celebrationSoundPath = value; OnPropertyChanged(); }
        }

        public bool ShowCelebrationToast
        {
            get => showCelebrationToast;
            set { showCelebrationToast = value; OnPropertyChanged(); }
        }

        public int CelebrationToastDurationSeconds
        {
            get => celebrationToastDurationSeconds;
            set { celebrationToastDurationSeconds = Math.Max(3, Math.Min(15, value)); OnPropertyChanged(); }
        }

        public CelebrationToastTheme CelebrationToastTheme
        {
            get => celebrationToastTheme;
            set { celebrationToastTheme = value; OnPropertyChanged(); }
        }

        // When enabled and Live Effects are active, jingles play through NAudio with effects (reverb, filters, etc.)
        // When disabled, jingles play through a plain player without effects
        public bool ApplyLiveEffectsToJingles
        {
            get => applyLiveEffectsToJingles;
            set { applyLiveEffectsToJingles = value; OnPropertyChanged(); }
        }

        // Play a system sound when any download completes successfully
        public bool PlaySoundOnDownloadComplete
        {
            get => playSoundOnDownloadComplete;
            set { playSoundOnDownloadComplete = value; OnPropertyChanged(); }
        }

        // Default Music Support
        private bool enableDefaultMusic = true;
        private string defaultMusicPath = string.Empty;
        private string backupCustomMusicPath = string.Empty; // Backup of custom path when using native as default
        private bool suppressPlayniteBackgroundMusic = true;
        private bool useNativeMusicAsDefault = false;
        private bool musicOnlyForInstalledGames = false;
        private DefaultMusicSource defaultMusicSourceOption = DefaultMusicSource.BundledPreset;
        private string selectedBundledPreset = string.Empty; // Filename of selected bundled preset
        private bool bundledPresetMigrated = false; // One-time migration flag for v1.2.11 bundled preset feature

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

        // Suppresses Playnite's native background music in fullscreen mode to prevent audio conflicts.
        // Independent of Default Music settings — strongly recommended to keep enabled.
        public bool SuppressPlayniteBackgroundMusic
        {
            get => suppressPlayniteBackgroundMusic;
            set
            {
                suppressPlayniteBackgroundMusic = value;
                OnPropertyChanged();
            }
        }

        // Use Playnite's native "Default" theme music as fallback when no game music is found.
        // Only applies when EnableDefaultMusic is true.
        public bool UseNativeMusicAsDefault
        {
            get => useNativeMusicAsDefault;
            set
            {
                useNativeMusicAsDefault = value;
                OnPropertyChanged();
            }
        }

        // When enabled, game-specific music only plays for installed games.
        // Uninstalled games fall through to default music (or silence if default music is off).
        public bool MusicOnlyForInstalledGames
        {
            get => musicOnlyForInstalledGames;
            set
            {
                musicOnlyForInstalledGames = value;
                OnPropertyChanged();
            }
        }

        // Which default music source to use: CustomFile, NativeTheme, or BundledPreset
        public DefaultMusicSource DefaultMusicSourceOption
        {
            get => defaultMusicSourceOption;
            set
            {
                defaultMusicSourceOption = value;
                OnPropertyChanged();
                // Keep UseNativeMusicAsDefault in sync for backward compatibility
                useNativeMusicAsDefault = (value == DefaultMusicSource.NativeTheme);
            }
        }

        // Filename of the selected bundled preset (e.g. "tunetank-dark-space-ambient-348870.mp3")
        public string SelectedBundledPreset
        {
            get => selectedBundledPreset;
            set
            {
                selectedBundledPreset = value ?? string.Empty;
                OnPropertyChanged();
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

        // One-time migration flag: existing users get DefaultMusicSourceOption set from their old boolean settings
        public bool BundledPresetMigrated
        {
            get => bundledPresetMigrated;
            set { bundledPresetMigrated = value; OnPropertyChanged(); }
        }

        // Determines if "Use Playnite Native 'Default' Theme Music" checkbox should be enabled.
        // Requires EnableDefaultMusic to be true.
        public bool IsUseNativeMusicAsDefaultEnabled => enableDefaultMusic;

        // Search Cache Settings
        private bool enableSearchCache = true;
        private int searchCacheDurationDays = 7;
        private bool autoCheckHintsOnStartup = true;

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

        /// <summary>
        /// Automatically check for search hints database updates on application startup.
        /// Compares the bundled version with the GitHub version and notifies if updates are available.
        /// </summary>
        public bool AutoCheckHintsOnStartup
        {
            get => autoCheckHintsOnStartup;
            set { autoCheckHintsOnStartup = value; OnPropertyChanged(); }
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

        // Song Playback Behavior
        private bool randomizeOnEverySelect = false;
        private bool randomizeOnMusicEnd = true;
        private bool stopAfterSongEnds = false;

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

        // Stop playback after current song finishes instead of looping or randomizing
        public bool StopAfterSongEnds
        {
            get => stopAfterSongEnds;
            set { stopAfterSongEnds = value; OnPropertyChanged(); }
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
        private int reverbMix = 50;          // % (0-100) - wet/dry crossfade (0=fully dry, 100=fully wet)
        private bool makeupGainEnabled = false;
        private int makeupGain = 0;       // dB (-6 to +12) - output gain after effects
        private ReverbPreset selectedReverbPreset = ReverbPreset.Custom;
        private EffectChainPreset effectChainPreset = EffectChainPreset.Standard;
        private bool slowEnabled = false;
        private int slowAmount = 0;           // 0-50 (maps to speed: 1.0x to 0.5x)
        private StylePreset selectedStylePreset = StylePreset.None;
        private bool _applyingStylePreset = false;

        // Stereo Widener
        private bool stereoWidenerEnabled = false;
        private int stereoWidenerWidth = 50;      // 0-100 (0=mono, 50=normal, 100=max wide)

        // Chorus
        private bool chorusEnabled = false;
        private int chorusRate = 30;              // tenths of Hz (1-50 → 0.1-5.0 Hz)
        private int chorusDepth = 50;             // % (0-100) - modulation depth
        private int chorusMix = 40;               // % (0-100) - wet/dry

        // Bitcrusher
        private bool bitcrusherEnabled = false;
        private int bitcrusherBitDepth = 8;       // bits (2-16)
        private int bitcrusherDownsample = 1;     // rate divisor (1-32, 1=off)

        // Tremolo
        private bool tremoloEnabled = false;
        private int tremoloRate = 40;             // tenths of Hz (1-100 → 0.1-10.0 Hz)
        private int tremoloDepth = 50;            // % (0-100)

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
            set { lowPassEnabled = value; OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Enable high-pass filter effect (removes low frequencies).
        /// Only works when LiveEffectsEnabled is true.
        /// Use to reduce bass/rumble or create a "thin" sound.
        /// </summary>
        public bool HighPassEnabled
        {
            get => highPassEnabled;
            set { highPassEnabled = value; OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Enable reverb effect (adds spacial echo/ambiance).
        /// Only works when LiveEffectsEnabled is true.
        /// Creates a sense of space or room around the sound.
        /// </summary>
        public bool ReverbEnabled
        {
            get => reverbEnabled;
            set { reverbEnabled = value; OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Low-pass filter cutoff frequency in Hz.
        /// Range: 200 - 20000 Hz. Default: 8000 Hz (fairly open).
        /// Lower values = more muffled sound.
        /// </summary>
        public int LowPassCutoff
        {
            get => lowPassCutoff;
            set { lowPassCutoff = Math.Max(200, Math.Min(20000, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// High-pass filter cutoff frequency in Hz.
        /// Range: 20 - 2000 Hz. Default: 80 Hz (minimal bass cut).
        /// Higher values = more bass removed.
        /// </summary>
        public int HighPassCutoff
        {
            get => highPassCutoff;
            set { highPassCutoff = Math.Max(20, Math.Min(2000, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
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
            set { reverbWetGain = Math.Max(-20, Math.Min(10, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Reverb dry (original) gain in dB.
        /// Range: -20 to 0 dB. Default: 0 dB (full volume).
        /// Controls the volume of the original signal independently from reverb.
        /// </summary>
        public int ReverbDryGain
        {
            get => reverbDryGain;
            set { reverbDryGain = Math.Max(-20, Math.Min(0, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Reverb room size percentage.
        /// Range: 0 - 100%. Default: 75%.
        /// Controls the size of the virtual room (larger = longer decay).
        /// </summary>
        public int ReverbRoomSize
        {
            get => reverbRoomSize;
            set { reverbRoomSize = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
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
            set { reverbReverberance = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Reverb high-frequency damping percentage.
        /// Range: 0 - 100%. Default: 50%.
        /// Controls how quickly high frequencies decay (higher = more muffled reverb tail).
        /// </summary>
        public int ReverbDamping
        {
            get => reverbDamping;
            set { reverbDamping = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Reverb tone low (bass) percentage.
        /// Range: 0 - 100%. Default: 100%.
        /// Controls bass content of reverb. Lower = less boomy reverb.
        /// </summary>
        public int ReverbToneLow
        {
            get => reverbToneLow;
            set { reverbToneLow = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Reverb tone high (treble) percentage.
        /// Range: 0 - 100%. Default: 100%.
        /// Controls treble content of reverb. Lower = darker, less bright reverb.
        /// </summary>
        public int ReverbToneHigh
        {
            get => reverbToneHigh;
            set { reverbToneHigh = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Reverb pre-delay in milliseconds.
        /// Range: 0 - 200 ms. Default: 10 ms.
        /// Delays the onset of reverb, creating sense of distance/space.
        /// </summary>
        public int ReverbPreDelay
        {
            get => reverbPreDelay;
            set { reverbPreDelay = Math.Max(0, Math.Min(200, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Reverb stereo width percentage.
        /// Range: 0 - 100%. Default: 100%.
        /// Controls the stereo spread of the reverb (0 = mono, 100 = full stereo).
        /// </summary>
        public int ReverbStereoWidth
        {
            get => reverbStereoWidth;
            set { reverbStereoWidth = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Reverb wet/dry mix percentage.
        /// Range: 0 - 100%. Default: 50%.
        /// Controls the balance between dry (original) and wet (reverb) signal.
        /// 0% = fully dry (no reverb audible), 50% = equal blend, 100% = fully wet.
        /// Applied as a crossfade on top of WetGain/DryGain dB controls.
        /// </summary>
        public int ReverbMix
        {
            get => reverbMix;
            set { reverbMix = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Enable makeup gain (output amplification).
        /// Only works when LiveEffectsEnabled is true.
        /// </summary>
        public bool MakeupGainEnabled
        {
            get => makeupGainEnabled;
            set { makeupGainEnabled = value; OnPropertyChanged(); ClearStylePresetIfActive(); }
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
            set { makeupGain = Math.Max(-6, Math.Min(12, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Enable slow effect (reduces playback speed with proportional pitch drop).
        /// Only works when LiveEffectsEnabled is true.
        /// Creates a vinyl-slowdown aesthetic by resampling audio at a lower rate.
        /// </summary>
        public bool SlowEnabled
        {
            get => slowEnabled;
            set { slowEnabled = value; OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Slow effect amount as a percentage (0-50%).
        /// Maps to speed multiplier: speed = 1.0 - (SlowAmount / 100.0)
        /// 0% = normal speed (1.0x), 25% = 0.75x, 50% = 0.5x (half speed).
        /// </summary>
        public int SlowAmount
        {
            get => slowAmount;
            set { slowAmount = Math.Max(0, Math.Min(50, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        // ===== Stereo Widener =====

        public bool StereoWidenerEnabled
        {
            get => stereoWidenerEnabled;
            set { stereoWidenerEnabled = value; OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Stereo width. 0 = mono, 50 = normal (unchanged), 100 = maximum widening.
        /// </summary>
        public int StereoWidenerWidth
        {
            get => stereoWidenerWidth;
            set { stereoWidenerWidth = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        // ===== Chorus =====

        public bool ChorusEnabled
        {
            get => chorusEnabled;
            set { chorusEnabled = value; OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Chorus LFO rate in tenths of Hz (1-50 → 0.1-5.0 Hz).
        /// </summary>
        public int ChorusRate
        {
            get => chorusRate;
            set { chorusRate = Math.Max(1, Math.Min(50, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Chorus modulation depth percentage. Higher = more pitch wobble.
        /// </summary>
        public int ChorusDepth
        {
            get => chorusDepth;
            set { chorusDepth = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Chorus wet/dry mix. 0% = dry only, 100% = wet only.
        /// </summary>
        public int ChorusMix
        {
            get => chorusMix;
            set { chorusMix = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        // ===== Bitcrusher =====

        public bool BitcrusherEnabled
        {
            get => bitcrusherEnabled;
            set { bitcrusherEnabled = value; OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Bit depth for quantization (2-16). Lower = more distortion.
        /// </summary>
        public int BitcrusherBitDepth
        {
            get => bitcrusherBitDepth;
            set { bitcrusherBitDepth = Math.Max(2, Math.Min(16, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Sample rate reduction factor (1-32). 1 = no reduction. Higher = grittier.
        /// </summary>
        public int BitcrusherDownsample
        {
            get => bitcrusherDownsample;
            set { bitcrusherDownsample = Math.Max(1, Math.Min(32, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        // ===== Tremolo =====

        public bool TremoloEnabled
        {
            get => tremoloEnabled;
            set { tremoloEnabled = value; OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Tremolo LFO rate in tenths of Hz (1-100 → 0.1-10.0 Hz).
        /// </summary>
        public int TremoloRate
        {
            get => tremoloRate;
            set { tremoloRate = Math.Max(1, Math.Min(100, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Tremolo modulation depth. 0% = no effect, 100% = full volume swing.
        /// </summary>
        public int TremoloDepth
        {
            get => tremoloDepth;
            set { tremoloDepth = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); ClearStylePresetIfActive(); }
        }

        /// <summary>
        /// Currently selected style preset.
        /// Applies a complete configuration of all live effects (HP, LP, reverb, slow, gain).
        /// Selecting a style overrides all individual effect settings.
        /// </summary>
        public StylePreset SelectedStylePreset
        {
            get => selectedStylePreset;
            set
            {
                if (selectedStylePreset != value)
                {
                    selectedStylePreset = value;
                    OnPropertyChanged();
                    if (value != StylePreset.None)
                    {
                        ApplyStylePreset(value);
                    }
                }
            }
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
                    ClearStylePresetIfActive();
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

        // ===== Spectrum Visualizer Tuning (Experimental) =====

        /// <summary>
        /// Minimum bar opacity percentage at idle (0-100). Controls brightness modulation range.
        /// 0 = bars fade to invisible, 100 = no opacity change.
        /// </summary>
        public int VizOpacityMin
        {
            get => vizOpacityMin;
            set { vizOpacityMin = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Global gain boost/cut in percent (-50 to +100). Applied on top of per-bar A-weighting.
        /// Positive = bars jump higher, negative = more subtle.
        /// </summary>
        public int VizBarGainBoost
        {
            get => vizBarGainBoost;
            set { vizBarGainBoost = Math.Max(-50, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Peak hold duration in milliseconds (0-300). Bars hold at peak this long before falling. Default: 80
        /// </summary>
        public int VizPeakHoldMs
        {
            get => vizPeakHoldMs;
            set { vizPeakHoldMs = Math.Max(0, Math.Min(300, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Base gravity in tenths (10-200). Controls how fast bars fall after peak hold. 80 = 8.0. Default: 80
        /// </summary>
        public int VizGravity
        {
            get => vizGravity;
            set { vizGravity = Math.Max(10, Math.Min(200, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Bass/Treble gravity contrast (0-100). 0 = all bars fall same speed. 100 = bass snappy, treble floaty. Default: 50
        /// </summary>
        public int VizBassGravityBias
        {
            get => vizBassGravityBias;
            set { vizBassGravityBias = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// FFT window size for spectrum analysis (512, 1024, or 2048).
        /// Higher = better frequency resolution but slower temporal response.
        /// 512: ~86Hz/bin, ~11.6ms window — snappy but coarse bass
        /// 1024: ~43Hz/bin, ~23ms window — balanced
        /// 2048: ~21.5Hz/bin, ~46ms window — precise but slower attack
        /// Requires song restart to take effect.
        /// </summary>
        public int VizFftSize
        {
            get => vizFftSize;
            set
            {
                // Only allow valid FFT sizes
                if (value <= 512) vizFftSize = 512;
                else if (value <= 1024) vizFftSize = 1024;
                else vizFftSize = 2048;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Bass gain multiplier (0-200%). Scales the gain applied to bars 0-5 (sub-bass through vocal presence).
        /// Lower = bass bars shorter. Higher = bass bars taller. Default: 100%
        /// </summary>
        public int VizBassGain
        {
            get => vizBassGain;
            set { vizBassGain = Math.Max(0, Math.Min(200, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Treble gain multiplier (0-200%). Scales the gain applied to bars 6-11 (upper vocal through high treble).
        /// Lower = treble bars shorter. Higher = treble bars taller. Default: 100%
        /// </summary>
        public int VizTrebleGain
        {
            get => vizTrebleGain;
            set { vizTrebleGain = Math.Max(0, Math.Min(200, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Frequency bleed amount (0-200%). Scales how much energy bleeds between adjacent bars.
        /// 0 = no bleed (bars independent). 100 = default. 200 = heavy coupling. Default: 100%
        /// </summary>
        public int VizBleedAmount
        {
            get => vizBleedAmount;
            set { vizBleedAmount = Math.Max(0, Math.Min(200, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// Soft-knee compression strength (0-100%). Controls how aggressively peaks are tamed.
        /// 0 = no compression (linear). 50 = moderate. 100 = heavy compression. Default: 50%
        /// </summary>
        public int VizCompression
        {
            get => vizCompression;
            set { vizCompression = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// UI smoothing rise alpha (0-100%). How quickly bars respond to rising energy.
        /// Higher = snappier beat attack. Lower = sluggish rise. Default: 85%
        /// </summary>
        public int VizSmoothRise
        {
            get => vizSmoothRise;
            set { vizSmoothRise = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// UI smoothing fall alpha (0-100%). How quickly bars decay after energy drops.
        /// Lower = smoother trailing. Higher = snappy drop. Default: 15%
        /// </summary>
        public int VizSmoothFall
        {
            get => vizSmoothFall;
            set { vizSmoothFall = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// FFT rise alpha for bass bins (0-95). Controls how quickly low-frequency FFT bins
        /// respond to rising energy. Higher = snappier bass attack. Default: 88
        /// </summary>
        public int VizFftRiseLow
        {
            get => vizFftRiseLow;
            set { vizFftRiseLow = Math.Max(0, Math.Min(95, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// FFT rise alpha for treble bins (0-95). Controls how quickly high-frequency FFT bins
        /// respond to rising energy. Higher = snappier treble attack. Default: 93
        /// </summary>
        public int VizFftRiseHigh
        {
            get => vizFftRiseHigh;
            set { vizFftRiseHigh = Math.Max(0, Math.Min(95, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// FFT fall alpha for bass bins (0-95). Controls how quickly low-frequency FFT bins
        /// decay after energy drops. Higher = faster decay, lower = smoother trailing. Default: 50
        /// </summary>
        public int VizFftFallLow
        {
            get => vizFftFallLow;
            set { vizFftFallLow = Math.Max(0, Math.Min(95, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// FFT fall alpha for treble bins (0-95). Controls how quickly high-frequency FFT bins
        /// decay after energy drops. Higher = faster decay, lower = smoother trailing. Default: 65
        /// </summary>
        public int VizFftFallHigh
        {
            get => vizFftFallHigh;
            set { vizFftFallHigh = Math.Max(0, Math.Min(95, value)); OnPropertyChanged(); }
        }

        /// <summary>
        /// FFT timing mode. False = signal-based (wakes on new audio data, ~43fps at 1024 samples).
        /// True = fixed 16ms timer (~62fps, matches UI refresh rate). Timer mode uses slightly more
        /// CPU but provides consistent update rate that matches CompositionTarget.Rendering.
        /// </summary>
        public bool VizFftTimerMode
        {
            get => vizFftTimerMode;
            set { vizFftTimerMode = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Bar color theme for the spectrum visualizer.
        /// Classic (0) uses the original solid white brush. Other themes use colored gradients.
        /// </summary>
        public int VizColorTheme
        {
            get => vizColorTheme;
            set { vizColorTheme = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// When true, non-Classic themes use a bottom-to-top gradient.
        /// When false, bars use a solid color from the theme's primary color.
        /// </summary>
        public bool VizGradientEnabled
        {
            get => vizGradientEnabled;
            set { vizGradientEnabled = value; OnPropertyChanged(); }
        }

        // Dynamic theme colors — extracted from game background image at runtime.
        // Defaults to Classic white; updated by GameColorExtractor on game selection.
        [JsonIgnore]
        public Color DynamicColorBottom
        {
            get => _dynamicColorBottom;
            set { _dynamicColorBottom = value; OnPropertyChanged(); }
        }
        private Color _dynamicColorBottom = Color.FromArgb(200, 255, 255, 255);

        [JsonIgnore]
        public Color DynamicColorTop
        {
            get => _dynamicColorTop;
            set { _dynamicColorTop = value; OnPropertyChanged(); }
        }
        private Color _dynamicColorTop = Color.FromArgb(200, 255, 255, 255);

        // Dynamic color extraction tuning — user-configurable via sliders
        public int DynMinBrightnessBottom
        {
            get => dynMinBrightnessBottom;
            set { dynMinBrightnessBottom = value; OnPropertyChanged(); }
        }

        public int DynMinBrightnessTop
        {
            get => dynMinBrightnessTop;
            set { dynMinBrightnessTop = value; OnPropertyChanged(); }
        }

        public int DynMinSatBottom
        {
            get => dynMinSatBottom;
            set { dynMinSatBottom = value; OnPropertyChanged(); }
        }

        public int DynMinSatTop
        {
            get => dynMinSatTop;
            set { dynMinSatTop = value; OnPropertyChanged(); }
        }

        public VizPreset SelectedVizPreset
        {
            get => selectedVizPreset;
            set { selectedVizPreset = value; OnPropertyChanged(); }
        }

        // Spectrum Visualizer Tuning (defaults match Punchy preset)
        private int vizOpacityMin = 20;          // 0-100 (%) — idle bar opacity
        private int vizBarGainBoost = 0;         // -50 to +100 (%) — global gain offset
        private int vizPeakHoldMs = 40;          // 0-300 ms — how long bars hold at peak
        private int vizGravity = 160;            // 10-200 — base gravity (tenths, 160 = 16.0)
        private int vizBassGravityBias = 70;     // 0-100 — bass/treble gravity spread (0=uniform, 100=max contrast)
        private int vizFftSize = 1024;           // 512, 1024, or 2048 — FFT window size (requires restart)
        private int vizBassGain = 110;           // 0-200 (%) — scales gain for bass bars (0-5)
        private int vizTrebleGain = 90;          // 0-200 (%) — scales gain for treble bars (6-11)
        private int vizBleedAmount = 60;         // 0-200 (%) — scales frequency bleed between bars
        private int vizCompression = 35;         // 0-100 (%) — soft-knee compression strength
        private int vizSmoothRise = 95;          // 0-100 (%) — UI smoothing rise speed (higher = snappier attack)
        private int vizSmoothFall = 30;          // 0-100 (%) — UI smoothing fall speed (lower = smoother decay)
        private int vizFftRiseLow = 92;          // 0-95 — FFT rise alpha for bass bins (mapped to 0.00-0.95)
        private int vizFftRiseHigh = 95;         // 0-95 — FFT rise alpha for treble bins
        private int vizFftFallLow = 55;          // 0-95 — FFT fall alpha for bass bins
        private int vizFftFallHigh = 70;         // 0-95 — FFT fall alpha for treble bins
        private bool vizFftTimerMode = false;    // false = signal-based (audio-driven), true = fixed 16ms timer (~62fps)
        private VizPreset selectedVizPreset = VizPreset.Punchy; // Current visualizer preset
        private int vizColorTheme = 0;               // VizColorTheme enum — bar color theme (0=Dynamic)
        private bool vizGradientEnabled = true;      // true = gradient bars, false = solid color
        private int dynMinBrightnessBottom = 200;    // 0-255 — min brightness floor for bottom (gradient base) color
        private int dynMinBrightnessTop = 150;       // 0-255 — min brightness floor for top (gradient tip) color
        private int dynMinSatBottom = 30;            // 0-100 (%) — min saturation for bottom color
        private int dynMinSatTop = 35;               // 0-100 (%) — min saturation for top color


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

                // ===== UniPlaySong Custom Presets - Creative/Stylistic =====

                case ReverbPreset.Dreamy:
                    // Dreamy - ethereal, lush wash for ambient/chill vibes
                    // Rich reverb engine, mix controls how much washes over the dry signal
                    ReverbRoomSize = 85;
                    ReverbPreDelay = 30;
                    ReverbReverberance = 75;
                    ReverbDamping = 60;
                    ReverbToneLow = 85;
                    ReverbToneHigh = 35;
                    ReverbWetGain = 0;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 100;
                    ReverbMix = 30;
                    break;

                case ReverbPreset.LoFi:
                    // Lo-Fi - muffled, tape-like warmth
                    // Dark, damped reverb blended in subtly for texture
                    ReverbRoomSize = 45;
                    ReverbPreDelay = 8;
                    ReverbReverberance = 40;
                    ReverbDamping = 85;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 10;
                    ReverbWetGain = 0;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 45;
                    ReverbMix = 25;
                    break;

                case ReverbPreset.Underwater:
                    // Underwater - deep, submerged, surreal atmosphere
                    // Full reverb character with heavy damping, mix pushes you under
                    ReverbRoomSize = 90;
                    ReverbPreDelay = 40;
                    ReverbReverberance = 75;
                    ReverbDamping = 95;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 5;
                    ReverbWetGain = 2;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 80;
                    ReverbMix = 45;
                    break;

                case ReverbPreset.SciFiCorridor:
                    // Sci-Fi Corridor - metallic, spacious, futuristic echo
                    // Bright, undamped reflections with long pre-delay for distinct echoes
                    ReverbRoomSize = 70;
                    ReverbPreDelay = 80;
                    ReverbReverberance = 50;
                    ReverbDamping = 10;
                    ReverbToneLow = 25;
                    ReverbToneHigh = 95;
                    ReverbWetGain = 0;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 95;
                    ReverbMix = 30;
                    break;

                case ReverbPreset.VinylWarmth:
                    // Vinyl Warmth - subtle warm coloring, like vinyl playback
                    // Small, dark room character, low mix for gentle texture
                    ReverbRoomSize = 30;
                    ReverbPreDelay = 5;
                    ReverbReverberance = 30;
                    ReverbDamping = 75;
                    ReverbToneLow = 95;
                    ReverbToneHigh = 20;
                    ReverbWetGain = 0;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 40;
                    ReverbMix = 20;
                    break;

                case ReverbPreset.Vaporwave:
                    // Vaporwave - the classic slowed+reverb aesthetic
                    // Big lush room cranked up, mix brings it to a dreamy blend
                    ReverbRoomSize = 90;
                    ReverbPreDelay = 25;
                    ReverbReverberance = 60;
                    ReverbDamping = 45;
                    ReverbToneLow = 80;
                    ReverbToneHigh = 40;
                    ReverbWetGain = 2;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 100;
                    ReverbMix = 35;
                    break;

                case ReverbPreset.Custom:
                default:
                    // Custom - don't change anything
                    break;
            }
        }

        /// <summary>
        /// When the user manually changes any individual effect parameter,
        /// clear the style preset back to None so it doesn't re-apply on settings reload.
        /// Suppressed during ApplyStylePreset so the preset's own property changes don't trigger clearing.
        /// </summary>
        private void ClearStylePresetIfActive()
        {
            if (!_applyingStylePreset && selectedStylePreset != StylePreset.None)
            {
                selectedStylePreset = StylePreset.None;
                OnPropertyChanged(nameof(SelectedStylePreset));
            }
        }

        public void ApplyStylePreset(StylePreset style)
        {
            _applyingStylePreset = true;
            try
            {
            switch (style)
            {
                case StylePreset.HuddiniRehearsal:
                    // Huddini Style — wide stereo, rich reverb, live rehearsal room
                    // Full-range with gentle filtering, high reverb mix for immersion
                    HighPassEnabled = true;
                    HighPassCutoff = 145;
                    LowPassEnabled = true;
                    LowPassCutoff = 7870;
                    ReverbEnabled = true;
                    ReverbRoomSize = 80;
                    ReverbReverberance = 65;
                    ReverbDamping = 35;
                    ReverbPreDelay = 22;
                    ReverbToneLow = 95;
                    ReverbToneHigh = 75;
                    ReverbWetGain = -1;
                    ReverbDryGain = -3;
                    ReverbStereoWidth = 96;
                    ReverbMix = 50;
                    selectedReverbPreset = ReverbPreset.Custom;
                    OnPropertyChanged(nameof(SelectedReverbPreset));
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 58;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 0;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.HuddiniBrightRoom:
                    // Huddini Style — open, spacious large room with bright tone
                    // Wide stereo, LargeRoom reverb at moderate mix
                    HighPassEnabled = true;
                    HighPassCutoff = 180;
                    LowPassEnabled = false;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.LargeRoom;
                    ReverbToneHigh = 49;
                    ReverbMix = 40;
                    selectedReverbPreset = ReverbPreset.Custom;
                    OnPropertyChanged(nameof(SelectedReverbPreset));
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 72;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 0;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.HuddiniRetroRadio:
                    // Huddini Style — narrow bandpass, bathroom reverb, bitcrushed
                    // Old AM radio — tight band, collapsed stereo, gritty digital
                    HighPassEnabled = true;
                    HighPassCutoff = 636;
                    LowPassEnabled = true;
                    LowPassCutoff = 3530;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.Bathroom;
                    ReverbWetGain = -3;
                    ReverbMix = 26;
                    selectedReverbPreset = ReverbPreset.Custom;
                    OnPropertyChanged(nameof(SelectedReverbPreset));
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 24;
                    ChorusEnabled = false;
                    BitcrusherEnabled = true;
                    BitcrusherBitDepth = 9;
                    BitcrusherDownsample = 2;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 2;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.HuddiniLoBit:
                    // Huddini Style — dark lo-fi, heavy bitcrusher, narrow and gritty
                    // LoFi reverb for warmth, aggressive downsample for crunch
                    HighPassEnabled = true;
                    HighPassCutoff = 509;
                    LowPassEnabled = true;
                    LowPassCutoff = 5445;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.LoFi;
                    ReverbMix = 35;
                    SlowEnabled = false;
                    StereoWidenerEnabled = false;
                    ChorusEnabled = false;
                    BitcrusherEnabled = true;
                    BitcrusherBitDepth = 12;
                    BitcrusherDownsample = 3;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 2;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.HuddiniSlowedDream:
                    // Huddini Style — deep slow with vaporwave reverb, wide and dreamy
                    // Gentle slowdown with lush reverb and wide stereo, subdued gain
                    HighPassEnabled = false;
                    HighPassCutoff = 200;
                    LowPassEnabled = false;
                    LowPassCutoff = 1270;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.Vaporwave;
                    // Override reverb params from Vaporwave preset
                    ReverbRoomSize = 90;
                    ReverbReverberance = 60;
                    ReverbDamping = 45;
                    ReverbPreDelay = 25;
                    ReverbToneLow = 80;
                    ReverbToneHigh = 40;
                    ReverbWetGain = 2;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 100;
                    ReverbMix = 35;
                    selectedReverbPreset = ReverbPreset.Custom;
                    OnPropertyChanged(nameof(SelectedReverbPreset));
                    SlowEnabled = true;
                    SlowAmount = 25;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 65;
                    ChorusEnabled = false;
                    ChorusRate = 2;
                    ChorusDepth = 30;
                    ChorusMix = 20;
                    BitcrusherEnabled = false;
                    BitcrusherBitDepth = 14;
                    BitcrusherDownsample = 1;
                    TremoloEnabled = false;
                    TremoloRate = 10;
                    TremoloDepth = 25;
                    MakeupGainEnabled = true;
                    MakeupGain = -2;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.HuddiniCaveLake:
                    // Huddini Style — submerged cave, heavy slow, underwater reverb
                    // Chorus adds murky pitch drift, LP-first chain for deep filtering
                    HighPassEnabled = true;
                    HighPassCutoff = 191;
                    LowPassEnabled = true;
                    LowPassCutoff = 1270;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.Underwater;
                    ReverbMix = 60;
                    SlowEnabled = true;
                    SlowAmount = 40;
                    StereoWidenerEnabled = false;
                    ChorusEnabled = true;
                    ChorusRate = 2;
                    ChorusDepth = 30;
                    ChorusMix = 20;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = -1;
                    EffectChainPreset = EffectChainPreset.LowPassFirst;
                    break;

                case StylePreset.HuddiniHoneyRoom:
                    // Huddini Style — bright metallic reverb, subtle bitcrush, wide
                    // Sci-fi corridor with boosted wet, reverb-first chain for shimmer
                    HighPassEnabled = true;
                    HighPassCutoff = 200;
                    LowPassEnabled = false;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.SciFiCorridor;
                    ReverbDamping = 24;
                    ReverbWetGain = 1;
                    ReverbMix = 40;
                    selectedReverbPreset = ReverbPreset.Custom;
                    OnPropertyChanged(nameof(SelectedReverbPreset));
                    SlowEnabled = true;
                    SlowAmount = 4;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 65;
                    ChorusEnabled = false;
                    BitcrusherEnabled = true;
                    BitcrusherBitDepth = 14;
                    BitcrusherDownsample = 1;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 2;
                    EffectChainPreset = EffectChainPreset.ReverbFirst;
                    break;

                case StylePreset.CleanBoost:
                    // Volume boost + stereo widening — makes everything bigger
                    HighPassEnabled = false;
                    LowPassEnabled = false;
                    ReverbEnabled = false;
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 62;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 6;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.WarmFMRadio:
                    // Cozy radio broadcast — soft top end, lounge cafe warmth
                    // Chorus for analog broadcast texture, makeup gain for presence
                    HighPassEnabled = false;
                    LowPassEnabled = true;
                    LowPassCutoff = 5500;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.LoungeCafe;
                    ReverbMix = 30;
                    SlowEnabled = false;
                    StereoWidenerEnabled = false;
                    ChorusEnabled = true;
                    ChorusRate = 4;
                    ChorusDepth = 12;
                    ChorusMix = 12;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 2;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.BrightAiry:
                    // Crisp, open, spacious — bass cleaned up, wide stereo field
                    // Large room reverb with bright tone, high mix for atmosphere
                    HighPassEnabled = true;
                    HighPassCutoff = 180;
                    LowPassEnabled = false;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.LargeRoom;
                    ReverbMix = 40;
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 72;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 2;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.Telephone:
                    // Tinny phone speaker — narrow band, collapsed stereo, bit-reduced
                    // Bitcrusher degrades quality, narrow width sells the phone speaker
                    HighPassEnabled = true;
                    HighPassCutoff = 500;
                    LowPassEnabled = true;
                    LowPassCutoff = 3000;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.Bathroom;
                    ReverbMix = 10;
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 10;
                    ChorusEnabled = false;
                    BitcrusherEnabled = true;
                    BitcrusherBitDepth = 10;
                    BitcrusherDownsample = 1;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 3;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.ConcertLive:
                    // In the audience — big hall, wide stereo, full range
                    // Concert hall at high mix, widener for immersion
                    HighPassEnabled = true;
                    HighPassCutoff = 80;
                    LowPassEnabled = false;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.ConcertHall;
                    ReverbMix = 45;
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 75;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 2;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.MuffledNextRoom:
                    // Music bleeding through a wall — heavy LP, dark room, quiet
                    // Very narrow stereo, high reverb mix to sell the distance
                    HighPassEnabled = true;
                    HighPassCutoff = 100;
                    LowPassEnabled = true;
                    LowPassCutoff = 1200;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.SmallRoomDark;
                    ReverbMix = 55;
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 15;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = -5;
                    EffectChainPreset = EffectChainPreset.LowPassFirst;
                    break;

                case StylePreset.LoFiChill:
                    // Lo-fi study beats — dark, warm, gritty
                    // Bitcrusher for texture, LoFi reverb at low mix for warmth
                    HighPassEnabled = true;
                    HighPassCutoff = 120;
                    LowPassEnabled = true;
                    LowPassCutoff = 3500;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.LoFi;
                    ReverbMix = 35;
                    SlowEnabled = true;
                    SlowAmount = 6;
                    StereoWidenerEnabled = false;
                    ChorusEnabled = false;
                    BitcrusherEnabled = true;
                    BitcrusherBitDepth = 12;
                    BitcrusherDownsample = 2;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 2;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.SlowedReverb:
                    // The classic vaporwave aesthetic — slowed with lush reverb
                    // High reverb mix is the point, widener for dreamy space
                    HighPassEnabled = false;
                    LowPassEnabled = false;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.Vaporwave;
                    ReverbMix = 55;
                    SlowEnabled = true;
                    SlowAmount = 12;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 68;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = false;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.VinylNight:
                    // Old record player — warm, gentle, slightly slowed
                    // Chorus for analog pitch drift, vinyl reverb kept subtle
                    HighPassEnabled = false;
                    LowPassEnabled = true;
                    LowPassCutoff = 5000;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.VinylWarmth;
                    ReverbMix = 30;
                    SlowEnabled = true;
                    SlowAmount = 5;
                    StereoWidenerEnabled = false;
                    ChorusEnabled = true;
                    ChorusRate = 2;
                    ChorusDepth = 18;
                    ChorusMix = 18;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = false;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.UnderwaterDream:
                    // Submerged — deep, murky, everything filtered and distant
                    // Underwater reverb cranked high, chorus for pitch drift
                    HighPassEnabled = true;
                    HighPassCutoff = 150;
                    LowPassEnabled = true;
                    LowPassCutoff = 1800;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.Underwater;
                    ReverbMix = 60;
                    SlowEnabled = true;
                    SlowAmount = 10;
                    StereoWidenerEnabled = false;
                    ChorusEnabled = true;
                    ChorusRate = 2;
                    ChorusDepth = 30;
                    ChorusMix = 20;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = -3;
                    EffectChainPreset = EffectChainPreset.LowPassFirst;
                    break;

                case StylePreset.Cyberpunk:
                    // Neon dystopia — bright metallic reverb, digital edge
                    // Sci-fi corridor at high mix, bitcrusher for digital grit
                    HighPassEnabled = true;
                    HighPassCutoff = 200;
                    LowPassEnabled = false;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.SciFiCorridor;
                    ReverbMix = 40;
                    SlowEnabled = true;
                    SlowAmount = 4;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 78;
                    ChorusEnabled = false;
                    BitcrusherEnabled = true;
                    BitcrusherBitDepth = 13;
                    BitcrusherDownsample = 1;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 2;
                    EffectChainPreset = EffectChainPreset.ReverbFirst;
                    break;

                case StylePreset.None:
                default:
                    // None - don't change anything
                    break;
            }
            }
            finally
            {
                _applyingStylePreset = false;
            }
        }
    }
}
