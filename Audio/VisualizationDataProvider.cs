using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using NAudio.Dsp;
using NAudio.Wave;

namespace UniPlaySong.Audio
{
    /// <summary>
    /// Wraps an ISampleProvider to tap audio samples for visualization.
    /// Audio thread: buffers mono samples into a circular buffer, signals FFT thread.
    /// Background thread: wakes on signal, runs FFT, writes normalized spectrum to double-buffered array.
    /// UI thread: GetSpectrumData() is a simple Array.Copy — zero computation.
    /// </summary>
    public class VisualizationDataProvider : ISampleProvider, IDisposable
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private readonly UniPlaySongSettings _settings;

        // Simple circular buffer for raw samples (written by audio thread, read by FFT thread)
        private const int BufferSize = 8192;
        private readonly float[] _sampleBuffer = new float[BufferSize];
        private volatile int _writePos;

        // Per-update peak tracking (written by audio thread, read by UI)
        private volatile float _currentPeak;
        private volatile float _currentRms;

        // FFT configuration — set at construction, immutable thereafter.
        // Supported sizes: 512 (~86Hz/bin, ~11.6ms), 1024 (~43Hz/bin, ~23ms), 2048 (~21.5Hz/bin, ~46ms)
        private readonly int _fftSize;
        private readonly int _fftLog2;
        private readonly int _spectrumSize;

        // FFT state (owned exclusively by the background thread)
        private readonly Complex[] _fftBuffer;
        private readonly float[] _hannWindow;
        private readonly float[] _smoothedSpectrum;

        // Per-bin temporal smoothing: bass gets slightly slower smoothing (weighty),
        // treble gets faster smoothing (sparkly). Linearly interpolated across bins.
        // Alphas are scaled based on FFT size — larger windows need higher alphas
        // to compensate for less-frequent updates.
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

        public WaveFormat WaveFormat => _source.WaveFormat;

        /// <summary>
        /// The FFT size used by this provider. UI reads this to configure matching bin ranges.
        /// </summary>
        public int FftSize => _fftSize;

        /// <summary>
        /// Half the FFT size — number of usable spectrum bins.
        /// </summary>
        public int SpectrumSize => _spectrumSize;

        public VisualizationDataProvider(ISampleProvider source, int fftSize = 1024, UniPlaySongSettings settings = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _channels = source.WaveFormat.Channels;
            _settings = settings;

            // Validate and set FFT size
            if (fftSize <= 512) { _fftSize = 512; _fftLog2 = 9; }
            else if (fftSize <= 1024) { _fftSize = 1024; _fftLog2 = 10; }
            else { _fftSize = 2048; _fftLog2 = 11; }
            _spectrumSize = _fftSize / 2;

            // Allocate FFT arrays
            _fftBuffer = new Complex[_fftSize];
            _hannWindow = new float[_fftSize];
            _smoothedSpectrum = new float[_spectrumSize];
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

        /// <summary>
        /// Recomputes per-bin rise/fall alphas from current settings (or defaults).
        /// Called at construction and each FFT tick. Skips recomputation if settings unchanged.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecomputeAlphas()
        {
            // Read current settings (cheap int reads)
            int riseLow = _settings?.VizFftRiseLow ?? 88;
            int riseHigh = _settings?.VizFftRiseHigh ?? 93;
            int fallLow = _settings?.VizFftFallLow ?? 50;
            int fallHigh = _settings?.VizFftFallHigh ?? 65;

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
            int samplesRead = _source.Read(buffer, offset, count);
            if (samplesRead == 0) return 0;

            float peak = 0f;
            float sumSq = 0f;
            int monoCount = 0;

            int pos = _writePos;
            for (int i = 0; i < samplesRead; i += _channels)
            {
                float sample = buffer[offset + i];
                if (_channels == 2 && i + 1 < samplesRead)
                    sample = (sample + buffer[offset + i + 1]) * 0.5f;

                float abs = sample > 0 ? sample : -sample;
                if (abs > peak) peak = abs;
                sumSq += sample * sample;
                monoCount++;

                _sampleBuffer[pos & (BufferSize - 1)] = sample;
                pos++;
            }

            _writePos = pos;
            _currentPeak = peak;
            _currentRms = monoCount > 0 ? (float)Math.Sqrt(sumSq / monoCount) : 0f;
            _newSamplesSignal.Set();

            return samplesRead;
        }

        /// <summary>
        /// Fast path: get simple peak and RMS levels. No FFT. Near-zero cost.
        /// </summary>
        public void GetLevels(out float peak, out float rms)
        {
            peak = _currentPeak;
            rms = _currentRms;
        }

        /// <summary>
        /// UI thread: copies pre-calculated spectrum data. Zero computation — just Array.Copy.
        /// </summary>
        public int GetSpectrumData(float[] destination, int destOffset, int count)
        {
            int toCopy = Math.Min(count, _spectrumSize);
            var front = _spectrumFront;
            Array.Copy(front, 0, destination, destOffset, toCopy);
            return toCopy;
        }

        /// <summary>
        /// Background thread: runs FFT and temporal smoothing.
        /// Two modes controlled by VizFftTimerMode setting (live-switchable):
        /// - Signal mode (default): wakes on new audio data via ManualResetEventSlim.
        ///   Runs at audio buffer rate (~43fps at 1024/44.1kHz). Zero CPU when idle.
        /// - Timer mode: fixed ~16ms intervals via Stopwatch for consistent ~62fps.
        ///   Matches CompositionTarget.Rendering rate. Uses SpinWait for sub-ms precision
        ///   (Thread.Sleep has ~15ms granularity on Windows and can't reliably hit 16ms).
        /// </summary>
        private void FftLoop()
        {
            long targetTicksPerFrame = 16L * Stopwatch.Frequency / 1000L; // ~16ms in Stopwatch ticks
            var sw = new Stopwatch();
            sw.Start();
            long nextFrameTick = sw.ElapsedTicks;

            while (!_disposed)
            {
                bool timerMode = _settings?.VizFftTimerMode ?? false;

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
                    // Signal mode: sleep until audio thread delivers new samples
                    _newSamplesSignal.Wait(50);
                    _newSamplesSignal.Reset();
                    // Keep timer in sync so switching to timer mode doesn't cause a burst
                    nextFrameTick = sw.ElapsedTicks + targetTicksPerFrame;
                }

                if (_disposed) break;

                // Snapshot write position to avoid reading a moving target
                int wp = _writePos;
                int readPos = wp - _fftSize;

                for (int i = 0; i < _fftSize; i++)
                {
                    _fftBuffer[i].X = _sampleBuffer[(readPos + i) & (BufferSize - 1)] * _hannWindow[i];
                    _fftBuffer[i].Y = 0;
                }

                FastFourierTransform.FFT(true, _fftLog2, _fftBuffer);

                // Recompute alphas from live settings (skips if unchanged — cheap int compare)
                RecomputeAlphas();

                // Combined: normalize FFT output + per-bin asymmetric temporal smoothing.
                // Single pass eliminates the intermediate _fftSpectrum array and halves cache misses.
                for (int i = 0; i < _spectrumSize; i++)
                {
                    float re = _fftBuffer[i].X;
                    float im = _fftBuffer[i].Y;
                    float magSq = re * re + im * im;
                    float db = 10f * (float)Math.Log10(Math.Max(magSq, 1e-20f));
                    // Map -80dB..0dB to 0..1, then square for dynamic range expansion
                    float normalized = (db + 80f) / 80f;
                    if (normalized < 0f) normalized = 0f;
                    else if (normalized > 1f) normalized = 1f;
                    float raw = normalized * normalized;

                    // Per-bin asymmetric smoothing: bass slower (weighty), treble faster (sparkly)
                    float prev = _smoothedSpectrum[i];
                    float alpha = raw >= prev ? _riseAlpha[i] : _fallAlpha[i];
                    _smoothedSpectrum[i] = prev + (raw - prev) * alpha;
                }

                // Publish to front buffer for UI consumption.
                // Copy smoothed state into back buffer, then atomically swap into front.
                // Interlocked.Exchange ensures the UI thread always reads a complete frame.
                Array.Copy(_smoothedSpectrum, 0, _spectrumBack, 0, _spectrumSize);
                _spectrumBack = Interlocked.Exchange(ref _spectrumFront, _spectrumBack);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _newSamplesSignal.Set(); // wake thread so it can exit
        }

        private static volatile VisualizationDataProvider _current;
        public static VisualizationDataProvider Current
        {
            get => _current;
            set => _current = value;
        }
    }
}
