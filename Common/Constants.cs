using System;
using System.Collections.Generic;

namespace UniPlaySong.Common
{
    // Centralized constants for UniPlaySong
    public static class Constants
    {
        #region Fade Durations

        public const double DefaultFadeInDuration = 0.5;
        public const double DefaultFadeOutDuration = 0.3;
        public const double MinFadeDuration = 0.05;
        public const double MaxFadeDuration = 10.0;

        #endregion
        
        #region Volume

        public const double DefaultTargetVolume = 0.5;
        public const double MinVolume = 0.0;
        public const double MaxVolume = 1.0;

        public const double VolumeDivisor = 100.0; // percentage (0-100) to decimal (0.0-1.0)

        public const int DefaultMusicVolume = 50;
        public const int MinMusicVolume = 0;
        public const int MaxMusicVolume = 100;

        #endregion

        #region Preview Duration

        public const int DefaultPreviewDuration = 30;

        public const int MinPreviewDuration = 15;  // seconds
        public const int MaxPreviewDuration = 300; // seconds (5 minutes)

        #endregion

        #region File Extensions

        public const string DefaultAudioExtension = ".mp3";

        public static readonly string[] SupportedAudioExtensions =
        {
            ".mp3", ".wav", ".flac", ".wma", ".aif", ".m4a", ".aac", ".mid"
        };

        #endregion
        
        #region Directory Names

        public const string ExtraMetadataFolderName = "ExtraMetadata";
        public const string ExtensionFolderName = "UniPlaySong";
        public const string GamesFolderName = "Games";
        public const string TempFolderName = "Temp";
        public const string DefaultMusicFolderName = "DefaultMusic";

        public const string PreservedOriginalsFolderName = "PreservedOriginals";
        public const string PlayniteFolderName = "Playnite";
        public const string PlayniteExtensionsFolderName = "Extensions";
        
        #endregion
        
        #region File Names
        
        public const string LogFileName = "UniPlaySong.log";
        public const string DownloaderLogFileName = "downloader.log";
        
        #endregion
        
        #region UI
        
        public const string MenuSectionName = "UniPlaySong";
        
        #endregion
        
        #region Download Settings

        public const int MaxPreviewSongLengthMinutes = 8;  // songs longer than this get penalized in scoring
        public const int MaxAllowedSongDurationMinutes = 30; // songs exceeding this are rejected entirely

        public static readonly List<string> PreferredSongEndings = new List<string>
        {
            "Theme",
            "Title",
            "Menu",
            "Main Theme"
        };

        #endregion

        #region Music Status Tags

        public const string TagHasMusic = "[UPS] Has Music";
        public const string TagNoMusic = "[UPS] No Music";

        #endregion
    }
}

