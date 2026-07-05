using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    // Locates and launches the Spotify desktop app (classic Win32 install AND Microsoft Store install).
    // Fail-safe throughout (never throws to callers).
    //
    // The launch DECISION must use IsSpotifyRunning() (process check), NOT SMTC availability — SMTC only
    // sees registered media sessions, so a running-but-no-session Spotify would be double-launched if
    // keyed off session availability.
    //
    // Store vs Win32: the classic installer puts Spotify.exe at %APPDATA%\Spotify\Spotify.exe (a real,
    // launchable file). The Microsoft Store version has NO stable launchable .exe path — it lives under
    // C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_<version>_x64__zpdnekdrzrea0\ which is
    // version-pinned AND execution-protected. Store apps launch by AppUserModelId (AUMID) via
    // shell:AppsFolder, or by the "spotify:" URI protocol — both version-independent. The publisher-hash
    // suffix "zpdnekdrzrea0" is constant for Spotify's official Store package on every machine.
    public static class SpotifyLauncher
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // Version-independent AppUserModelId for the Store Spotify package. PackageFamilyName is stable
        // across machines/versions (only the version segment of the install folder changes); "!Spotify"
        // is the app entry point within the package.
        private const string StoreAumid = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";

        // True if a Spotify desktop process is currently running (Win32 or Store; both surface a
        // "Spotify" process name). Fail-safe: returns false on any error.
        public static bool IsSpotifyRunning()
        {
            try { return Process.GetProcessesByName("Spotify").Length > 0; }
            catch (Exception ex) { Logger.Warn($"[SpotifyLauncher] IsSpotifyRunning failed: {ex.Message}"); return false; }
        }

        // Resolves a launchable Spotify FILE path (classic Win32 install or a user-supplied exe/.lnk),
        // or null if none exists. Store installs have no such path — LaunchSpotify falls back to the
        // AUMID/URI strategies when this returns null.
        public static string ResolveSpotifyPath(string userConfiguredPath)
        {
            return ResolveSpotifyPath(userConfiguredPath, GetAutoScanCandidates(), File.Exists);
        }

        // Testable overload: user path + ordered auto-scan candidates + fileExists injected.
        internal static string ResolveSpotifyPath(string userConfiguredPath, IEnumerable<string> autoScanCandidates, Func<string, bool> fileExists)
        {
            try
            {
                // User path first — explicit intent wins over auto-detection.
                if (!string.IsNullOrWhiteSpace(userConfiguredPath) && fileExists(userConfiguredPath))
                    return userConfiguredPath;

                if (autoScanCandidates != null)
                {
                    foreach (var candidate in autoScanCandidates)
                    {
                        if (!string.IsNullOrWhiteSpace(candidate) && fileExists(candidate))
                            return candidate;
                    }
                }
                return null;
            }
            catch (Exception ex) { Logger.Warn($"[SpotifyLauncher] ResolveSpotifyPath failed: {ex.Message}"); return null; }
        }

        // Known launchable file-path candidates for the classic Win32 install and the Store execution
        // alias, in priority order. Best-effort — any that can't be built is skipped.
        internal static List<string> GetAutoScanCandidates()
        {
            var candidates = new List<string>();
            TryAddCombined(candidates, Environment.SpecialFolder.ApplicationData, "Spotify", "Spotify.exe");          // %APPDATA%\Spotify\Spotify.exe (classic installer)
            TryAddCombined(candidates, Environment.SpecialFolder.LocalApplicationData, "Microsoft", "WindowsApps", "Spotify.exe"); // Store execution alias (0-byte reparse stub, launchable if present)
            return candidates;
        }

        private static void TryAddCombined(List<string> list, Environment.SpecialFolder root, params string[] parts)
        {
            try
            {
                var basePath = Environment.GetFolderPath(root);
                if (string.IsNullOrEmpty(basePath)) return;
                var all = new string[parts.Length + 1];
                all[0] = basePath;
                Array.Copy(parts, 0, all, 1, parts.Length);
                list.Add(Path.Combine(all));
            }
            catch { /* skip this candidate */ }
        }

        // Launches Spotify, trying strategies in order until one succeeds (returns true):
        //   1. A resolved FILE path (user-set exe/.lnk, or Win32 auto-scan) — ShellExecute.
        //   2. The "spotify:" URI protocol (registered by both Win32 and Store installs).
        //   3. The Store AUMID via shell:AppsFolder (version-independent Store launch).
        // Fail-safe: returns false only if every strategy fails.
        public static bool LaunchSpotify(string userConfiguredPath)
        {
            var path = ResolveSpotifyPath(userConfiguredPath);
            if (!string.IsNullOrEmpty(path) && Launch(path))
                return true;

            if (LaunchViaUri())
                return true;

            if (LaunchViaStoreAumid())
                return true;

            Logger.Warn("[SpotifyLauncher] All launch strategies failed (no file path, no URI, no Store AUMID).");
            return false;
        }

        // Launches the given path via ShellExecute (resolves .exe and .lnk natively). No WaitForExit
        // (Spotify is a long-running GUI app). Returns true if Process.Start didn't throw.
        public static bool Launch(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                using (Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }))
                {
                    Logger.Info($"[SpotifyLauncher] Launched Spotify (file): {path}");
                    return true;
                }
            }
            catch (Exception ex) { Logger.Warn($"[SpotifyLauncher] Launch(file) failed for '{path}': {ex.Message}"); return false; }
        }

        // Launches via the "spotify:" URI protocol (registered by both install flavors). ShellExecute.
        private static bool LaunchViaUri()
        {
            try
            {
                using (Process.Start(new ProcessStartInfo { FileName = "spotify:", UseShellExecute = true }))
                {
                    Logger.Info("[SpotifyLauncher] Launched Spotify (spotify: URI).");
                    return true;
                }
            }
            catch (Exception ex) { Logger.Warn($"[SpotifyLauncher] Launch(URI) failed: {ex.Message}"); return false; }
        }

        // Launches the Store package by AUMID via explorer.exe shell:AppsFolder. explorer.exe reliably
        // resolves shell:AppsFolder across Windows versions. Version-independent.
        private static bool LaunchViaStoreAumid()
        {
            try
            {
                using (Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "shell:AppsFolder\\" + StoreAumid,
                    UseShellExecute = true
                }))
                {
                    Logger.Info($"[SpotifyLauncher] Launched Spotify (Store AUMID): {StoreAumid}");
                    return true;
                }
            }
            catch (Exception ex) { Logger.Warn($"[SpotifyLauncher] Launch(Store AUMID) failed: {ex.Message}"); return false; }
        }
    }
}
