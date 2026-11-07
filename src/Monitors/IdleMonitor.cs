using System;
using System.Windows;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Monitors
{
    // Idle/AFK features: pause, lower the volume, or engage Calm Down after keyboard/mouse inactivity.
    // Created once in OnApplicationStarted (on the UI thread: its timers are DispatcherTimers).
    public class IdleMonitor
    {
        private readonly Func<UniPlaySongSettings> _getSettings;
        private readonly IMusicPlaybackService _playbackService;
        private readonly FileLogger _fileLogger;
        private readonly Action _rewarmJingle;
        private readonly Func<bool> _isUsingLiveEffectsPlayer;

        // Read live: a settings save replaces the whole settings object.
        private UniPlaySongSettings _settings => _getSettings();

        // Idle/AFK detection (polls keyboard/mouse activity to pause after inactivity)
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private System.Windows.Threading.DispatcherTimer _idlePollTimer;
        private bool _idleDetected;

        // Dynamic Volume on Idle — gradual volume fade when inactive
        private bool _idleVolumeLowered;
        private System.Windows.Threading.DispatcherTimer _idleVolumeFadeTimer;
        private double _idleVolumeFadeCurrent = 1.0;

        public IdleMonitor(Func<UniPlaySongSettings> getSettings, IMusicPlaybackService playbackService,
            FileLogger fileLogger, Action rewarmJingle, Func<bool> isUsingLiveEffectsPlayer)
        {
            _getSettings = getSettings;
            _playbackService = playbackService;
            _fileLogger = fileLogger;
            _rewarmJingle = rewarmJingle;
            _isUsingLiveEffectsPlayer = isUsingLiveEffectsPlayer;

            _idlePollTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background);
            _idlePollTimer.Interval = TimeSpan.FromSeconds(10);
            _idlePollTimer.Tick += OnIdlePollTick;
            _idlePollTimer.Start();
        }

        public void Stop()
        {
            _idlePollTimer?.Stop();
            _idlePollTimer = null;
        }

        // The user is back (session unlock): lift the idle pause and restore the volume now, not on the next tick.
        public void ClearIdleState()
        {
            if (_idleDetected)
            {
                _idleDetected = false;
                _playbackService?.RemovePauseSource(Models.PauseSource.Idle);
            }
            if (_idleVolumeLowered)
            {
                _idleVolumeLowered = false;
                RestoreIdleVolume();
            }
        }

        // Polls keyboard/mouse inactivity for idle-based features (pause and/or volume lowering).
        // Uses Win32 GetLastInputInfo — does not detect gamepad input.
        private void OnIdlePollTick(object sender, EventArgs e)
        {
            bool pauseEnabled = _settings?.PauseOnIdle == true;
            bool volumeEnabled = _settings?.LowerVolumeOnIdle == true;
            bool calmDownEnabled = _settings?.CalmDownOnIdle == true;

            // Fast exit: no idle feature enabled
            if (!pauseEnabled && !volumeEnabled && !calmDownEnabled)
            {
                if (_idleDetected)
                {
                    _idleDetected = false;
                    _playbackService?.RemovePauseSource(Models.PauseSource.Idle);
                }
                if (_idleVolumeLowered)
                {
                    _idleVolumeLowered = false;
                    RestoreIdleVolume();
                }
                if (_settings != null && _settings.CalmDownIdleActive)
                {
                    _settings.CalmDownIdleActive = false;
                }
                return;
            }

            try
            {
                var info = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (!GetLastInputInfo(ref info))
                    return;

                uint idleMs = (uint)Environment.TickCount - info.dwTime;

                // Pause on Idle
                if (pauseEnabled)
                {
                    uint thresholdMs = (uint)(_settings.IdleTimeoutMinutes * 60 * 1000);
                    if (idleMs >= thresholdMs)
                    {
                        if (!_idleDetected)
                        {
                            _idleDetected = true;
                            _fileLogger?.Debug($"Idle detected ({_settings.IdleTimeoutMinutes}min), pausing");
                            _playbackService?.AddPauseSource(Models.PauseSource.Idle);
                        }
                    }
                    else if (_idleDetected)
                    {
                        _idleDetected = false;
                        _fileLogger?.Debug("Input detected, resuming from idle");
                        _playbackService?.RemovePauseSource(Models.PauseSource.Idle);
                        _rewarmJingle(); // re-open jingle device if idle-teardown released it
                    }
                }
                else if (_idleDetected)
                {
                    _idleDetected = false;
                    _playbackService?.RemovePauseSource(Models.PauseSource.Idle);
                }

                // Dynamic Volume on Idle
                if (volumeEnabled)
                {
                    uint volumeThresholdMs = (uint)(_settings.IdleVolumeTimeoutMinutes * 60 * 1000);
                    if (idleMs >= volumeThresholdMs)
                    {
                        if (!_idleVolumeLowered)
                        {
                            _idleVolumeLowered = true;
                            _fileLogger?.Debug($"Idle volume: lowering to 25% ({_settings.IdleVolumeTimeoutMinutes}min)");
                            StartIdleVolumeFade(0.25);
                        }
                    }
                    else if (_idleVolumeLowered)
                    {
                        _idleVolumeLowered = false;
                        _fileLogger?.Debug("Input detected, restoring volume");
                        RestoreIdleVolume();
                    }
                }
                else if (_idleVolumeLowered)
                {
                    _idleVolumeLowered = false;
                    RestoreIdleVolume();
                }

                // Calm Down on Idle
                //
                // Sets the runtime-only CalmDownIdleActive rather than CalmDownModeEnabled, so the
                // user's own toggle is left alone and idle cannot switch off a Calm Down they turned
                // on themselves. CalmDownProcessor ORs the two and ramps on the audio thread, which
                // is why there is no fade timer here to match the volume feature's.
                //
                // Re-asserted every tick rather than only on the transition: a settings save clones
                // through JSON and drops this [JsonIgnore] flag, so without the re-assert an
                // unrelated save during an idle stretch would quietly lift Calm Down until the next
                // input. The write is guarded so it only fires when the value actually differs.
                if (_settings != null)
                {
                    bool wantCalm = calmDownEnabled
                        && idleMs >= (uint)(_settings.CalmDownIdleTimeoutMinutes * 60 * 1000);

                    if (_settings.CalmDownIdleActive != wantCalm)
                    {
                        _settings.CalmDownIdleActive = wantCalm;
                        _fileLogger?.Debug(wantCalm
                            ? $"Idle Calm Down: engaging ({_settings.CalmDownIdleTimeoutMinutes}min)"
                            : "Input detected, releasing idle Calm Down");

                        // The flag alone does nothing on SDL2 — CalmDownProcessor only exists in the
                        // NAudio pipeline. Both routes into that pipeline are covered (startup via
                        // CreateMusicPlayer, mid-session via the backend swap), so this should not
                        // fire; say so loudly if it ever does, because the symptom is silence and
                        // nothing else in the log would explain it.
                        if (wantCalm && !_isUsingLiveEffectsPlayer())
                        {
                            _fileLogger?.Warn("Idle Calm Down engaged but the active player cannot host the effect "
                                + "(SDL2). Restart Playnite, or toggle any Live Effects setting, to rebuild on NAudio.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"Idle poll error: {ex.Message}");
            }
        }

        private void StartIdleVolumeFade(double target)
        {
            if (_idleVolumeFadeTimer == null)
            {
                _idleVolumeFadeTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background);
                _idleVolumeFadeTimer.Interval = TimeSpan.FromMilliseconds(50);
                _idleVolumeFadeTimer.Tick += OnIdleVolumeFadeTick;
            }
            _idleVolumeFadeTimer.Start();
        }

        private void OnIdleVolumeFadeTick(object sender, EventArgs e)
        {
            // Step ~0.015 per 50ms tick = 1.0→0.25 in ~50 ticks = ~2.5 seconds
            _idleVolumeFadeCurrent = Math.Max(0.25, _idleVolumeFadeCurrent - 0.015);
            _playbackService?.SetIdleVolumeMultiplier(_idleVolumeFadeCurrent);

            if (_idleVolumeFadeCurrent <= 0.25)
                _idleVolumeFadeTimer.Stop();
        }

        private void RestoreIdleVolume()
        {
            _idleVolumeFadeTimer?.Stop();
            _idleVolumeFadeCurrent = 1.0;
            _playbackService?.SetIdleVolumeMultiplier(1.0);
        }
    }
}
