using System;
using System.IO;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    /// <summary>
    /// Simple helper to find Playnite's default background music file
    /// The file is always at: AppData\Local\Playnite\Themes\Fullscreen\Default\audio\background.*
    /// </summary>
    public static class PlayniteThemeHelper
    {
        /// <summary>
        /// Finds Playnite's default background music file
        /// Simple approach: Just check the known location
        /// </summary>
        /// <param name="api">Playnite API instance (not used, but kept for compatibility)</param>
        /// <returns>Path to background music file, or null if not found</returns>
        public static string FindBackgroundMusicFile(IPlayniteAPI api)
        {
            try
            {
                // Known location: AppData\Local\Playnite\Themes\Fullscreen\Default\audio\background.*
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var audioDir = Path.Combine(localAppData, "Playnite", "Themes", "Fullscreen", "Default", "audio");
                
                if (!Directory.Exists(audioDir))
                {
                    return null;
                }

                // Check for background music files (mp3, ogg, wav, flac)
                string[] extensions = { ".mp3", ".ogg", ".wav", ".flac" };
                foreach (var ext in extensions)
                {
                    var filePath = Path.Combine(audioDir, $"background{ext}");
                    if (File.Exists(filePath))
                    {
                        return filePath;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                LogManager.GetLogger()?.Error(ex, "PlayniteThemeHelper: Error finding background music file");
                return null;
            }
        }
    }
}
