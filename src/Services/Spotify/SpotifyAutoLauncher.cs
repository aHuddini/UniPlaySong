using System;
using System.Windows;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services.Spotify
{
    // "Launch Spotify on startup": starts the desktop app when Spotify is the music source, engages it once its
    // media session registers, and minimizes its window. Used once, from OnApplicationStarted. The client, control
    // service and window handle are read live (Func) because the worker outlives the call and the handle is assigned
    // after it starts.
    public class SpotifyAutoLauncher
    {
        private readonly IPlayniteAPI _api;
        private readonly FileLogger _fileLogger;
        private readonly Func<UniPlaySongSettings> _getSettings;
        private readonly Func<SpotifySmtcClient> _getSpotifyClient;
        private readonly Func<SpotifyControlService> _getSpotifyControlService;
        private readonly Func<bool> _isFullscreen;
        private readonly Func<IntPtr> _getMainWindowHandle;

        public SpotifyAutoLauncher(IPlayniteAPI api, FileLogger fileLogger, Func<UniPlaySongSettings> getSettings,
            Func<SpotifySmtcClient> getSpotifyClient, Func<SpotifyControlService> getSpotifyControlService,
            Func<bool> isFullscreen, Func<IntPtr> getMainWindowHandle)
        {
            _api = api;
            _fileLogger = fileLogger;
            _getSettings = getSettings;
            _getSpotifyClient = getSpotifyClient;
            _getSpotifyControlService = getSpotifyControlService;
            _isFullscreen = isFullscreen;
            _getMainWindowHandle = getMainWindowHandle;
        }

        // True when Spotify is the user's active music source (radio or default). The auto-launch
        // gate — we never launch Spotify unless it's actually the chosen source.
        private bool SpotifyIsActiveSource(UniPlaySongSettings settings)
        {
            if (settings == null) return false;
            bool radioSpotify = settings.RadioModeEnabled
                && settings.RadioMusicSource == RadioMusicSource.Spotify;
            bool defaultSpotify = settings.DefaultMusicSourceOption == DefaultMusicSource.Spotify;
            return radioSpotify || defaultSpotify;
        }

        // Experimental: on startup, if Spotify is the active source and the desktop app isn't
        // running, launch it (file path / spotify: URI / Store AUMID), then run a 15s dual-goal
        // watch: ENGAGE when the SMTC session registers (standard Recompute sends Play; toast at the
        // 10s mark if no session), and MINIMIZE Spotify's window whenever it appears (+ re-assert Playnite foreground
        // in Fullscreen). The two goals are decoupled because Spotify's session usually registers BEFORE its window
        // renders on a cold start. All blocking work (Process.Start + watch loop) runs on the Spotify worker thread —
        // NEVER the UI thread or under the recompute lock (issue: 8e1f2e4 launch-freeze deadlock). Called once from
        // OnApplicationStarted, after _appStarted (avoids premature-engage churn, b849240).
        public void TryAutoLaunchSpotify()
        {
            var settings = _getSettings();
            if (settings?.AutoLaunchSpotifyOnStartup != true) return;   // feature off
            if (!SpotifyIsActiveSource(settings)) return;               // Spotify not the source
            if (Common.SpotifyLauncher.IsSpotifyRunning()) return;      // already running (process check, not SMTC)

            var userPath = settings.SpotifyExePath;

            _getSpotifyClient()?.PostToWorker(() =>
            {
                try
                {
                    // Try every launch strategy: file path (Win32/user exe/.lnk), then the "spotify:"
                    // URI, then the Store AUMID (Microsoft Store install has no launchable file path).
                    if (!Common.SpotifyLauncher.LaunchSpotify(userPath))
                    {
                        _fileLogger?.Warn("[AutoLaunch] Could not launch Spotify (no file path, URI, or Store AUMID worked).");
                        ShowSpotifyNotRunningToast();
                        return;
                    }

                    // Watch loop (15s, 1s ticks) with TWO independent goals — deliberately decoupled,
                    // because cold-start Spotify usually registers its SMTC session BEFORE its window
                    // renders, so an engage-and-return poll stops watching before the window exists
                    // and nothing ever minimizes it:
                    //   1. ENGAGE: when the SMTC session registers, run the standard Recompute (sends
                    //      Play). If no session by the 10s mark, show the failure toast — but keep
                    //      watching for the window.
                    //   2. MINIMIZE: when Spotify's window appears, minimize it (Spotify has no
                    //      "start minimized" option). In Fullscreen, then re-assert Playnite as
                    //      foreground (minimizing hands focus to the desktop there, killing controller
                    //      input); in Desktop, minimizing restores Playnite naturally and force-focus
                    //      could yank focus if the user tabbed away.
                    // Exits early only when BOTH goals are met.
                    // restoreForeground: in Fullscreen we must actively pull Playnite back to the front
                    // AFTER minimizing Spotify — and keep doing it for a while, because Playnite's own Fullscreen startup (theme
                    // intro video, login/hub overlay) keeps grabbing focus for ~15-20s after launch, stealing it back from a single
                    // restore. We re-assert each tick until Playnite is confirmed foreground (self-limiting), then stop.
                    bool restoreForeground = _isFullscreen();
                    bool minimized = false;
                    bool engaged = false;
                    bool toasted = false;
                    bool foregroundSettled = false;

                    for (int i = 1; i <= 20; i++)
                    {
                        System.Threading.Thread.Sleep(1000);

                        if (!minimized)
                        {
                            minimized = Common.SpotifyLauncher.MinimizeSpotifyWindow();
                            _fileLogger?.Debug($"[AutoLaunch] t={i}s minimize attempt -> {(minimized ? "MINIMIZED" : "window not found yet")}");
                        }

                        // Re-assert Playnite foreground each tick after minimize (Fullscreen only), continuing even once it briefly
                        // sticks — Playnite's theme-intro (video + login/hub overlay) keeps stealing focus for ~15-20s, so a one-shot
                        // restore loses to the LAST steal. Runs on the UI thread; we wait briefly for the result so foregroundSettled
                        // reflects the actual post-restore state for the exit check.
                        if (minimized && restoreForeground)
                        {
                            bool settledThisTick = false;
                            var wait = Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                settledThisTick = RestorePlayniteForegroundAndCheck();
                            }));
                            try { wait?.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
                            foregroundSettled = settledThisTick;
                            _fileLogger?.Debug($"[AutoLaunch] t={i}s foreground re-assert -> {(settledThisTick ? "IS foreground" : "not yet")}");
                        }

                        if (!engaged && _getSpotifyClient()?.IsAvailable == true)
                        {
                            engaged = true;
                            _fileLogger?.Debug($"[AutoLaunch] t={i}s Spotify session available — engaging.");
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                _getSpotifyControlService()?.Recompute()));
                        }

                        if (!engaged && !toasted && i == 10)
                        {
                            toasted = true;
                            _fileLogger?.Warn("[AutoLaunch] Spotify session not available after 10s.");
                            ShowSpotifyNotRunningToast();
                        }

                        // All goals met. In Fullscreen, don't exit on foreground-settled before ~8s — the theme intro keeps stealing
                        // focus for ~15-20s, so an early exit would stop re-asserting and lose to a later steal. After the churn
                        // window, exit once Playnite is confirmed foreground.
                        if (engaged && minimized && (!restoreForeground || (foregroundSettled && i >= 8)))
                            return;
                    }

                    _fileLogger?.Debug($"[AutoLaunch] watch ended (engaged={engaged}, minimized={minimized}, fgSettled={foregroundSettled}).");
                }
                catch (Exception ex)
                {
                    _fileLogger?.Warn($"[AutoLaunch] TryAutoLaunchSpotify worker failed: {ex.Message}");
                }
            });
        }

        // Brings Playnite back to the foreground after the auto-launch minimizes Spotify (Fullscreen)
        // and returns true if Playnite IS now the foreground window (so the caller can stop retrying). Resolves the
        // window handle AT CALL TIME on the UI thread: _mainWindowHandle is assigned LATER in OnApplicationStarted than
        // the auto-launch dispatch, so an eager capture at dispatch time reads IntPtr.Zero and the restore silently
        // no-ops (the original Fullscreen focus bug).
        private bool RestorePlayniteForegroundAndCheck()
        {
            try
            {
                var handle = _getMainWindowHandle();
                if (handle == IntPtr.Zero && Application.Current?.MainWindow != null)
                    handle = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
                if (handle == IntPtr.Zero)
                {
                    _fileLogger?.Debug("[AutoLaunch] restore foreground: handle ZERO!");
                    return false;
                }
                Common.SpotifyLauncher.BringWindowToForeground(handle);
                return Common.SpotifyLauncher.IsForegroundWindow(handle);
            }
            catch (Exception ex) { _fileLogger?.Debug($"[AutoLaunch] RestorePlayniteForeground failed: {ex.Message}"); return false; }
        }

        // Single failure toast (Jingle/Download style) for the auto-launch flow. Marshalled to UI.
        private void ShowSpotifyNotRunningToast()
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                try
                {
                    Common.DialogHelper.ShowErrorToast(_api,
                        "Spotify Desktop not running. Media Controls will not work if Spotify is closed.",
                        "UniPlaySong", null);
                }
                catch (Exception ex) { _fileLogger?.Debug($"[AutoLaunch] toast failed: {ex.Message}"); }
            }));
        }
    }
}
