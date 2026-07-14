using System;
using System.Threading;
using UniPlaySong.Audio;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Services.Spotify
{
    // Real-collaborator host for SpotifyEffectsCoordinator. Owns the loopback client, the
    // capture->provider->player wiring, and a single background thread that doubles as:
    //   1. capture-death watchdog (both effects + viz-only modes)
    //   2. viz-only silent pump (reads the capture through a VisualizationDataProvider so the
    //      desktop spectrum reacts to dry Spotify while UPS outputs nothing)
    // All coordinator entry (Evaluate/Shutdown) is serialized through this host's lock — the
    // coordinator mutates unguarded state and is driven from the UI thread, the SMTC worker
    // thread, and the watchdog thread.
    public class SpotifyLiveEffectsHost
    {
        private readonly Func<UniPlaySongSettings> _getSettings;
        private readonly Func<bool> _isSpotifyActive;
        private readonly Func<IMusicPlayer> _getPlayer; // current backend; effected output needs NAudio
        private readonly FileLogger _fileLogger;

        private readonly object _gate = new object();     // serializes coordinator Evaluate/Shutdown
        private readonly object _stateLock = new object(); // guards _client/_provider/_effecting/pump

        private SpotifyEffectsCoordinator _coordinator;
        private SpotifyLoopbackClient _client;
        private SpotifyCaptureSampleProvider _provider;
        private VisualizationDataProvider _pumpViz;        // viz-only tap (own provider, not the player's)
        private volatile bool _effecting;                  // true => mixer pulls the provider, pump idles

        private Thread _worker;
        private volatile bool _workerRun;

        // ~10ms pump cadence keeps the spectrum reactive; also the watchdog poll interval.
        private const int PumpIntervalMs = 10;

        public SpotifyLiveEffectsHost(
            Func<UniPlaySongSettings> getSettings,
            Func<bool> isSpotifyActive,
            Func<IMusicPlayer> getPlayer,
            FileLogger fileLogger)
        {
            _getSettings = getSettings;
            _isSpotifyActive = isSpotifyActive;
            _getPlayer = getPlayer;
            _fileLogger = fileLogger;

            _coordinator = new SpotifyEffectsCoordinator(
                isLiveEffects: () => _getSettings()?.LiveEffectsEnabled ?? false,
                isApplyToSpotify: () => _getSettings()?.ApplyLiveEffectsToSpotify ?? false,
                isVisualizer: () => _getSettings()?.ShowSpectrumVisualizer ?? false,
                isSpotifyActive: () => _isSpotifyActive(),
                isOsCapable: () => OsCapabilities.SupportsProcessLoopback,
                setMuted: SetMuted,
                startCapture: StartCapture,
                stopCapture: StopCapture,
                startEffectedOutput: StartEffectedOutput,
                stopEffectedOutput: StopEffectedOutput,
                isCalmDown: () => _getSettings()?.CalmDownModeEnabled ?? false);
        }

        // Armed at OnApplicationStarted (set + Evaluate there). Until then Evaluate no-ops:
        // settings-load events fire during InitializeServices, which otherwise starts effected
        // Spotify output several seconds before the theme binds its overlay flags — audible
        // during login views. Same gating rule as the Spotify radio engage (v1.5.8).
        public bool Armed { get; set; }

        // True while effected Spotify output is running (device is producing audio even though
        // UPS's own game player is idle). The sleep coordinator checks this so idle device-release
        // doesn't tear down the mixer out from under audible Spotify effects.
        public bool IsEffecting => _effecting;

        // Public entry points — always serialized so concurrent triggers (settings, Spotify
        // state, watchdog) never interleave coordinator state transitions.
        public void Evaluate()
        {
            if (!Armed) return;
            lock (_gate)
            {
                try { _coordinator.Evaluate(); }
                catch (Exception ex) { _fileLogger?.Warn($"[SpotifyFx] Evaluate failed: {ex.Message}"); }
            }
        }

        public void Shutdown()
        {
            lock (_gate)
            {
                try { _coordinator.Shutdown(); }
                catch (Exception ex) { _fileLogger?.Warn($"[SpotifyFx] Shutdown failed: {ex.Message}"); }
            }
        }

        // Unconditional safety net for process exit: hand Spotify back to Windows in a usable
        // state no matter what the coordinator thinks. Clears the WASAPI mute flag (which the
        // theme mute button — SpotifyAudioSession.ToggleMute — can set independently of the
        // effects duck) AND restores session volume if it's still ducked. Called from
        // OnApplicationStopped after Shutdown(), so even a state desync or a mute set outside the
        // coordinator can't leave Spotify muted/silent after Playnite closes.
        public void RestoreSpotifyForExit()
        {
            try
            {
                // We CAN restore — the trick is not dying before Windows commits it. A WASAPI
                // SetMasterVolume/SetMute call returns success the instant it's accepted, but the
                // audio engine applies it a few ms later on its own thread. At process exit that
                // window is fatal: the process terminates before the change lands, so Spotify stays
                // ducked/muted. Fix: set, then READ BACK and confirm the value actually took — only
                // stop once the session reports the restored level AND unmuted. This both proves it
                // landed and holds the process the extra beat Windows needs to commit.
                float restore = _volumeBeforeDuck > DuckVolume * 2 ? _volumeBeforeDuck : 1f;
                bool confirmed = false;
                for (int attempt = 0; attempt < 12 && !confirmed; attempt++)
                {
                    SpotifyAudioSession.SetMuted(false);
                    SpotifyAudioSession.SetSessionVolume(restore);
                    Thread.Sleep(25); // give the audio engine time to apply before we read back

                    bool muted = SpotifyAudioSession.IsMuted();
                    float now = SpotifyAudioSession.GetSessionVolume(0f);
                    // Confirmed when unmuted AND volume is at/near the target (not still ducked).
                    confirmed = !muted && now >= restore * 0.9f && now > DuckVolume * 2;
                }
                _fileLogger?.Debug($"[SpotifyFx] exit restore: target={restore:F4} confirmed={confirmed}");
            }
            catch (Exception ex) { _fileLogger?.Warn($"[SpotifyFx] exit restore failed: {ex.Message}"); }
        }

        // --- coordinator collaborator funcs (all called under _gate) ---

        // "Mute" is implemented as a volume DUCK, not a session mute: the process-loopback tap
        // sits post-session-volume, so a hard mute (or volume 0) silences our own capture too.
        // Duck to 2^-10 (~-60 dB, inaudible) and the provider multiplies by 1024 to restore —
        // power-of-2 scaling in float is lossless. Restore retries a few times (carry-forward F)
        // so a permanently-ducked Spotify self-heals; duck failure is safe (coordinator won't
        // start effected output, Spotify stays audible dry).
        internal const float DuckVolume = 0.0009765625f; // 2^-10
        private float _volumeBeforeDuck = 1f;

        private bool SetMuted(bool muted)
        {
            if (muted)
            {
                float current = SpotifyAudioSession.GetSessionVolume(1f);
                // Don't save an already-ducked level (e.g. re-entry after a crash) as the restore target.
                _volumeBeforeDuck = current > DuckVolume * 2 ? current : 1f;
                _fileLogger?.Debug($"[SpotifyFx] duck: saved pre-duck volume {_volumeBeforeDuck:F4}, ducking to {DuckVolume:F4}");
                return SpotifyAudioSession.SetSessionVolume(DuckVolume);
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (SpotifyAudioSession.SetSessionVolume(_volumeBeforeDuck)) return true;
                Thread.Sleep(30);
            }
            _fileLogger?.Warn("[SpotifyFx] VOLUME RESTORE FAILED after retries — Spotify may be left quiet; user can raise it in the Windows Volume Mixer.");
            return false;
        }

        private bool StartCapture()
        {
            lock (_stateLock)
            {
                _client = new SpotifyLoopbackClient();
                bool ok = _client.Start();
                if (!ok)
                {
                    _client = null;
                    _fileLogger?.Debug("[SpotifyFx] capture start failed (Spotify not found / native start error)");
                    return false;
                }
                _fileLogger?.Debug("[SpotifyFx] capture started");
                StartWorker();
                return true;
            }
        }

        private void StopCapture()
        {
            StopWorker(); // outside _stateLock: worker may be mid-iteration needing the lock
            lock (_stateLock)
            {
                try { _client?.Stop(); } catch (Exception ex) { _fileLogger?.Debug($"[SpotifyFx] client stop: {ex.Message}"); }
                _client = null;
                _provider = null;
                DisposePumpViz();
            }
            _fileLogger?.Debug("[SpotifyFx] capture stopped");
        }

        // Carry-forward D: provider built AFTER Start() so its WaveFormat reflects the real
        // capture format. Carry-forward E: any throw here tears down + unmutes (no muted-with-
        // no-output state) and is rethrown so the coordinator does NOT flip _effecting=true.
        private void StartEffectedOutput()
        {
            try
            {
                // Let Spotify's session volume fully settle to the duck level (2^-10) BEFORE we
                // build the output. Windows ramps a session-volume change over a few tens of ms;
                // any sample captured mid-ramp is louder than 2^-10 and the 1024x makeup gain turns
                // it into a pop. Sleeping here (outside the lock) + flushing after means only
                // fully-ducked audio ever reaches the gain stage.
                Thread.Sleep(120);

                lock (_stateLock)
                {
                    if (_client == null) throw new InvalidOperationException("capture not started");
                    var player = _getPlayer() as NAudioMusicPlayer;
                    if (player == null) throw new InvalidOperationException("effected output requires the NAudio backend");

                    // Effects mode owns the viz: kill any pump tap (created in the pre-effecting
                    // window) so its .Current can't shadow the player's external viz.
                    DisposePumpViz();
                    // Drop everything captured so far — the pre-duck and duck-settle samples that
                    // the 1024x makeup gain would turn into a blast. Only post-settle (fully ducked)
                    // audio flows from here, and the per-input fade-in ramps it up smoothly.
                    _client.FlushRing();
                    _provider = new SpotifyCaptureSampleProvider(_client)
                    {
                        // Spotify's session is ducked to 2^-10 in effects mode; restore the level
                        // (unity = 1/DuckVolume), plus a small +2 dB makeup so the effected copy
                        // matches dry Spotify's perceived loudness (dry ignores MusicVolume; the
                        // effected path is scaled by it). The provider clamps + the chain limiter
                        // (0.9 threshold) protect loud tracks from clipping.
                        GainCompensation = (1f / DuckVolume) * 1.26f
                    };
                    player.LoadExternalSource(_provider);
                    _effecting = true; // mixer now pulls the provider; pump idles
                }
                _fileLogger?.Debug("[SpotifyFx] effected output started");
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"[SpotifyFx] StartEffectedOutput failed: {ex.Message} — tearing down + unmuting");
                // Shutdown unmutes (coordinator.StopEffected -> setMuted(false)) and stops capture.
                try { _coordinator.Shutdown(); } catch { }
                throw;
            }
        }

        // Carry-forward A: backend teardown does NOT auto-stop the external source — we do it here.
        private void StopEffectedOutput()
        {
            lock (_stateLock)
            {
                _effecting = false;
                // Leaving effects mode: Spotify's volume gets restored, so the duck-compensation
                // gain must go too — a viz-only pump reusing this provider would otherwise see
                // 1024x clipped garbage.
                if (_provider != null) _provider.GainCompensation = 1f;
                try { (_getPlayer() as NAudioMusicPlayer)?.StopExternalSource(); }
                catch (Exception ex) { _fileLogger?.Debug($"[SpotifyFx] StopExternalSource: {ex.Message}"); }
            }
            _fileLogger?.Debug("[SpotifyFx] effected output stopped");
        }

        // --- single background thread: watchdog (both modes) + viz-only pump ---

        private void StartWorker()
        {
            if (_worker != null) return;
            _workerRun = true;
            _worker = new Thread(WorkerLoop)
            {
                Name = "UniPlaySong-SpotifyFxPump",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _worker.Start();
        }

        private void StopWorker()
        {
            var w = _worker;
            _workerRun = false;
            _worker = null;
            if (w != null && w.IsAlive && w != Thread.CurrentThread)
            {
                if (!w.Join(500)) _fileLogger?.Debug("[SpotifyFx] worker join timed out");
            }
        }

        private void WorkerLoop()
        {
            var buffer = new float[4096];
            // Warm-up grace: native capture can take longer than one loop to report IsCapturing
            // on a cold start, so we only treat !IsCapturing as death AFTER it's been confirmed
            // alive once (armed). Un-armed after WarmupMaxIterations (~5s) = a start that never
            // came alive; tear down so we don't loop forever un-armed. 5000ms / 10ms = 500 iters.
            const int WarmupMaxIterations = 5000 / PumpIntervalMs;
            bool watchdogArmed = false;
            int iterations = 0;
            while (_workerRun)
            {
                SpotifyLoopbackClient client;
                bool effecting;
                lock (_stateLock) { client = _client; effecting = _effecting; }

                if (client == null) break;

                // Watchdog: capture died while we still think we're capturing -> unmute + re-eval.
                // Runs off-thread so we don't re-enter our own _gate lock from inside StopCapture.
                if (client.IsCapturing)
                {
                    watchdogArmed = true; // capture confirmed alive; a later drop is real death
                }
                else if (watchdogArmed)
                {
                    _fileLogger?.Warn("[SpotifyFx] capture died — unmuting + re-evaluating");
                    ThreadPool.QueueUserWorkItem(_ => { Shutdown(); Evaluate(); });
                    break;
                }
                else if (++iterations >= WarmupMaxIterations)
                {
                    _fileLogger?.Warn("[SpotifyFx] capture never started — tearing down");
                    ThreadPool.QueueUserWorkItem(_ => { Shutdown(); Evaluate(); });
                    break;
                }

                // Viz-only pump: effects OFF, so the mixer isn't pulling the provider. Read the
                // capture through our own VisualizationDataProvider (which computes FFT and is set
                // as .Current) and discard the audio, so the desktop spectrum reacts to dry Spotify.
                if (!effecting)
                {
                    var viz = EnsurePumpViz(client);
                    if (viz != null) viz.Read(buffer, 0, buffer.Length);
                }

                Thread.Sleep(PumpIntervalMs);
            }
        }

        // Lazily builds the viz-only tap once capture format is real; sets it as the shared
        // .Current so the desktop visualizer reads its spectrum. Carry-forward B: torn down on
        // stop so we don't leave a stale .Current — game-music Load() resets .Current on resume.
        private VisualizationDataProvider EnsurePumpViz(SpotifyLoopbackClient client)
        {
            lock (_stateLock)
            {
                if (_client != client) return null; // capture changed under us
                if (_effecting) return null;        // effects mode owns the viz — don't shadow it
                if (_pumpViz != null)
                {
                    // Re-assert .Current: an effects->viz-only flip leaves it on the DISPOSED external viz.
                    if (VisualizationDataProvider.Current != _pumpViz)
                        VisualizationDataProvider.Current = _pumpViz;
                    return _pumpViz;
                }
                if (_provider == null) _provider = new SpotifyCaptureSampleProvider(client);
                int fftSize = _getSettings()?.VizFftSize ?? 1024;
                _pumpViz = new VisualizationDataProvider(_provider, fftSize, _getSettings());
                VisualizationDataProvider.Current = _pumpViz;
                return _pumpViz;
            }
        }

        private void DisposePumpViz()
        {
            if (_pumpViz == null) return;
            if (VisualizationDataProvider.Current == _pumpViz)
                VisualizationDataProvider.Current = null;
            try { _pumpViz.Dispose(); } catch { }
            _pumpViz = null;
        }
    }
}
