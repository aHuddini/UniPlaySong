using System;
using System.IO;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    // Finds Playnite's default background music file
    public static class PlayniteThemeHelper
    {
        // Cached on class initialization to avoid repeated file I/O
        private static readonly string _nativeMusicPath;

        // Static constructor runs once when class is first accessed
        static PlayniteThemeHelper()
        {
            _nativeMusicPath = ScanForNativeMusicFile();
        }

        // Scans filesystem once at startup to find Playnite's native background music
        private static string ScanForNativeMusicFile()
        {
            try
            {
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

        // Returns the cached native music path (scanned once at startup)
        public static string FindBackgroundMusicFile(IPlayniteAPI api)
        {
            return _nativeMusicPath;
        }
    }
}
