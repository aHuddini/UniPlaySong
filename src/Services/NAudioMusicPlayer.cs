using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using Playnite.SDK;
using UniPlaySong.Audio;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // NAudio-based music player with persistent mixer architecture.
    // A single WaveOutEvent + MixingSampleProvider lives for the lifetime of the player.
    // Songs are swapped via AddMixerInput/RemoveMixerInput — no device create/destroy per song.
    // This eliminates the ~70ms UI-thread freeze from WaveOutEvent lifecycle on every game switch.
    public class NAudioMusicPlayer : IMusicPlayer, IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "NAudioPlayer";
        private FileLogger _fileLogger;

        private readonly SettingsService _settingsService;
        private bool _isDisposed;

        // Fixed mixer format — all songs resampled to match
        private static readonly WaveFormat MixerFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        // When true, Load() won't set VisualizationDataProvider.Current (for multi-instance support)
        public bool SuppressVisualizationProvider { get; set; }

        // Persistent infrastructure (created once on first Load, never stopped until Dispose)
        private WaveOutEvent _outputDevice;
        private MixingSampleProvider _mixer;
        private SmoothVolumeSampleProvider _volumeProvider;
        private bool _persistentLayerInitialized;

        // Per-song chain (created on Load, removed on Close)
        // WaveStream base type supports both AudioFileReader (MP3/WAV/FLAC) and VorbisWaveReader (OGG)
        private WaveStream _audioFile;
        private EffectsChain _effectsChain;
        private VisualizationDataProvider _visualizationProvider;
        private SongEndDetectorSampleProvider _songEndDetector;
        private ISampleProvider _mixerInput; // Final provider added to mixer (after format normalization)
        private bool _isInMixer;

        // Preloaded file reader — created during fade-out to reduce Load() time
        private WaveStream _preloadedAudioFile;
        private string _preloadedPath;

        // Logical state (replaces PlaybackState checks since WaveOutEvent never stops)
        private bool _isPlaying;
        private bool _logicallyPaused;
        private TimeSpan _pausedPosition;

        // GME async-resume state. When a background gme_seek is running, _seekInFlightTarget
        // holds the position it's seeking to so Pause() can save it without blocking on
        // GmeReader's internal lock (which the seek task owns). _seekCallbacks collects any
        // onReady callbacks from additional Resume() invocations that arrive while the
        // seek is in flight at the same target — all of them fire when the seek completes,
        // unless a Pause arrived during the seek (in which case the callbacks are discarded;
        // the next Resume will kick a fresh seek and fire its own onReady).
        // Guarded by _seekStateLock because Pause/Resume run on the UI thread while the
        // seek's completion dispatch marshals back to the UI thread via BeginInvoke.
        private readonly object _seekStateLock = new object();
        private TimeSpan? _seekInFlightTarget;
        private System.Collections.Generic.List<Action> _seekCallbacks;

        public event EventHandler MediaEnded;
        public event EventHandler<ExceptionEventArgs> MediaFailed;

        public double Volume
        {
            get => _volumeProvider?.Volume ?? 0;
            set
            {
                if (_volumeProvider != null)
                {
                    _fileLogger?.Debug($"[NAudio] Volume SET: {_volumeProvider.Volume:F4} → {value:F4} (instant, cancels ramp)");
                    _volumeProvider.Volume = (float)value;
                }
            }
        }

        public bool IsLoaded { get; private set; }

        // IsActive: true when playing or logically paused (so fader takes Resume path, not Play)
        public bool IsActive => _isPlaying || _logicallyPaused;

        public TimeSpan? CurrentTime => _audioFile?.CurrentTime;
        public TimeSpan? TotalTime => _audioFile?.TotalTime;

        public string Source { get; private set; }

        public NAudioMusicPlayer(SettingsService settingsService, FileLogger fileLogger = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _fileLogger = fileLogger;
        }

        // Creates the persistent mixer + WaveOutEvent on first use.
        // Mixer uses ReadFully=true so it outputs silence when no inputs are connected.
        private void EnsurePersistentLayer()
        {
            if (_persistentLayerInitialized) return;

            var sw = Stopwatch.StartNew();

            _mixer = new MixingSampleProvider(MixerFormat) { ReadFully = true };

            _volumeProvider = new SmoothVolumeSampleProvider(
                _mixer,
                getFadeInCurve: () => _settingsService.Current?.NaudioFadeInCurve ?? FadeCurveType.Quadratic,
                getFadeOutCurve: () => _settingsService.Current?.NaudioFadeOutCurve ?? FadeCurveType.Cubic);

            _outputDevice = new WaveOutEvent();
            _outputDevice.PlaybackStopped += OnPlaybackStopped;
            _outputDevice.Init(_volumeProvider);
            _outputDevice.Play(); // Starts once, runs forever outputting silence until inputs added

            _persistentLayerInitialized = true;

            sw.Stop();
            _fileLogger?.Debug($"[NAudio] EnsurePersistentLayer: {sw.ElapsedMilliseconds}ms (mixer+volume+device+play)");
        }

        // Error recovery: tears down the persistent layer so next Load() rebuilds it
        private void TearDownPersistentLayer()
        {
            try
            {
                if (_outputDevice != null)
                {
                    _outputDevice.PlaybackStopped -= OnPlaybackStopped;
                    _outputDevice.Stop();
                    _outputDevice.Dispose();
                    _outputDevice = null;
                }
                _mixer = null;
                _volumeProvider = null;
                _persistentLayerInitialized = false;
                _fileLogger?.Debug("[NAudio] TearDownPersistentLayer complete");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Error tearing down persistent layer");
            }
        }

        // Creates the appropriate WaveStream reader based on file extension.
        // OGG uses OggFileReader (NVorbis); GME formats use GmeReader; all others use AudioFileReader.
        private static WaveStream CreateAudioReader(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            if (ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
                return new OggFileReader(filePath);
            if (GmeNative.IsGmeExtension(ext))
                return new GmeReader(filePath);
            return new AudioFileReader(filePath);
        }

        public void PreLoad(string filePath)
        {
            try
            {
                var sw = Stopwatch.StartNew();

                if (_preloadedAudioFile != null)
                {
                    _preloadedAudioFile.Dispose();
                    _preloadedAudioFile = null;
                    _preloadedPath = null;
                }

                if (!string.IsNullOrEmpty(filePath))
                {
                    _preloadedAudioFile = CreateAudioReader(filePath);
                    _preloadedPath = filePath;
                }

                sw.Stop();
                _fileLogger?.Debug($"[NAudio] PreLoad: {sw.ElapsedMilliseconds}ms — {System.IO.Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Failed to preload: {filePath}");
                _preloadedAudioFile = null;
                _preloadedPath = null;
            }
        }

        public void Load(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Logger.Warn($"[{LogPrefix}] Load called with null or empty file path");
                return;
            }

            try
            {
                var sw = Stopwatch.StartNew();

                RemoveCurrentSongChain();
                long removeMs = sw.ElapsedMilliseconds;

                EnsurePersistentLayer();
                long persistMs = sw.ElapsedMilliseconds;

                // Use preloaded AudioFileReader if path matches, otherwise load fresh
                bool usedPreload = false;
                if (_preloadedAudioFile != null && _preloadedPath == filePath)
                {
                    _audioFile = _preloadedAudioFile;
                    _preloadedAudioFile = null;
                    _preloadedPath = null;
                    usedPreload = true;
                }
                else
                {
                    if (_preloadedAudioFile != null)
                    {
                        _preloadedAudioFile.Dispose();
                        _preloadedAudioFile = null;
                        _preloadedPath = null;
                    }
                    _audioFile = CreateAudioReader(filePath);
                }
                long readerMs = sw.ElapsedMilliseconds;

                _effectsChain = new EffectsChain((ISampleProvider)_audioFile, _settingsService);
                long chainMs = sw.ElapsedMilliseconds;

                int fftSize = _settingsService.Current?.VizFftSize ?? 1024;
                _visualizationProvider = new VisualizationDataProvider(_effectsChain, fftSize, _settingsService.Current);
                if (!SuppressVisualizationProvider)
                    VisualizationDataProvider.Current = _visualizationProvider;
                long vizMs = sw.ElapsedMilliseconds;

                // Song end detection — fires when source returns fewer samples than requested
                _songEndDetector = new SongEndDetectorSampleProvider(_visualizationProvider);
                _songEndDetector.SongEnded += OnSongEnded;

                // Format normalization for mixer compatibility (44100Hz stereo float)
                ISampleProvider normalized = _songEndDetector;

                // Mono → stereo
                if (normalized.WaveFormat.Channels == 1)
                {
                    normalized = new MonoToStereoSampleProvider(normalized);
                }

                // Resample if sample rate doesn't match mixer
                if (normalized.WaveFormat.SampleRate != MixerFormat.SampleRate)
                {
                    normalized = new WdlResamplingSampleProvider(normalized, MixerFormat.SampleRate);
                }

                _mixerInput = normalized;
                long normalizeMs = sw.ElapsedMilliseconds;

                Source = filePath;
                IsLoaded = true;
                _logicallyPaused = false;

                sw.Stop();
                _fileLogger?.Debug($"[NAudio] Load: {sw.ElapsedMilliseconds}ms total (Remove={removeMs}, Persist={persistMs - removeMs}, Reader={readerMs - persistMs}{(usedPreload ? " PRELOADED" : "")}, Chain={chainMs - readerMs}, Viz={vizMs - chainMs}, Normalize={normalizeMs - vizMs}) — {System.IO.Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Failed to load: {filePath}");
                RemoveCurrentSongChain();
                MediaFailed?.Invoke(this, null);
            }
        }

        public void Play()
        {
            try
            {
                if (_mixerInput == null || _mixer == null) return;

                // If the audio has reached the end, seek back to the beginning (for looping)
                if (_audioFile != null && _audioFile.CurrentTime >= _audioFile.TotalTime - TimeSpan.FromMilliseconds(100))
                {
                    _audioFile.CurrentTime = TimeSpan.Zero;
                    _songEndDetector?.Reset();
                }

                if (!_isInMixer)
                {
                    _mixer.AddMixerInput(_mixerInput);
                    _isInMixer = true;
                }

                _isPlaying = true;
                _logicallyPaused = false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Failed to play");
                MediaFailed?.Invoke(this, null);
            }
        }

        public void Play(TimeSpan startFrom)
        {
            try
            {
                if (_audioFile != null)
                {
                    _audioFile.CurrentTime = startFrom;
                }
                Play();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Failed to play from {startFrom}");
                MediaFailed?.Invoke(this, null);
            }
        }

        // Logical pause — song stays in mixer outputting silence (volume already 0 from fader).
        // Position is saved so resume can seek back (song advances at vol=0 otherwise).
        public void Pause()
        {
            // If a GME background seek is running, reading _audioFile.CurrentTime would
            // block the UI thread on GmeReader's internal lock until the seek finishes
            // (hundreds of ms to seconds). Use the known seek target as the paused
            // position instead — when the seek completes the emu will be there anyway.
            TimeSpan? inFlightTarget;
            lock (_seekStateLock)
            {
                inFlightTarget = _seekInFlightTarget;
            }

            if (inFlightTarget.HasValue)
            {
                _pausedPosition = inFlightTarget.Value;
                _fileLogger?.Debug($"[NAudio] Pause() — logical pause (seek in flight), pos={_pausedPosition} (from seek target), isInMixer={_isInMixer}, isActive={IsActive}");
            }
            else
            {
                _pausedPosition = _audioFile?.CurrentTime ?? TimeSpan.Zero;
                var totalTime = _audioFile?.TotalTime ?? TimeSpan.Zero;
                var remaining = totalTime - _pausedPosition;
                bool atOrPastEof = _audioFile != null && remaining <= TimeSpan.FromMilliseconds(500);
                _fileLogger?.Debug($"[NAudio] Pause() — logical pause, pos={_pausedPosition}, total={totalTime}, remaining={remaining.TotalMilliseconds:F0}ms, atOrPastEof={atOrPastEof}, isInMixer={_isInMixer}, isActive={IsActive}, vol={_volumeProvider?.Volume:F4}, isRamping={_volumeProvider?.IsRamping}");
            }

            // For GME: detach the mixer input so the audio thread stops calling gme_play().
            // Without this, the emulator keeps advancing at real-time speed during the
            // pause, and Resume's gme_seek back to _pausedPosition becomes an expensive
            // backward-seek (rewind to track start + fast-forward — 6+ seconds on long
            // tracks). Detaching freezes the emu at _pausedPosition so Resume can re-add
            // the input with no seek required (or a trivial no-op seek).
            if (_audioFile is GmeReader)
            {
                RemoveSongFromMixer();
            }

            _logicallyPaused = true;
            _isPlaying = false;
        }

        // Logical resume — seeks back to saved position, then the fader ramps volume up.
        // If the song ended while paused (short track EOF), re-adds to mixer from saved position.
        //
        // For GmeReader the seek is offloaded to a background thread: gme_seek() replays
        // emulation from track start to the target position and can block the caller for
        // many seconds on a long track — doing that on the UI thread freezes Playnite.
        // In that case this method returns immediately; onReady fires on the UI thread
        // when the seek completes and the mixer input is back in place.
        public void Resume(Action onReady = null)
        {
            var totalTimeBefore = _audioFile?.TotalTime ?? TimeSpan.Zero;
            var remainingBefore = totalTimeBefore - _pausedPosition;
            bool atOrPastEof = _audioFile != null && remainingBefore <= TimeSpan.FromMilliseconds(500);
            _fileLogger?.Debug($"[NAudio] Resume() — seeking to {_pausedPosition}, total={totalTimeBefore}, remaining={remainingBefore.TotalMilliseconds:F0}ms, atOrPastEof={atOrPastEof}, vol={_volumeProvider?.Volume:F4}, isRamping={_volumeProvider?.IsRamping}, isInMixer={_isInMixer}");

            if (_audioFile == null)
            {
                _logicallyPaused = false;
                _isPlaying = true;
                onReady?.Invoke();
                return;
            }

            // Fast path: non-GME readers seek instantly (buffer pointer update).
            if (!(_audioFile is GmeReader))
            {
                _audioFile.CurrentTime = _pausedPosition;

                if (!_isInMixer && _mixerInput != null && _mixer != null)
                {
                    _songEndDetector?.Reset();
                    _mixer.AddMixerInput(_mixerInput);
                    _isInMixer = true;
                    _fileLogger?.Debug("[NAudio] Resume() — re-added to mixer after EOF during pause");
                }

                _fileLogger?.Debug($"[NAudio] Resume() — after seek: actualPos={_audioFile.CurrentTime}, isInMixer={_isInMixer}");
                _logicallyPaused = false;
                _isPlaying = true;
                onReady?.Invoke();
                return;
            }

            // GME path.
            var gmeReader = (GmeReader)_audioFile;
            var targetPosition = _pausedPosition;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;

            // Coalesce check first: if a seek to the same target is already in flight
            // (rapid pause-resume-pause-resume while GME is mid-seek), don't kick a
            // second Task or try the fast-path (reading CurrentTime would block on the
            // GME lock anyway). Append onReady to the existing callback list.
            lock (_seekStateLock)
            {
                if (_seekInFlightTarget.HasValue && _seekInFlightTarget.Value == targetPosition)
                {
                    _logicallyPaused = false;
                    _isPlaying = true;
                    if (onReady != null)
                    {
                        if (_seekCallbacks == null)
                            _seekCallbacks = new System.Collections.Generic.List<Action>();
                        _seekCallbacks.Add(onReady);
                    }
                    _fileLogger?.Debug($"[NAudio] Resume() — GME seek to {targetPosition} already in flight, queuing onReady");
                    return;
                }
            }

            // Fast path: the emu is already at (or within 100ms of) the target. This is
            // the common case now that Pause() detaches the mixer input — the emu was
            // frozen at _pausedPosition, so no seek is needed. Just re-add to the mixer
            // and invoke onReady synchronously. No Task.Run, no UI stutter, no fade-in
            // delay. Non-trivial seeks (user scrub, future seek-to-position APIs) still
            // fall through to the background path.
            var currentEmuPosition = gmeReader.CurrentTime;
            var drift = Math.Abs((currentEmuPosition - targetPosition).TotalMilliseconds);
            if (drift < 100)
            {
                if (!_isInMixer && _mixerInput != null && _mixer != null)
                {
                    _songEndDetector?.Reset();
                    _mixer.AddMixerInput(_mixerInput);
                    _isInMixer = true;
                }
                _logicallyPaused = false;
                _isPlaying = true;
                _fileLogger?.Debug($"[NAudio] Resume() — GME fast path: emu already at {currentEmuPosition} (target {targetPosition}, drift {drift:F0}ms), skipping seek");
                onReady?.Invoke();
                return;
            }

            // Slow path: an actual seek is needed (target is not where the emu currently
            // is). Pull the input out of the mixer, run gme_seek on a thread-pool thread,
            // then re-add on the UI thread and invoke onReady so the fader can start fade-in.
            lock (_seekStateLock)
            {
                if (_seekInFlightTarget.HasValue && _seekInFlightTarget.Value == targetPosition)
                {
                    _logicallyPaused = false;
                    _isPlaying = true;
                    if (onReady != null)
                    {
                        if (_seekCallbacks == null)
                            _seekCallbacks = new System.Collections.Generic.List<Action>();
                        _seekCallbacks.Add(onReady);
                    }
                    _fileLogger?.Debug($"[NAudio] Resume() — GME seek to {targetPosition} already in flight, queuing onReady");
                    return;
                }

                _seekInFlightTarget = targetPosition;
                if (_seekCallbacks == null)
                    _seekCallbacks = new System.Collections.Generic.List<Action>();
                if (onReady != null)
                    _seekCallbacks.Add(onReady);
            }

            var seekSw = Stopwatch.StartNew();

            RemoveSongFromMixer();
            _logicallyPaused = false;
            _isPlaying = true;

            _fileLogger?.Debug($"[NAudio] Resume() — GME detected, starting background seek to {targetPosition}");

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    gmeReader.CurrentTime = targetPosition;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"[{LogPrefix}] Background GME seek failed");
                }

                Action finalize = () =>
                {
                    seekSw.Stop();
                    System.Collections.Generic.List<Action> callbacks;
                    lock (_seekStateLock)
                    {
                        _seekInFlightTarget = null;
                        callbacks = _seekCallbacks;
                        _seekCallbacks = null;
                    }

                    // If a Pause arrived during the seek, the queued onReady callbacks
                    // are stale — they belong to a Resume intent that has been superseded.
                    // Firing them would call the fader's FadeIn against a detached mixer
                    // input (no audio) and, worse, poison the fader's _isPaused state so
                    // the NEXT Resume takes the "reversing incomplete fade-out" branch and
                    // never actually re-adds the mixer input. Discard the callbacks and
                    // leave the player in the paused state — the next Resume will kick a
                    // fresh seek and its onReady will fire normally.
                    if (_logicallyPaused)
                    {
                        _fileLogger?.Debug($"[NAudio] Resume() — GME background seek done in {seekSw.ElapsedMilliseconds}ms but Pause arrived mid-seek; discarding {callbacks?.Count ?? 0} stale onReady callback(s), waiting for next Resume");
                        return;
                    }

                    try
                    {
                        if (_mixerInput != null && _mixer != null && !_isInMixer)
                        {
                            _songEndDetector?.Reset();
                            _mixer.AddMixerInput(_mixerInput);
                            _isInMixer = true;
                        }
                        _fileLogger?.Debug($"[NAudio] Resume() — GME background seek done in {seekSw.ElapsedMilliseconds}ms, actualPos={_audioFile?.CurrentTime}, isInMixer={_isInMixer}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"[{LogPrefix}] Error re-adding mixer input after GME seek");
                    }
                    finally
                    {
                        if (callbacks != null)
                        {
                            foreach (var cb in callbacks)
                            {
                                try { cb(); }
                                catch (Exception ex) { Logger.Error(ex, $"[{LogPrefix}] Resume onReady callback failed"); }
                            }
                        }
                    }
                };

                if (dispatcher != null)
                    dispatcher.BeginInvoke(finalize);
                else
                    finalize();
            });
        }

        public void Stop()
        {
            try
            {
                RemoveSongFromMixer();
                _isPlaying = false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Failed to stop");
            }
        }

        public void Close()
        {
            try
            {
                var sw = Stopwatch.StartNew();

                RemoveCurrentSongChain();

                sw.Stop();
                _fileLogger?.Debug($"[NAudio] Close: {sw.ElapsedMilliseconds}ms");

                IsLoaded = false;
                Source = null;
                _isPlaying = false;
                _logicallyPaused = false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Error during close");
            }
        }

        public void SetVolumeRamp(double targetVolume, double durationSeconds)
        {
            _fileLogger?.Debug($"[NAudio] SetVolumeRamp({targetVolume:F4}, {durationSeconds:F3}s) — before: {_volumeProvider?.DiagSnapshot}");
            _volumeProvider?.SetTargetWithRamp((float)targetVolume, (float)durationSeconds);
        }

        // Removes current song chain from mixer and disposes per-song resources.
        // Does NOT touch the persistent layer (device/mixer/volume provider).
        private void RemoveCurrentSongChain()
        {
            RemoveSongFromMixer();

            if (_songEndDetector != null)
            {
                _songEndDetector.SongEnded -= OnSongEnded;
                _songEndDetector = null;
            }

            _effectsChain = null;

            if (VisualizationDataProvider.Current == _visualizationProvider)
                VisualizationDataProvider.Current = null;
            _visualizationProvider?.Dispose();
            _visualizationProvider = null;

            _mixerInput = null;

            if (_audioFile != null)
            {
                _audioFile.Dispose();
                _audioFile = null;
            }
        }

        // Guarded removal from mixer — only calls RemoveMixerInput if actually in mixer
        private void RemoveSongFromMixer()
        {
            if (_isInMixer && _mixerInput != null && _mixer != null)
            {
                try
                {
                    _mixer.RemoveMixerInput(_mixerInput);
                }
                catch (Exception ex)
                {
                    _fileLogger?.Debug($"[NAudio] RemoveSongFromMixer: {ex.Message}");
                }
                _isInMixer = false;
            }
        }

        // Called on the audio thread when song reaches EOF.
        // MixingSampleProvider auto-removes the input on partial read (read < count),
        // so _isInMixer must be set false here. MediaEnded is marshaled to the UI thread.
        // Also clears _logicallyPaused so IsActive returns false — this lets the fader's
        // stall detection kick in if the song ended during a fade-out pause.
        private void OnSongEnded()
        {
            _isInMixer = false;
            _isPlaying = false;
            _logicallyPaused = false;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        MediaEnded?.Invoke(this, EventArgs.Empty);
                    }));
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Error dispatching MediaEnded from SongEndDetector");
            }
        }

        // Device-level error (hardware disconnect, driver crash, etc.)
        // Tears down persistent layer so next Load() rebuilds it fresh.
        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Logger.Error(e.Exception, $"[{LogPrefix}] Playback stopped with error — tearing down persistent layer");
                TearDownPersistentLayer();
                MediaFailed?.Invoke(this, null);
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                RemoveCurrentSongChain();
                TearDownPersistentLayer();

                if (_preloadedAudioFile != null)
                {
                    _preloadedAudioFile.Dispose();
                    _preloadedAudioFile = null;
                    _preloadedPath = null;
                }

                _isDisposed = true;
            }
        }

        // Thin wrapper that detects when the source returns fewer samples than requested (EOF).
        // MixingSampleProvider auto-removes inputs on partial reads (read < count), NOT on read == 0.
        // So the detector must fire on the partial read — the zero-read is unreachable after removal.
        private class SongEndDetectorSampleProvider : ISampleProvider
        {
            private readonly ISampleProvider _source;
            private bool _ended;
            public event Action SongEnded;
            public WaveFormat WaveFormat => _source.WaveFormat;

            public SongEndDetectorSampleProvider(ISampleProvider source)
            {
                _source = source ?? throw new ArgumentNullException(nameof(source));
            }

            // Reset for looping — allows the detector to fire again on next EOF
            public void Reset() { _ended = false; }

            public int Read(float[] buffer, int offset, int count)
            {
                if (_ended) return 0;
                int read = _source.Read(buffer, offset, count);
                if (read < count && !_ended)
                {
                    _ended = true;
                    SongEnded?.Invoke();
                }
                return read;
            }
        }
    }
}
