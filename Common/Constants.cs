using System;
using System.Collections.Generic;

namespace UniPlaySong.Common
{
    /// <summary>
    /// Centralized constants for UniPlaySong extension
    /// </summary>
    public static class Constants
    {
        #region Fade Durations
        
        /// <summary>
        /// Default fade-in duration in seconds
        /// </summary>
        public const double DefaultFadeInDuration = 0.5;
        
        /// <summary>
        /// Default fade-out duration in seconds
        /// </summary>
        public const double DefaultFadeOutDuration = 0.3;
        
        /// <summary>
        /// Minimum fade duration in seconds
        /// </summary>
        public const double MinFadeDuration = 0.05;
        
        /// <summary>
        /// Maximum fade duration in seconds
        /// </summary>
        public const double MaxFadeDuration = 10.0;
        
        #endregion
        
        #region Volume
        
        /// <summary>
        /// Default target volume (0.0 to 1.0)
        /// </summary>
        public const double DefaultTargetVolume = 0.5;
        
        /// <summary>
        /// Minimum volume (0.0)
        /// </summary>
        public const double MinVolume = 0.0;
        
        /// <summary>
        /// Maximum volume (1.0)
        /// </summary>
        public const double MaxVolume = 1.0;
        
        /// <summary>
        /// Divisor to convert percentage volume (0-100) to decimal (0.0-1.0)
        /// </summary>
        public const double VolumeDivisor = 100.0;
        
        /// <summary>
        /// Default music volume percentage (0-100)
        /// </summary>
        public const int DefaultMusicVolume = 50;
        
        /// <summary>
        /// Minimum music volume percentage (0-100)
        /// </summary>
        public const int MinMusicVolume = 0;
        
        /// <summary>
        /// Maximum music volume percentage (0-100)
        /// </summary>
        public const int MaxMusicVolume = 100;
        
        #endregion

        #region Preview Duration

        /// <summary>
        /// Default preview duration in seconds for game music
        /// </summary>
        public const int DefaultPreviewDuration = 30;

        /// <summary>
        /// Minimum preview duration in seconds
        /// </summary>
        public const int MinPreviewDuration = 15;

        /// <summary>
        /// Maximum preview duration in seconds (5 minutes)
        /// </summary>
        public const int MaxPreviewDuration = 300;

        #endregion

        #region File Extensions
        
        /// <summary>
        /// Supported audio file extensions
        /// </summary>
        public static readonly string[] SupportedAudioExtensions = 
        {
            ".mp3", ".wav", ".flac", ".wma", ".aif", ".m4a", ".aac", ".mid"
        };
        
        #endregion
        
        #region Directory Names
        
        /// <summary>
        /// ExtraMetadata folder name in Playnite configuration
        /// </summary>
        public const string ExtraMetadataFolderName = "ExtraMetadata";
        
        /// <summary>
        /// Extension folder name
        /// </summary>
        public const string ExtensionFolderName = "UniPlaySong";
        
        /// <summary>
        /// Games music folder name
        /// </summary>
        public const string GamesFolderName = "Games";
        
        /// <summary>
        /// Temporary files folder name
        /// </summary>
        public const string TempFolderName = "Temp";
        
        /// <summary>
        /// Default music folder name
        /// </summary>
        public const string DefaultMusicFolderName = "DefaultMusic";
        
        /// <summary>
        /// Preserved originals folder name for original files moved during normalization
        /// </summary>
        public const string PreservedOriginalsFolderName = "PreservedOriginals";
        
        /// <summary>
        /// Playnite application folder name
        /// </summary>
        public const string PlayniteFolderName = "Playnite";
        
        /// <summary>
        /// Playnite extensions folder name
        /// </summary>
        public const string PlayniteExtensionsFolderName = "Extensions";
        
        #endregion
        
        #region File Names
        
        /// <summary>
        /// Log file name
        /// </summary>
        public const string LogFileName = "UniPlaySong.log";

        /// <summary>
        /// Downloader log file name - dedicated log for download operations
        /// </summary>
        public const string DownloaderLogFileName = "downloader.log";
        
        #endregion
        
        #region UI
        
        /// <summary>
        /// Menu section name for game menu items
        /// </summary>
        public const string MenuSectionName = "UniPlaySong";
        
        #endregion
        
        #region Download Settings

        /// <summary>
        /// Maximum preview song length in minutes (used for scoring - songs longer than this get penalized)
        /// </summary>
        public const int MaxPreviewSongLengthMinutes = 8;

        /// <summary>
        /// Maximum allowed song duration in minutes. Songs exceeding this are rejected entirely.
        /// This prevents downloading gameplay videos, streams, or full album compilations.
        /// </summary>
        public const int MaxAllowedSongDurationMinutes = 30;

        /// <summary>
        /// Preferred song name endings for selection
        /// </summary>
        public static readonly List<string> PreferredSongEndings = new List<string>
        {
            "Theme",
            "Title",
            "Menu",
            "Main Theme"
        };

        #endregion

        #region Music Status Tags

        /// <summary>
        /// Tag name for games that have music downloaded
        /// </summary>
        public const string TagHasMusic = "[UPS] Has Music";

        /// <summary>
        /// Tag name for games that do not have music downloaded
        /// </summary>
        public const string TagNoMusic = "[UPS] No Music";

        #endregion
    }
}

