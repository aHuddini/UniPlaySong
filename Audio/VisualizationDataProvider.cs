using System;
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

        // Simple circular buffer for raw samples (written by audio thread, read by FFT thread)
        private const int BufferSize = 4096;
        private readonly float[] _sampleBuffer = new float[BufferSize];
        private volatile int _writePos;

        // Per-update peak tracking (written by audio thread, read by UI)
        private volatile float _currentPeak;
        private volatile float _currentRms;

        // FFT constants
        private const int FftSize = 1024;
        private const int FftLog2 = 10;
        private const int SpectrumSize = FftSize / 2;

        // FFT state (owned exclusively by the background thread)
        private readonly Complex[] _fftBuffer = new Complex[FftSize];
        private readonly float[] _hannWindow = new float[FftSize];
        private readonly float[] _fftSpectrum = new float[SpectrumSize];
        private readonly float[] _prevSpectrum = new float[SpectrumSize]; // temporal smoothing
        private const float FftSmoothAlpha = 0.35f; // blend factor: 0=all previous, 1=all current

        // Double-buffered spectrum output
        private float[] _spectrumFront = new float[SpectrumSize];
        private float[] _spectrumBack = new float[SpectrumSize];

        // Background thread control
        private readonly Thread _fftThread;
        private volatile bool _disposed;
        private readonly ManualResetEventSlim _newSamplesSignal = new ManualResetEventSlim(false);

        public WaveFormat WaveFormat => _source.WaveFormat;

        public VisualizationDataProvider(ISampleProvider source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _channels = source.WaveFormat.Channels;

            // Precompute Hann window
            double twoPiOverN = 2.0 * Math.PI / (FftSize - 1);
            for (int i = 0; i < FftSize; i++)
                _hannWindow[i] = (float)(0.5 * (1.0 - Math.Cos(twoPiOverN * i)));

            // Start background FFT thread
            _fftThread = new Thread(FftLoop)
            {
                Name = "UniPlaySong-FFT",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _fftThread.Start();
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
            int toCopy = Math.Min(count, SpectrumSize);
            var front = _spectrumFront;
            Array.Copy(front, 0, destination, destOffset, toCopy);
            return toCopy;
        }

        /// <summary>
        /// Background thread: wakes on new audio data, runs FFT, writes to front buffer.
        /// Signal-based wakeup eliminates the 0-16ms polling latency of Thread.Sleep.
        /// </summary>
        private void FftLoop()
        {
            while (!_disposed)
            {
                // Wait for audio thread to signal new samples (up to 50ms timeout as safety net)
                _newSamplesSignal.Wait(50);
                _newSamplesSignal.Reset();

                if (_disposed) break;

                // Snapshot write position to avoid reading a moving target
                int wp = _writePos;
                int readPos = wp - FftSize;

                for (int i = 0; i < FftSize; i++)
                {
                    _fftBuffer[i].X = _sampleBuffer[(readPos + i) & (BufferSize - 1)] * _hannWindow[i];
                    _fftBuffer[i].Y = 0;
                }

                FastFourierTransform.FFT(true, FftLog2, _fftBuffer);

                for (int i = 0; i < SpectrumSize; i++)
                {
                    float re = _fftBuffer[i].X;
                    float im = _fftBuffer[i].Y;
                    float magSq = re * re + im * im;
                    float db = 10f * (float)Math.Log10(Math.Max(magSq, 1e-20f));
                    // Map -45dB..0dB to 0..1 — tighter range preserves transient punch
                    float normalized = (db + 45f) / 45f;
                    if (normalized < 0f) normalized = 0f;
                    else if (normalized > 1f) normalized = 1f;

                    // Temporal smoothing: asymmetric — fast rise (instant), gentle fall
                    // Prevents single-frame FFT spikes from dominating while preserving beat attacks
                    float prev = _prevSpectrum[i];
                    if (normalized >= prev)
                        _fftSpectrum[i] = normalized; // instant rise — beats punch through
                    else
                        _fftSpectrum[i] = prev + FftSmoothAlpha * (normalized - prev); // smooth fall
                    _prevSpectrum[i] = _fftSpectrum[i];
                }

                // Swap into front buffer for UI consumption
                Array.Copy(_fftSpectrum, 0, _spectrumBack, 0, SpectrumSize);
                var temp = _spectrumFront;
                _spectrumFront = _spectrumBack;
                _spectrumBack = temp;
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
