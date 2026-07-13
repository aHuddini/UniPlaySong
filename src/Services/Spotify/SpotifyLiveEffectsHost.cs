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
                stopEffectedOutput: StopEffectedOutput);
        }

        // Public entry points — always serialized so concurrent triggers (settings, Spotify
        // state, watchdog) never interleave coordinator state transitions.
        public void Evaluate()
        {
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

        // --- coordinator collaborator funcs (all called under _gate) ---

        // Carry-forward F: unmute (false) can transiently fail over COM. Retry a few times so a
        // permanently-muted Spotify self-heals; mute (true) is best-effort single attempt (a mute
        // failure just leaves Spotify dry -> the coordinator won't start effected output, safe).
        private bool SetMuted(bool muted)
        {
            if (muted) return SpotifyAudioSession.SetMuted(true);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (SpotifyAudioSession.SetMuted(false)) return true;
                Thread.Sleep(30);
            }
            _fileLogger?.Warn("[SpotifyFx] UNMUTE FAILED after retries — Spotify may be left muted; user can unmute in the Windows Volume Mixer.");
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
                lock (_stateLock)
                {
                    if (_client == null) throw new InvalidOperationException("capture not started");
                    var player = _getPlayer() as NAudioMusicPlayer;
                    if (player == null) throw new InvalidOperationException("effected output requires the NAudio backend");

                    _provider = new SpotifyCaptureSampleProvider(_client);
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
            while (_workerRun)
            {
                SpotifyLoopbackClient client;
                bool effecting;
                lock (_stateLock) { client = _client; effecting = _effecting; }

                if (client == null) break;

                // Watchdog: capture died while we still think we're capturing -> unmute + re-eval.
                // Runs off-thread so we don't re-enter our own _gate lock from inside StopCapture.
                if (!client.IsCapturing)
                {
                    _fileLogger?.Warn("[SpotifyFx] capture died — unmuting + re-evaluating");
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
                if (_pumpViz != null) return _pumpViz;
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
