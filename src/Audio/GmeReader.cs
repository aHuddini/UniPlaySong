using System;
using NAudio.Wave;

namespace UniPlaySong.Audio
{
    // WaveStream + ISampleProvider wrapper around Game Music Emu for retro game music playback.
    // Converts GME's int16 stereo PCM output to IEEE float32 for the NAudio pipeline.
    // Drop-in replacement for AudioFileReader when the file is a GME-supported format.
    public class GmeReader : WaveStream, ISampleProvider
    {
        private const int SampleRate = 44100;
        private const int Channels = 2;
        private const float OutputGain = 1.5f; // Retro chip output is quieter than modern mastered audio

        private IntPtr _emu;
        private readonly WaveFormat _waveFormat;
        private readonly int _playLengthMs;
        private readonly long _totalSamples;
        private short[] _shortBuffer;
        private bool _disposed;

        public GmeReader(string fileName)
        {
            IntPtr emu;
            string err = GmeNative.GetError(GmeNative.gme_open_file(fileName, out emu, SampleRate));
            if (err != null)
                throw new InvalidOperationException($"GME failed to open '{fileName}': {err}");
            _emu = emu;

            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

            // Get track duration from metadata
            IntPtr info;
            err = GmeNative.GetError(GmeNative.gme_track_info(_emu, out info, 0));
            if (err == null && info != IntPtr.Zero)
            {
                _playLengthMs = GmeNative.GetPlayLength(info);
                GmeNative.gme_free_info(info);
            }
            else
            {
                _playLengthMs = 150000; // GME default: 2.5 minutes
            }

            _totalSamples = (long)SampleRate * _playLengthMs / 1000;

            // Schedule fade-out 1 second before play_length so track ends naturally
            int fadeStart = Math.Max(0, _playLengthMs - 1000);
            GmeNative.gme_set_fade(_emu, fadeStart);

            // Start track 0
            err = GmeNative.GetError(GmeNative.gme_start_track(_emu, 0));
            if (err != null)
                throw new InvalidOperationException($"GME failed to start track: {err}");
        }

        public override WaveFormat WaveFormat => _waveFormat;

        // Length in bytes (float32 samples × sizeof(float))
        public override long Length => _totalSamples * Channels * sizeof(float);

        public override long Position
        {
            get
            {
                if (_emu == IntPtr.Zero) return 0;
                int ms = GmeNative.gme_tell(_emu);
                return (long)ms * SampleRate / 1000 * Channels * sizeof(float);
            }
            set
            {
                if (_emu == IntPtr.Zero) return;
                int ms = (int)(value * 1000 / (SampleRate * Channels * sizeof(float)));
                GmeNative.gme_seek(_emu, ms);
            }
        }

        public override TimeSpan TotalTime => TimeSpan.FromMilliseconds(_playLengthMs);

        public override TimeSpan CurrentTime
        {
            get
            {
                if (_emu == IntPtr.Zero) return TimeSpan.Zero;
                return TimeSpan.FromMilliseconds(GmeNative.gme_tell(_emu));
            }
            set
            {
                if (_emu == IntPtr.Zero) return;
                GmeNative.gme_seek(_emu, (int)value.TotalMilliseconds);
            }
        }

        // ISampleProvider.Read — the hot path used by NAudio's mixer pipeline.
        // Generates int16 samples from GME, converts to float32 in-place.
        public int Read(float[] buffer, int offset, int count)
        {
            if (_emu == IntPtr.Zero || GmeNative.gme_track_ended(_emu) != 0)
                return 0;

            // Reuse short buffer if possible
            if (_shortBuffer == null || _shortBuffer.Length < count)
                _shortBuffer = new short[count];

            string err = GmeNative.GetError(GmeNative.gme_play(_emu, count, _shortBuffer));
            if (err != null)
                return 0;

            // Convert int16 → float32 with gain boost (retro chips are quieter than modern audio)
            for (int i = 0; i < count; i++)
                buffer[offset + i] = _shortBuffer[i] / 32768f * OutputGain;

            // Signal EOF if track ended during this read
            if (GmeNative.gme_track_ended(_emu) != 0)
            {
                // Find where audio actually ends (last non-silent sample)
                // For simplicity, return full buffer — SongEndDetector handles partial reads
                return count;
            }

            return count;
        }

        // WaveStream.Read — required by abstract base, not used in ISampleProvider pipeline
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
                if (_emu != IntPtr.Zero)
                {
                    GmeNative.gme_delete(_emu);
                    _emu = IntPtr.Zero;
                }
                _shortBuffer = null;
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
