using System;
using System.Windows.Threading;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Owns the issue-#81 sleep triggers. One 1-minute idle timer (loaded-but-paused COUNTS toward
    // idle), plus immediate release on lock/suspend (routed from UniPlaySong's SystemEvents handlers).
    // All releases go through AudioDeviceRegistry.ReleaseAllDevices so every holder closes.
    public class SleepCoordinator
    {
        private readonly AudioDeviceRegistry _registry;
        private readonly Func<bool> _isAudible;      // true when music is actively playing (not paused/stopped)
        private readonly Func<int> _getIdleMinutes;  // IdleAudioDeviceTeardownMinutes (0 disables idle release)
        private readonly Func<bool> _isGameRunning;  // true while any game is running — hold the device open
        private readonly FileLogger _fileLogger;

        private DispatcherTimer _idleTimer;
        private DateTime _idleBaselineUtc;           // when the current idle stretch began
        private bool _wasAudible;                    // last observed audible state
        private bool _seeded;                        // first IdleTick seeds the baseline at "now"

        public SleepCoordinator(AudioDeviceRegistry registry, Func<bool> isAudible, Func<int> getIdleMinutes, FileLogger fileLogger, Func<bool> isGameRunning = null)
        {
            _registry = registry;
            _isAudible = isAudible;
            _getIdleMinutes = getIdleMinutes;
            _isGameRunning = isGameRunning ?? (() => false);
            _fileLogger = fileLogger;
            _wasAudible = false;
            _seeded = false;
        }

        // Immediate release for lock/suspend — fires regardless of the idle setting. Safe from any thread.
        public void OnLockOrSuspend(string reason)
        {
            _fileLogger?.Debug($"[Sleep] {reason} — coordinator releasing audio devices");
            _registry?.ReleaseAllDevices(reason);
        }

        // Minimize / hide-to-tray: the window is out of the way, so if nothing is audible there is
        // no reason to hold the device for the rest of the idle countdown. Returns true if it
        // released.
        //
        // Conditional on silence, unlike lock and suspend. Those mean the machine is going away and
        // everything stops regardless; a minimize with PauseOnMinimize off is someone deliberately
        // keeping the music going in the background, and releasing there would cut it off.
        //
        // Gated by the same "Release after: 0 min" that disables the idle timer, so one control
        // still governs whether UPS releases the device on its own at all.
        public bool OnWindowHidden(string reason)
        {
            bool audible;
            try { audible = _isAudible?.Invoke() ?? false; } catch { audible = false; }
            if (audible)
            {
                _fileLogger?.Debug($"[Sleep] {reason} — still audible, keeping the device");
                return false;
            }

            bool gameRunning;
            try { gameRunning = _isGameRunning?.Invoke() ?? false; } catch { gameRunning = false; }
            if (gameRunning)
            {
                // Minimizing to play a game is the common case, and the machine stays awake anyway.
                _fileLogger?.Debug($"[Sleep] {reason} — game running, keeping the device");
                return false;
            }

            int minutes = 0;
            try { minutes = _getIdleMinutes?.Invoke() ?? 0; } catch { minutes = 0; }
            if (minutes <= 0)
            {
                _fileLogger?.Debug($"[Sleep] {reason} — idle release disabled, keeping the device");
                return false;
            }

            _fileLogger?.Debug($"[Sleep] {reason} — releasing audio devices");
            int released = _registry?.ReleaseAllDevices(reason) ?? 0;

            // SDL2's device is process-wide and can be open with no holder claiming it.
            SDL2MusicPlayer.CloseSharedDeviceIfUnused();

            // Restart the idle countdown so a restore-then-idle still behaves normally.
            _idleBaselineUtc = DateTime.UtcNow;
            _seeded = true;
            _wasAudible = false;
            return released > 0;
        }

        // Pure idle state machine (unit-tested). Returns true if it released devices this tick.
        // Audible playback resets the idle baseline; paused/stopped lets the baseline age. When the
        // idle stretch reaches the threshold (and a device is open), release. 0 minutes disables.
        public bool IdleTick(DateTime nowUtc)
        {
            bool audible;
            try { audible = _isAudible?.Invoke() ?? false; } catch { audible = false; }

            bool gameRunning;
            try { gameRunning = _isGameRunning?.Invoke() ?? false; } catch { gameRunning = false; }

            // A running game keeps Windows awake anyway, so releasing UPS's audio device buys no sleep — and tearing it
            // down mid-game (common when PauseOnGameStart pauses the music, or a controller-only session reads as idle to
            // GetLastInputInfo) breaks clean resume on game exit. Treat "game running" like audible: hold the device open,
            // reset baseline.
            if (audible || gameRunning)
            {
                _idleBaselineUtc = nowUtc; // reset — actively playing (or in-game) is not idle
                _wasAudible = true;
                _seeded = true;
                return false;
            }

            // First observation, or a transition from audible: (re)seed the idle baseline at this
            // tick so the idle stretch is measured from a real reference point.
            if (!_seeded || _wasAudible)
            {
                _idleBaselineUtc = nowUtc;
                _seeded = true;
                _wasAudible = false;
                return false;
            }

            int minutes = 0;
            try { minutes = _getIdleMinutes?.Invoke() ?? 0; } catch { minutes = 0; }
            if (minutes <= 0) return false; // idle release disabled

            var idleFor = nowUtc - _idleBaselineUtc;
            if (idleFor.TotalMinutes < minutes) return false;

            // Logged because a tick that decides NOT to release used to be silent, which made
            // "the idle timer doesn't work" impossible to confirm or refute from a log.
            bool anyOpen = _registry?.IsAnyDeviceOpen ?? false;
            _fileLogger?.Debug($"[Sleep] idle {idleFor.TotalMinutes:F1}min >= {minutes}min threshold, deviceOpen={anyOpen}");
            if (!anyOpen)
            {
                // SDL2's device is process-wide and can be open with no live holder claiming it,
                // so ask directly rather than concluding there is nothing to close.
                SDL2MusicPlayer.CloseSharedDeviceIfUnused();
                return false;
            }

            int released = _registry.ReleaseAllDevices($"Idle {idleFor.TotalMinutes:F1}min");
            // Reset baseline so we don't re-fire every tick while still idle.
            _idleBaselineUtc = nowUtc;
            return released > 0;
        }

        public void Start()
        {
            if (_idleTimer != null) return;
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _idleTimer.Tick += (s, e) => { try { IdleTick(DateTime.UtcNow); } catch { } };
            _idleTimer.Start();
        }

        public void Stop()
        {
            if (_idleTimer == null) return;
            _idleTimer.Stop();
            _idleTimer = null;
        }
    }
}
