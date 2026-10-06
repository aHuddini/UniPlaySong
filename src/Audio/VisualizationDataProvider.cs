using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using NAudio.Dsp;
using NAudio.Wave;

namespace UniPlaySong.Audio
{
    // Audio visualization tap: circular buffer -> FFT thread -> double-buffered spectrum for UI.
    //
    // The output device pulls audio in 150 ms blocks and keeps two queued, so a block passes through here ~350 ms
    // before it is heard (measured with WASAPI loopback). By default the bars and levels follow the newest block, as
    // they always have. The experimental "alternative audio-reactive visualizer sync" setting (needs an OutputClock)
    // instead ends the analysis window at the frame the device is playing now and slides it ~45 times a second
    // through the queued audio. Read live, so toggling it takes effect without reloading the song.
    public class VisualizationDataProvider : ISampleProvider, IDisposable
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private readonly Func<UniPlaySongSettings> _getSettings;
        private UniPlaySongSettings Settings => _getSettings?.Invoke();
        private readonly OutputClock _clock;

        private bool Synced => _clock != null && (Settings?.AlternativeAudioReactiveVisualizerSync ?? false);

        // Synced-update state (FFT thread only)
        private int _lastEnd = int.MinValue;
        private long _lastUpdateTick;

        // Circular buffers for left and right (written by audio thread, read by FFT thread). Large enough for the
        // device queue plus one incoming block plus the level window, at 44.1 kHz.
        private const int BufferSize = 32768;
        private const int MaxIncomingBlock = 6615; // the device's 150 ms block (WaveOutEvent default: 300 ms in two)
        private readonly float[] _left = new float[BufferSize];
        private readonly float[] _right = new float[BufferSize];

        // Write position and the device frame the newest sample corresponds to; published together under the lock
        // so the FFT thread never pairs one block's position with another's.
        private readonly object _publishLock = new object();
        private int _writePos;
        private long _deviceFrameAtWriteEnd;

        // Peak/RMS window ending at the playing frame: 150 ms, the block the levels have always covered.
        private const int LevelWindow = 6615;
        private readonly int _maxLag;

        // Alphas are per step and scaled to the real time between updates, so the look doesn't depend on update rate.
        // Rise is per FFT hop (~23 ms): a hit lands within one update. Fall is per ~49 ms, the pace the fall settings
        // were tuned against: the old loop woke for each 150 ms output block and on a 50 ms timeout in between, so it
        // smoothed three times per block (the "43 fps" in the old settings text was never true). Measured on a
        // synthetic kick, bass now clears between hits as it did then.
        private const double RiseStepMs = 1024 * 1000.0 / 44100;
        private const double FallStepMs = 147.0 / 3;
        private const int FrameIntervalMs = 16;
        // Windows sleeps in 15.6 ms ticks by default; a 16 ms wait rounds up to two of them (~31 Hz), 15 ms to one.
        private const int FrameWaitMs = 15;

        // Audio the device reports as played still has ~40 ms to go before Windows mixes it (the audio engine's own
        // buffer, below what waveOut's position counts). Measured with WASAPI loopback: 31-55 ms early without this.
        // ponytail: one constant from one machine; a user-facing sync offset if other hardware (Bluetooth) needs it.
        private const int DeviceLatencyFrames = 44100 * 40 / 1000;

        // Per-update levels (written by FFT thread, read by UI)
        private volatile float _currentPeak;
        private volatile float _currentRms;
        private volatile float _currentPeakL, _currentPeakR;
        private volatile float _currentRmsL, _currentRmsR;

        // FFT configuration — set at construction, immutable thereafter.
        // Supported sizes: 512 (~86Hz/bin, ~11.6ms), 1024 (~43Hz/bin, ~23ms), 2048 (~21.5Hz/bin, ~46ms)
        private readonly int _fftSize;
        private readonly int _fftLog2;
        private readonly int _spectrumSize;

        // FFT state (owned exclusively by the background thread)
        private readonly Complex[] _fftBuffer;
        private readonly float[] _hannWindow;
        private readonly float[] _smoothedSpectrum;
        private readonly float[] _powerSum; // summed power per bin across the windows of one update

        // Windows per update: enough for ~60 ms of new audio at 1024 (a slow tick); a longer gap (resume, a stall)
        // just analyses the newest stretch.
        private const int MaxWindowsPerUpdate = 12;

        // Per-bin temporal smoothing: bass gets slightly slower smoothing (weighty), treble gets faster smoothing
        // (sparkly). Linearly interpolated across bins. Alphas are scaled based on FFT size — larger windows need
        // higher alphas to compensate for less-frequent updates.
        private readonly float[] _riseAlpha;
        private readonly float[] _fallAlpha;

        // Cached alpha inputs — skip RecomputeAlphas when settings haven't changed
        // Initialized to -1 so the first call always computes
        private int _cachedRiseLow = -1, _cachedRiseHigh = -1, _cachedFallLow = -1, _cachedFallHigh = -1;

        // Double-buffered spectrum output
        private float[] _spectrumFront;
        private float[] _spectrumBack;

        // Background thread control
        private readonly Thread _fftThread;
        private volatile bool _disposed;
        private readonly ManualResetEventSlim _newSamplesSignal = new ManualResetEventSlim(false);

        // Fullscreen FFT gate: when paused, the FFT thread still wakes on audio signals
        // but skips the expensive computation (FFT + smoothing + buffer swap).
        // Audio passthrough (Read()) is unaffected — only the spectrum analysis is skipped.
        //
        // Currently used to save CPU in fullscreen mode where the desktop visualizer is not visible.
        // When fullscreen visualizer support is implemented, this gate should be made
        // mode-aware (e.g., pause only if no fullscreen visualizer is subscribed).
        private volatile bool _paused;
        public bool Paused { get => _paused; set => _paused = value; }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int FftSize => _fftSize; // UI reads this to configure matching bin ranges

        public int SpectrumSize => _spectrumSize; // Half FFT size = usable spectrum bins

        public VisualizationDataProvider(ISampleProvider source, int fftSize = 1024, UniPlaySongSettings settings = null)
            : this(source, fftSize, settings == null ? null : (Func<UniPlaySongSettings>)(() => settings), null)
        {
        }

        // Settings read live: a settings save replaces the settings object, and toggles should apply mid-song.
        public VisualizationDataProvider(ISampleProvider source, int fftSize, Func<UniPlaySongSettings> getSettings,
            OutputClock clock)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _channels = source.WaveFormat.Channels;
            _getSettings = getSettings;
            _clock = clock;

            // Validate and set FFT size
            if (fftSize <= 512) { _fftSize = 512; _fftLog2 = 9; }
            else if (fftSize <= 1024) { _fftSize = 1024; _fftLog2 = 10; }
            else { _fftSize = 2048; _fftLog2 = 11; }
            _spectrumSize = _fftSize / 2;
            _maxLag = BufferSize - Math.Max(_fftSize, LevelWindow) - MaxIncomingBlock;

            // Allocate FFT arrays
            _fftBuffer = new Complex[_fftSize];
            _hannWindow = new float[_fftSize];
            _smoothedSpectrum = new float[_spectrumSize];
            _powerSum = new float[_spectrumSize];
            _riseAlpha = new float[_spectrumSize];
            _fallAlpha = new float[_spectrumSize];
            _spectrumFront = new float[_spectrumSize];
            _spectrumBack = new float[_spectrumSize];

            // Precompute Hann window
            double twoPiOverN = 2.0 * Math.PI / (_fftSize - 1);
            for (int i = 0; i < _fftSize; i++)
                _hannWindow[i] = (float)(0.5 * (1.0 - Math.Cos(twoPiOverN * i)));

            // Compute initial per-bin smoothing alphas
            RecomputeAlphas();

            // Start background FFT thread
            _fftThread = new Thread(FftLoop)
            {
                Name = "UniPlaySong-FFT",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _fftThread.Start();
        }

        // Recomputes per-bin rise/fall alphas from settings; skips if unchanged
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecomputeAlphas()
        {
            // Read current settings (cheap int reads)
            int riseLow = Settings?.VizFftRiseLow ?? 88;
            int riseHigh = Settings?.VizFftRiseHigh ?? 93;
            int fallLow = Settings?.VizFftFallLow ?? 50;
            int fallHigh = Settings?.VizFftFallHigh ?? 65;

            // Skip if nothing changed since last computation
            if (riseLow == _cachedRiseLow && riseHigh == _cachedRiseHigh &&
                fallLow == _cachedFallLow && fallHigh == _cachedFallHigh)
                return;

            _cachedRiseLow = riseLow;
            _cachedRiseHigh = riseHigh;
            _cachedFallLow = fallLow;
            _cachedFallHigh = fallHigh;

            // Scale alphas based on FFT size: larger windows update less frequently,
            // so need higher alphas (faster convergence per update).
            float sizeScale = _fftSize == 512 ? -0.05f : _fftSize == 2048 ? 0.12f : 0f;
            float riseAlphaLow = Math.Min(riseLow / 100f + sizeScale, 0.95f);
            float riseAlphaHigh = Math.Min(riseHigh / 100f + sizeScale, 0.95f);
            float fallAlphaLow = Math.Min(fallLow / 100f + sizeScale, 0.60f);
            float fallAlphaHigh = Math.Min(fallHigh / 100f + sizeScale, 0.75f);

            for (int i = 0; i < _spectrumSize; i++)
            {
                float t = (float)i / (_spectrumSize - 1);
                _riseAlpha[i] = riseAlphaLow + (riseAlphaHigh - riseAlphaLow) * t;
                _fallAlpha[i] = fallAlphaLow + (fallAlphaHigh - fallAlphaLow) * t;
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            // Read before the source: the device frame this block ends on is everything already submitted plus it.
            long submittedBefore = _clock?.FramesSubmitted ?? 0;

            int samplesRead = _source.Read(buffer, offset, count);
            if (samplesRead == 0) return 0;

            float peakL = 0f, peakR = 0f;
            float sumSqL = 0f, sumSqR = 0f;
            int pos;
            lock (_publishLock) pos = _writePos;
            int frames = 0;
            for (int i = 0; i < samplesRead; i += _channels)
            {
                float left = buffer[offset + i];
                float right = (_channels == 2 && i + 1 < samplesRead) ? buffer[offset + i + 1] : left;

                float absL = left > 0 ? left : -left;
                float absR = right > 0 ? right : -right;
                if (absL > peakL) peakL = absL;
                if (absR > peakR) peakR = absR;
                sumSqL += left * left;
                sumSqR += right * right;

                _left[pos & (BufferSize - 1)] = left;
                _right[pos & (BufferSize - 1)] = right;
                pos++;
                frames++;
            }

            lock (_publishLock)
            {
                _writePos = pos;
                _deviceFrameAtWriteEnd = submittedBefore + frames;
            }

            // Long-standing behavior: levels cover the block just read. Synced, the FFT thread computes them from
            // the window being heard instead.
            if (!Synced)
            {
                _currentPeakL = peakL;
                _currentPeakR = peakR;
                _currentRmsL = (float)Math.Sqrt(sumSqL / frames);
                _currentRmsR = (float)Math.Sqrt(sumSqR / frames);
                _currentPeak = Math.Max(peakL, peakR);
                _currentRms = (float)Math.Sqrt((sumSqL + sumSqR) / (frames * 2));
            }
            _newSamplesSignal.Set();

            return samplesRead;
        }

        // Ring position one past the frame to analyse: the newest frame, held back by however much of this tap's
        // audio the device has not played yet, capped at what the ring can hold.
        internal static int WindowEnd(int writePos, long deviceFrameAtWriteEnd, long framesPlayed, int maxLag)
        {
            long behind = deviceFrameAtWriteEnd - framesPlayed;
            if (behind <= 0) return writePos;
            if (behind > maxLag) behind = maxLag;
            return writePos - (int)behind;
        }

        private void ComputeLevels(int end)
        {
            float peakL = 0f, peakR = 0f, sumSqL = 0f, sumSqR = 0f;
            for (int i = end - LevelWindow; i < end; i++)
            {
                float l = _left[i & (BufferSize - 1)];
                float r = _right[i & (BufferSize - 1)];
                float absL = l > 0 ? l : -l;
                float absR = r > 0 ? r : -r;
                if (absL > peakL) peakL = absL;
                if (absR > peakR) peakR = absR;
                sumSqL += l * l;
                sumSqR += r * r;
            }

            _currentPeakL = peakL;
            _currentPeakR = peakR;
            _currentRmsL = (float)Math.Sqrt(sumSqL / LevelWindow);
            _currentRmsR = (float)Math.Sqrt(sumSqR / LevelWindow);
            _currentPeak = Math.Max(peakL, peakR);
            _currentRms = (float)Math.Sqrt((sumSqL + sumSqR) / (LevelWindow * 2));
        }

        // Fast path: peak and RMS levels (no FFT, near-zero cost)
        public void GetLevels(out float peak, out float rms)
        {
            peak = _currentPeak;
            rms = _currentRms;
        }

        // Stereo peak and RMS levels for L/R channel meters
        public void GetStereoLevels(out float peakL, out float peakR, out float rmsL, out float rmsR)
        {
            peakL = _currentPeakL;
            peakR = _currentPeakR;
            rmsL = _currentRmsL;
            rmsR = _currentRmsR;
        }

        // UI thread: copies pre-calculated spectrum data (just Array.Copy)
        public int GetSpectrumData(float[] destination, int destOffset, int count)
        {
            int toCopy = Math.Min(count, _spectrumSize);
            var front = _spectrumFront;
            Array.Copy(front, 0, destination, destOffset, toCopy);
            return toCopy;
        }

        // Background FFT thread. Timer mode spins to exact 16 ms intervals; otherwise it sleeps until new audio
        // arrives or a timeout: 50 ms for the long-standing behavior, 15 ms when synced (the window slides with
        // playback, not with incoming blocks).
        private void FftLoop()
        {
            long targetTicksPerFrame = FrameIntervalMs * Stopwatch.Frequency / 1000L;
            var sw = new Stopwatch();
            sw.Start();
            long nextFrameTick = sw.ElapsedTicks;

            while (!_disposed)
            {
                bool synced = Synced;
                bool timerMode = Settings?.VizFftTimerMode ?? false;

                if (timerMode)
                {
                    // Timer mode: precise fixed-interval via Stopwatch + SpinWait
                    var spinner = new SpinWait();
                    while (sw.ElapsedTicks < nextFrameTick && !_disposed)
                        spinner.SpinOnce();
                    nextFrameTick = sw.ElapsedTicks + targetTicksPerFrame;
                    // Drain any pending signal to avoid stale wakeups when switching back
                    _newSamplesSignal.Reset();
                }
                else
                {
                    _newSamplesSignal.Wait(synced ? FrameWaitMs : 50);
                    _newSamplesSignal.Reset();
                    // Keep timer in sync so switching to timer mode doesn't cause a burst
                    nextFrameTick = sw.ElapsedTicks + targetTicksPerFrame;
                }

                if (_disposed) break;

                if (synced) SyncedUpdate(sw.ElapsedTicks);
                else LegacyUpdate();
            }
        }

        // The long-standing behavior: the newest 1024 samples, smoothed once per update.
        private void LegacyUpdate()
        {
            _lastEnd = int.MinValue; // a later switch to synced starts fresh
            if (_paused) return; // Fullscreen: skip FFT, audio passthrough unaffected

            int wp;
            lock (_publishLock) wp = _writePos;
            int readPos = wp - _fftSize;

            for (int i = 0; i < _fftSize; i++)
            {
                int p = (readPos + i) & (BufferSize - 1);
                _fftBuffer[i].X = (_left[p] + _right[p]) * 0.5f * _hannWindow[i];
                _fftBuffer[i].Y = 0;
            }

            FastFourierTransform.FFT(true, _fftLog2, _fftBuffer);

            // Recompute alphas from live settings (skips if unchanged — cheap int compare)
            RecomputeAlphas();

            for (int i = 0; i < _spectrumSize; i++)
            {
                float re = _fftBuffer[i].X;
                float im = _fftBuffer[i].Y;
                float raw = ToBarScale(re * re + im * im);

                // Per-bin asymmetric smoothing: bass slower (weighty), treble faster (sparkly)
                float prev = _smoothedSpectrum[i];
                float alpha = raw >= prev ? _riseAlpha[i] : _fallAlpha[i];
                _smoothedSpectrum[i] = prev + (raw - prev) * alpha;
            }

            PublishSpectrum();
        }

        // Alternative sync: the window ends at the frame being heard; levels and bars follow it.
        private void SyncedUpdate(long now)
        {
            int wp;
            long deviceEnd;
            lock (_publishLock)
            {
                wp = _writePos;
                deviceEnd = _deviceFrameAtWriteEnd;
            }
            int end = WindowEnd(wp, deviceEnd, _clock.FramesPlayed - DeviceLatencyFrames, _maxLag);

            // Nothing new has played (paused, stopped, between songs): leave levels and bars where they are.
            if (end == _lastEnd)
            {
                _lastUpdateTick = now;
                return;
            }
            int prevEnd = _lastEnd;
            _lastEnd = end;

            ComputeLevels(end); // meters and glow work in fullscreen too
            if (_paused || prevEnd == int.MinValue) { _lastUpdateTick = now; return; } // Fullscreen: skip FFT

            double elapsedMs = (now - _lastUpdateTick) * 1000.0 / Stopwatch.Frequency;
            double riseSteps = elapsedMs / RiseStepMs;
            double fallSteps = elapsedMs / FallStepMs;
            _lastUpdateTick = now;

            // Energy of everything that played since the last update: the average power of every window a
            // quarter-window apart. One window per update covered ~23 ms of every 22-31 ms, tapered at its edges,
            // so a drum hit landing between two updates came out at half height or not at all (measured:
            // identical clicks varied 2-2.6x). Quarter-step Hann windows weigh every moment about equally, so a
            // hit counts the same wherever it lands. Averaged, not the strongest window: bass cycles are as long
            // as a window, so the strongest window always catches a bass note at its crest and the bars never fall.
            Array.Clear(_powerSum, 0, _spectrumSize);
            int hop = _fftSize / 4;
            int windows = 0;
            for (int k = 0; k < MaxWindowsPerUpdate; k++)
            {
                int windowEnd = end - k * hop;
                if (k > 0 && windowEnd <= prevEnd) break;
                AddWindowPower(windowEnd);
                windows++;
            }

            // Recompute alphas from live settings (skips if unchanged — cheap int compare)
            RecomputeAlphas();

            for (int i = 0; i < _spectrumSize; i++)
            {
                float raw = ToBarScale(_powerSum[i] / windows);

                // Per-bin asymmetric smoothing: bass slower (weighty), treble faster (sparkly)
                float prev = _smoothedSpectrum[i];
                float alpha = raw >= prev ? ScaleAlpha(_riseAlpha[i], riseSteps) : ScaleAlpha(_fallAlpha[i], fallSteps);
                _smoothedSpectrum[i] = prev + (raw - prev) * alpha;
            }

            PublishSpectrum();
        }

        // Publish to front buffer for UI consumption. Copy smoothed state into back buffer, then atomically swap
        // into front. Interlocked.Exchange ensures the UI thread always reads a complete frame.
        private void PublishSpectrum()
        {
            Array.Copy(_smoothedSpectrum, 0, _spectrumBack, 0, _spectrumSize);
            _spectrumBack = Interlocked.Exchange(ref _spectrumFront, _spectrumBack);
        }

        // Power per bin of the window ending at `windowEnd`, added to the update's sum.
        private void AddWindowPower(int windowEnd)
        {
            int readPos = windowEnd - _fftSize;
            for (int i = 0; i < _fftSize; i++)
            {
                int p = (readPos + i) & (BufferSize - 1);
                _fftBuffer[i].X = (_left[p] + _right[p]) * 0.5f * _hannWindow[i];
                _fftBuffer[i].Y = 0;
            }

            FastFourierTransform.FFT(true, _fftLog2, _fftBuffer);

            for (int i = 0; i < _spectrumSize; i++)
            {
                float re = _fftBuffer[i].X;
                float im = _fftBuffer[i].Y;
                _powerSum[i] += re * re + im * im;
            }
        }

        // Power to the 0..1 bar scale: -80..0 dB, squared for dynamic range expansion.
        private static float ToBarScale(float magSq)
        {
            float db = 10f * (float)Math.Log10(Math.Max(magSq, 1e-20f));
            float normalized = (db + 80f) / 80f;
            if (normalized < 0f) normalized = 0f;
            else if (normalized > 1f) normalized = 1f;
            return normalized * normalized;
        }

        // An alpha applied once per smoothing step, rescaled for `steps` of them: n updates of the result cover the
        // same ground as n * steps updates of the original.
        internal static float ScaleAlpha(float alpha, double steps)
        {
            if (steps <= 0) return 0f;
            return (float)(1.0 - Math.Pow(1.0 - alpha, steps));
        }

        public void Dispose()
        {
            _disposed = true;
            _newSamplesSignal.Set(); // wake thread so it can exit
        }

        private static volatile VisualizationDataProvider _current;

        // Static pause flag: applied automatically to each new provider via the Current setter.
        // This ensures newly created providers (one per song) inherit the pause state
        // without requiring the caller to know about the FFT lifecycle.
        private static volatile bool _globalPaused;
        public static bool GlobalPaused
        {
            get => _globalPaused;
            set
            {
                _globalPaused = value;
                var current = _current;
                if (current != null) current.Paused = value;
            }
        }

        public static VisualizationDataProvider Current
        {
            get => _current;
            set
            {
                _current = value;
                if (value != null) value.Paused = _globalPaused;
            }
        }
    }
}
