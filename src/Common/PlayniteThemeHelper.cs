using System;
using System.IO;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    // Status of the Active Theme UPS Audio feature for the currently active fullscreen theme.
    public enum ActiveThemeUpsStatus
    {
        NotApplicable,      // Could not resolve a fullscreen theme (Desktop only, or theme ID lookup failed)
        Ready,              // UPS_BackgroundAudio.* found — UPS will play it
        CanBeCreated,       // No UPS file but background.* exists — settings UI offers a one-click copy
        Unsupported         // Neither file exists — theme dev must add one
    }

    public class ActiveThemeStatusInfo
    {
        public ActiveThemeUpsStatus Status { get; set; }
        public string ThemeName { get; set; }            // Display name from theme.yaml (falls back to dir name)
        public string ThemeDirectory { get; set; }       // Resolved active-theme folder
        public string UpsAudioPath { get; set; }         // Full path if Status == Ready
        public string BackgroundSourcePath { get; set; } // Full path if Status == CanBeCreated
    }

    public class CopyResult
    {
        public bool Success { get; set; }
        public string TargetPath { get; set; }
        public string ErrorMessage { get; set; }
    }

    // Resolves the active fullscreen theme's UPS audio file.
    //
    // v1.5.2: strict UPS_BackgroundAudio.* filename only — no fallback to background.*.
    // Reading background.* directly caused playback glitches because Playnite's built-in
    // SDL player opens the same file simultaneously in fullscreen mode.
    public static class PlayniteThemeHelper
    {
        private static readonly string[] SupportedExtensions = { ".mp3", ".ogg", ".wav", ".flac" };
        private const string UpsAudioBaseName = "UPS_BackgroundAudio";
        private const string LegacyBackgroundBaseName = "background";

        // API reference for active theme lookup (set once during plugin init)
        private static IPlayniteAPI _api;

        // Cached active theme music path (resolved on first access; invalidated by InvalidateCache())
        private static string _activeThemeUpsAudioPath;
        private static bool _activeThemeScanned;

        // Call once during plugin initialization to enable active theme lookup
        public static void Initialize(IPlayniteAPI api)
        {
            _api = api;
        }

        // Returns the active theme's UPS_BackgroundAudio.* path, or null if not present.
        // Does NOT fall back to background.* — that file belongs to Playnite's SDL player.
        public static string FindActiveThemeUpsAudioFile()
        {
            if (_activeThemeScanned)
                return _activeThemeUpsAudioPath;

            _activeThemeScanned = true;
            _activeThemeUpsAudioPath = ScanForUpsAudioFile();
            return _activeThemeUpsAudioPath;
        }

        // Invalidates the cached path so the next FindActiveThemeUpsAudioFile() re-scans.
        // Called after CreateUpsAudioFromBackground() succeeds and when the user
        // changes the active fullscreen theme.
        public static void InvalidateCache()
        {
            _activeThemeScanned = false;
            _activeThemeUpsAudioPath = null;
        }

        // Returns rich status for the settings UI: which state to show, what theme is active,
        // whether a one-click copy is possible.
        public static ActiveThemeStatusInfo GetActiveThemeStatus()
        {
            var info = new ActiveThemeStatusInfo { Status = ActiveThemeUpsStatus.NotApplicable };

            try
            {
                var themeDir = ResolveActiveThemeDirectory();
                if (string.IsNullOrWhiteSpace(themeDir))
                    return info;

                info.ThemeDirectory = themeDir;
                info.ThemeName = ReadThemeDisplayName(themeDir) ?? Path.GetFileName(themeDir);

                var audioDir = Path.Combine(themeDir, "audio");
                if (!Directory.Exists(audioDir))
                {
                    info.Status = ActiveThemeUpsStatus.Unsupported;
                    return info;
                }

                var upsPath = FindFileWithBaseName(audioDir, UpsAudioBaseName);
                if (upsPath != null)
                {
                    info.Status = ActiveThemeUpsStatus.Ready;
                    info.UpsAudioPath = upsPath;
                    return info;
                }

                var backgroundPath = FindFileWithBaseName(audioDir, LegacyBackgroundBaseName);
                if (backgroundPath != null)
                {
                    info.Status = ActiveThemeUpsStatus.CanBeCreated;
                    info.BackgroundSourcePath = backgroundPath;
                    return info;
                }

                info.Status = ActiveThemeUpsStatus.Unsupported;
                return info;
            }
            catch (Exception ex)
            {
                LogManager.GetLogger()?.Error(ex, "PlayniteThemeHelper: Error reading active theme status");
                return info;
            }
        }

        // Copies the active theme's background.{ext} to UPS_BackgroundAudio.{ext} in the
        // same audio folder. User-initiated action surfaced through the settings UI.
        // Source extension is preserved (background.ogg -> UPS_BackgroundAudio.ogg).
        public static CopyResult CreateUpsAudioFromBackground()
        {
            var status = GetActiveThemeStatus();
            if (status.Status != ActiveThemeUpsStatus.CanBeCreated)
            {
                return new CopyResult
                {
                    Success = false,
                    ErrorMessage = "No background.* file to copy from in the active theme."
                };
            }

            try
            {
                var sourcePath = status.BackgroundSourcePath;
                var sourceExt = Path.GetExtension(sourcePath);
                var targetPath = Path.Combine(
                    Path.GetDirectoryName(sourcePath) ?? string.Empty,
                    UpsAudioBaseName + sourceExt);

                File.Copy(sourcePath, targetPath, overwrite: true);

                InvalidateCache();

                return new CopyResult { Success = true, TargetPath = targetPath };
            }
            catch (Exception ex)
            {
                return new CopyResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        // === Internal helpers ===

        private static string ScanForUpsAudioFile()
        {
            try
            {
                var themeDir = ResolveActiveThemeDirectory();
                if (string.IsNullOrWhiteSpace(themeDir)) return null;

                var audioDir = Path.Combine(themeDir, "audio");
                if (!Directory.Exists(audioDir)) return null;

                return FindFileWithBaseName(audioDir, UpsAudioBaseName);
            }
            catch (Exception ex)
            {
                LogManager.GetLogger()?.Error(ex, "PlayniteThemeHelper: Error finding UPS audio file");
                return null;
            }
        }

        // Returns Playnite's data-folder roots to probe for themes/config, most-likely
        // first. ConfigurationPath comes from the SDK and is correct for BOTH installed
        // (%AppData%\Playnite) and portable (data folder next to the .exe, any drive)
        // installs — portable installs were the cause of issue #76. The %AppData% and
        // %LocalAppData% paths are kept as fallbacks so the built-in Default theme
        // (historically under %LocalAppData%) still resolves for installed users.
        private static System.Collections.Generic.IEnumerable<string> GetPlayniteDataRoots()
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string configPath = null;
            try { configPath = _api?.Paths?.ConfigurationPath; } catch { }
            if (!string.IsNullOrWhiteSpace(configPath) && seen.Add(configPath))
                yield return configPath;

            var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playnite");
            if (seen.Add(roaming)) yield return roaming;

            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playnite");
            if (seen.Add(local)) yield return local;
        }

        // Returns the full theme directory (parent of the audio/ folder) for the active
        // fullscreen theme, or null if the theme can't be resolved.
        private static string ResolveActiveThemeDirectory()
        {
            var themeId = GetActiveFullscreenThemeId();
            if (string.IsNullOrWhiteSpace(themeId)) return null;

            bool isDefault = themeId == "Playnite_builtin_DefaultFullscreen";

            // Probe each Playnite data root (ConfigurationPath first for portable support).
            foreach (var root in GetPlayniteDataRoots())
            {
                var searchDir = Path.Combine(root, "Themes", "Fullscreen");
                if (!Directory.Exists(searchDir)) continue;

                // Built-in default theme: ID is "Playnite_builtin_DefaultFullscreen", dir is just "Default"
                if (isDefault)
                {
                    var defaultDir = Path.Combine(searchDir, "Default");
                    if (Directory.Exists(defaultDir)) return defaultDir;
                    continue;
                }

                // User-installed themes: directory name matches the theme ID
                var match = Path.Combine(searchDir, themeId);
                if (Directory.Exists(match)) return match;

                // Fall back to case-insensitive name match for filesystems that need it
                foreach (var dir in Directory.GetDirectories(searchDir))
                {
                    if (string.Equals(Path.GetFileName(dir), themeId, StringComparison.OrdinalIgnoreCase))
                        return dir;
                }
            }
            return null;
        }

        // Gets the active fullscreen theme ID from Playnite's config.
        // Tries reflection first (through the SDK wrapper's inner settings field),
        // falls back to reading fullscreenConfig.json directly.
        private static string GetActiveFullscreenThemeId()
        {
            // Approach 1: Reflect through FullscreenSettingsAPI -> inner FullscreenSettings.Theme
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

            // Approach 2: Read from fullscreenConfig.json. Probe each Playnite data root
            // so portable installs (data folder next to the .exe, any drive) resolve too,
            // not just %AppData% installs (issue #76).
            try
            {
                foreach (var root in GetPlayniteDataRoots())
                {
                    var configPath = Path.Combine(root, "fullscreenConfig.json");
                    if (!File.Exists(configPath)) continue;

                    var json = File.ReadAllText(configPath);
                    var key = "\"Theme\"";
                    var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var colonIdx = json.IndexOf(':', idx + key.Length);
                        var quoteStart = json.IndexOf('"', colonIdx + 1);
                        var quoteEnd = json.IndexOf('"', quoteStart + 1);
                        if (quoteStart >= 0 && quoteEnd > quoteStart)
                            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                    }
                }
            }
            catch { }

            return null;
        }

        // Reads "Name:" from theme.yaml so the settings UI can show "Aniki ReMake" instead of
        // the raw theme-ID directory name. Best-effort; returns null on any failure.
        private static string ReadThemeDisplayName(string themeDir)
        {
            try
            {
                var yamlPath = Path.Combine(themeDir, "theme.yaml");
                if (!File.Exists(yamlPath)) return null;

                var lines = File.ReadAllLines(yamlPath);
                foreach (var line in lines)
                {
                    var trimmed = line.TrimStart();
                    if (!trimmed.StartsWith("Name:", StringComparison.OrdinalIgnoreCase)) continue;

                    var value = trimmed.Substring("Name:".Length).Trim().Trim('"', '\'');
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }
            catch { }
            return null;
        }

        private static string FindFileWithBaseName(string audioDir, string baseName)
        {
            foreach (var ext in SupportedExtensions)
            {
                var filePath = Path.Combine(audioDir, baseName + ext);
                if (File.Exists(filePath)) return filePath;
            }
            return null;
        }
    }
}
