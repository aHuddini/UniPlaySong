namespace UniPlaySong.Common;

static class Constants
{
    #region Folder Structure
    public const string PluginFolderName = "UniPlaySong";
    public const string GamesFolderName = "Games";
    #endregion

    #region Audio
    public static readonly HashSet<string> SupportedExtensions =
        [".mp3", ".wav", ".ogg", ".flac"];
    #endregion

    #region Volume
    public const double VolumeDivisor = 100.0;
    public const int DefaultMusicVolume = 75;
    public const int MinMusicVolume = 0;
    public const int MaxMusicVolume = 100;
    #endregion

    #region Fader
    public const int FaderPollIntervalMs = 50;
    public const double FadeCompleteThreshold = 0.0001;
    public const int DefaultFadeInDurationMs = 1500;
    public const int DefaultFadeOutDurationMs = 1500;
    public const int MinFadeDurationMs = 100;
    public const int MaxFadeDurationMs = 5000;
    #endregion
}
