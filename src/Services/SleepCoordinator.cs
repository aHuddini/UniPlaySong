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
        private readonly FileLogger _fileLogger;

        private DispatcherTimer _idleTimer;
        private DateTime _idleBaselineUtc;           // when the current idle stretch began
        private bool _wasAudible;                    // last observed audible state
        private bool _seeded;                        // first IdleTick seeds the baseline at "now"

        public SleepCoordinator(AudioDeviceRegistry registry, Func<bool> isAudible, Func<int> getIdleMinutes, FileLogger fileLogger)
        {
            _registry = registry;
            _isAudible = isAudible;
            _getIdleMinutes = getIdleMinutes;
            _fileLogger = fileLogger;
            _wasAudible = false;
            _seeded = false;
        }

        // Immediate release for lock/suspend — fires regardless of the idle setting. Safe from any thread.
        public void OnLockOrSuspend(string reason)
        {
            _registry?.ReleaseAllDevices(reason);
        }

        // Pure idle state machine (unit-tested). Returns true if it released devices this tick.
        // Audible playback resets the idle baseline; paused/stopped lets the baseline age. When the
        // idle stretch reaches the threshold (and a device is open), release. 0 minutes disables.
        public bool IdleTick(DateTime nowUtc)
        {
            bool audible;
            try { audible = _isAudible?.Invoke() ?? false; } catch { audible = false; }

            if (audible)
            {
                _idleBaselineUtc = nowUtc; // reset — actively playing is not idle
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

            if (!(_registry?.IsAnyDeviceOpen ?? false)) return false;

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
