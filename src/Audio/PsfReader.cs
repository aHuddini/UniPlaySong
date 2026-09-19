using System;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace UniPlaySong.Audio
{
    // WaveStream + ISampleProvider over psf.dll for PlayStation (.psf / .minipsf) music.
    // Converts the engine's int16 stereo output to IEEE float32 for the NAudio pipeline, the same
    // shape as GmeReader so NAudioMusicPlayer treats the two alike.
    //
    // A PSF is a program, not a stream: the only way to reach a position is to run the driver
    // there from the start. So there is no seek - Position and CurrentTime setters are no-ops,
    // GameMusicResumePolicy never asks to resume one, and NAudioMusicPlayer's pause detaches the
    // mixer input so the emulation freezes where it stopped.
    public class PsfReader : WaveStream, ISampleProvider
    {
        private const int SampleRate = 44100;
        private const int Channels = 2;

        // Same fallback GmeReader uses when a file carries no length.
        private const int DefaultLengthMs = 150000;

        private readonly WaveFormat _waveFormat;
        private readonly long _fadeStartFrame;
        private readonly long _fadeFrames;
        private readonly long _totalFrames;

        private byte[] _state;
        private GCHandle _pin;
        private IntPtr _psx;
        private long _framesGenerated;
        private short[] _shortBuffer;
        private bool _disposed;

        // The audio thread renders in Read while the UI thread reads CurrentTime from Pause; the
        // engine is not re-entrant, so every native call goes through here.
        private readonly object _lock = new object();

        public PsfReader(string fileName)
        {
            var psf = PsfFile.Load(fileName);

            _state = new byte[PsfNative.psx_get_state_size(1)];
            // The engine holds pointers into its own state, so the buffer must never move.
            _pin = GCHandle.Alloc(_state, GCHandleType.Pinned);
            _psx = _pin.AddrOfPinnedObject();

            try
            {
                PsfNative.psx_set_refresh(_psx, (uint)psf.RefreshHz);

                for (int i = 0; i < psf.Sections.Count; i++)
                {
                    var section = psf.Sections[i];
                    if (PsfNative.psf_load_section(_psx, section, (uint)section.Length, i == 0 ? 1u : 0u) != 0)
                        throw new InvalidOperationException($"PSF failed to load '{fileName}': {PsfNative.LastError(_psx) ?? "bad PS-X EXE"}");
                }

                if (PsfNative.psf_start(_psx) != PsfNative.Success)
                    throw new InvalidOperationException($"PSF failed to start '{fileName}': {PsfNative.LastError(_psx) ?? "unknown error"}");
            }
            catch
            {
                Release();
                throw;
            }

            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

            // The rip says how long it is and how long to fade; the fade is applied here rather than
            // by the engine so the app's own song-end fade still works off TotalTime.
            double lengthMs = psf.Length?.TotalMilliseconds ?? DefaultLengthMs;
            double fadeMs = psf.Fade?.TotalMilliseconds ?? 0;
            _fadeStartFrame = (long)(lengthMs * SampleRate / 1000);
            _fadeFrames = (long)(fadeMs * SampleRate / 1000);
            _totalFrames = _fadeStartFrame + _fadeFrames;
        }

        public override WaveFormat WaveFormat => _waveFormat;

        public override long Length => _totalFrames * Channels * sizeof(float);

        public override long Position
        {
            get { lock (_lock) return _framesGenerated * Channels * sizeof(float); }
            set { }
        }

        public override TimeSpan TotalTime => TimeSpan.FromSeconds((double)_totalFrames / SampleRate);

        public override TimeSpan CurrentTime
        {
            get { lock (_lock) return TimeSpan.FromSeconds((double)_framesGenerated / SampleRate); }
            set { }
        }

        // ISampleProvider.Read - the hot path. EOF is a partial read once length + fade is reached,
        // which is what NAudio's mixer takes as the end of the input.
        public int Read(float[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                if (_psx == IntPtr.Zero) return 0;

                int frames = (int)Math.Min(count / Channels, _totalFrames - _framesGenerated);
                if (frames <= 0) return 0;

                if (_shortBuffer == null || _shortBuffer.Length < frames * Channels)
                    _shortBuffer = new short[frames * Channels];

                if (PsfNative.psf_gen(_psx, _shortBuffer, (uint)frames) != PsfNative.Success)
                    return 0;

                for (int frame = 0; frame < frames; frame++)
                {
                    float gain = FadeGain(_framesGenerated + frame);
                    int i = frame * Channels;
                    buffer[offset + i] = _shortBuffer[i] / 32768f * gain;
                    buffer[offset + i + 1] = _shortBuffer[i + 1] / 32768f * gain;
                }

                _framesGenerated += frames;
                return frames * Channels;
            }
        }

        private float FadeGain(long frame)
        {
            if (_fadeFrames <= 0 || frame < _fadeStartFrame) return 1f;
            return Math.Max(0f, 1f - (float)(frame - _fadeStartFrame) / _fadeFrames);
        }

        // WaveStream.Read - required by the base class, not used by the ISampleProvider pipeline.
        public override int Read(byte[] buffer, int offset, int count)
        {
            var floatCount = count / sizeof(float);
            var floatBuffer = new float[floatCount];
            int samplesRead = Read(floatBuffer, 0, floatCount);
            Buffer.BlockCopy(floatBuffer, 0, buffer, offset, samplesRead * sizeof(float));
            return samplesRead * sizeof(float);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                lock (_lock)
                {
                    if (_psx != IntPtr.Zero) PsfNative.psf_stop(_psx);
                    Release();
                    _shortBuffer = null;
                    _disposed = true;
                }
            }
            base.Dispose(disposing);
        }

        private void Release()
        {
            _psx = IntPtr.Zero;
            if (_pin.IsAllocated) _pin.Free();
            _state = null;
        }
    }
}
