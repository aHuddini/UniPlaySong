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

        private static readonly string[] SupportedExtensions = { ".mp3", ".ogg", ".wav", ".flac" };

        // API reference for active theme lookup (set once during plugin init)
        private static IPlayniteAPI _api;

        // Cached active theme music path (resolved on first access after API is set)
        private static string _activeThemeMusicPath;
        private static bool _activeThemeMusicScanned;

        // Static constructor runs once when class is first accessed
        static PlayniteThemeHelper()
        {
            _nativeMusicPath = ScanForNativeMusicFile();
        }

        // Call once during plugin initialization to enable active theme lookup
        public static void Initialize(IPlayniteAPI api)
        {
            _api = api;
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

                foreach (var ext in SupportedExtensions)
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

        // Finds the background music file for the currently active fullscreen theme.
        // Scans both user themes (%AppData%/Roaming/Playnite/Themes/Fullscreen/)
        // and program themes (%LocalAppData%/Playnite/Themes/Fullscreen/) directories.
        // Result is cached after first scan.
        public static string FindActiveThemeMusicFile()
        {
            if (_activeThemeMusicScanned)
                return _activeThemeMusicPath;

            _activeThemeMusicScanned = true;
            _activeThemeMusicPath = ScanForActiveThemeMusicFile();
            return _activeThemeMusicPath;
        }

        private static string ScanForActiveThemeMusicFile()
        {
            try
            {
                // Get the active fullscreen theme ID.
                // Not exposed in SDK — read from fullscreenConfig.json or reflect through the settings wrapper.
                var themeId = GetActiveFullscreenThemeId();
                if (string.IsNullOrWhiteSpace(themeId)) return null;

                // Search user themes directory first (Roaming), then program themes (Local)
                var roamingPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var localPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                var searchDirs = new[]
                {
                    Path.Combine(roamingPath, "Playnite", "Themes", "Fullscreen"),
                    Path.Combine(localPath, "Playnite", "Themes", "Fullscreen")
                };

                foreach (var searchDir in searchDirs)
                {
                    if (!Directory.Exists(searchDir)) continue;

                    foreach (var dir in Directory.GetDirectories(searchDir))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (!string.Equals(dirName, themeId, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var audioDir = Path.Combine(dir, "audio");
                        if (!Directory.Exists(audioDir)) continue;

                        foreach (var ext in SupportedExtensions)
                        {
                            var filePath = Path.Combine(audioDir, $"background{ext}");
                            if (File.Exists(filePath))
                            {
                                return filePath;
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                LogManager.GetLogger()?.Error(ex, "PlayniteThemeHelper: Error finding active theme music file");
                return null;
            }
        }

        // Gets the active fullscreen theme ID from Playnite's config.
        // Tries reflection first (through the SDK wrapper's inner settings field),
        // falls back to reading fullscreenConfig.json directly.
        private static string GetActiveFullscreenThemeId()
        {
            // Approach 1: Reflect through FullscreenSettingsAPI → inner FullscreenSettings.Theme
            try
            {
                var fullscreenApi = _api?.ApplicationSettings?.Fullscreen;
                if (fullscreenApi != null)
                {
                    var settingsField = fullscreenApi.GetType()
                        .GetField("settings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (settingsField != null)
                    {
                        var innerSettings = settingsField.GetValue(fullscreenApi);
                        var themeId = innerSettings?.GetType()
                            .GetProperty("Theme")
                            ?.GetValue(innerSettings) as string;
                        if (!string.IsNullOrWhiteSpace(themeId))
                            return themeId;
                    }
                }
            }
            catch { }

            // Approach 2: Read from fullscreenConfig.json
            try
            {
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Playnite", "fullscreenConfig.json");
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    // Simple extraction — avoid adding a JSON dependency just for this
                    var key = "\"Theme\"";
                    var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var colonIdx = json.IndexOf(':', idx + key.Length);
                        var quoteStart = json.IndexOf('"', colonIdx + 1);
                        var quoteEnd = json.IndexOf('"', quoteStart + 1);
                        if (quoteStart >= 0 && quoteEnd > quoteStart)
                        {
                            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                        }
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
