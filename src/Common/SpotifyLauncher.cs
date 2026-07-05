using System;
using System.Diagnostics;
using System.IO;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    // Locates and launches the Spotify desktop app. Fail-safe throughout (never throws to callers).
    // The launch DECISION must use IsSpotifyRunning() (process check), NOT SMTC availability —
    // SMTC only sees registered media sessions, so a running-but-no-session Spotify would be
    // double-launched if keyed off session availability.
    public static class SpotifyLauncher
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // True if a Spotify desktop process is currently running (Win32 or Store; both surface a
        // "Spotify" process name). Fail-safe: returns false on any error.
        public static bool IsSpotifyRunning()
        {
            try { return Process.GetProcessesByName("Spotify").Length > 0; }
            catch (Exception ex) { Logger.Warn($"[SpotifyLauncher] IsSpotifyRunning failed: {ex.Message}"); return false; }
        }

        // Public resolver: auto-scan %APPDATA%\Spotify\Spotify.exe first, then the user path.
        public static string ResolveSpotifyPath(string userConfiguredPath)
        {
            string autoScan;
            try
            {
                autoScan = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Spotify", "Spotify.exe");
            }
            catch { autoScan = null; }
            return ResolveSpotifyPath(userConfiguredPath, autoScan, File.Exists);
        }

        // Testable overload: candidate + fileExists injected.
        internal static string ResolveSpotifyPath(string userConfiguredPath, string autoScanCandidate, Func<string, bool> fileExists)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(autoScanCandidate) && fileExists(autoScanCandidate))
                    return autoScanCandidate;
                if (!string.IsNullOrWhiteSpace(userConfiguredPath) && fileExists(userConfiguredPath))
                    return userConfiguredPath;
                return null;
            }
            catch (Exception ex) { Logger.Warn($"[SpotifyLauncher] ResolveSpotifyPath failed: {ex.Message}"); return null; }
        }

        // Launches the given path via ShellExecute (resolves .exe and .lnk natively). No WaitForExit
        // (Spotify is a long-running GUI app). Returns true if Process.Start didn't throw.
        public static bool Launch(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                using (var p = Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }))
                {
                    Logger.Info($"[SpotifyLauncher] Launched Spotify: {path}");
                    return true;
                }
            }
            catch (Exception ex) { Logger.Warn($"[SpotifyLauncher] Launch failed for '{path}': {ex.Message}"); return false; }
        }
    }
}
