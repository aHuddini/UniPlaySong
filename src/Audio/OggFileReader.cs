using System;
using NAudio.Wave;
using NVorbis;

namespace UniPlaySong.Audio
{
    // WaveStream + ISampleProvider wrapper around NVorbis for OGG Vorbis playback.
    // Drop-in replacement for AudioFileReader when the file is .ogg.
    public class OggFileReader : WaveStream, ISampleProvider
    {
        private readonly VorbisReader _reader;
        private readonly WaveFormat _waveFormat;
        // The audio thread reads while the UI thread can seek (resume, seeking); NVorbis isn't safe for both at once.
        private readonly object _lock = new object();

        public OggFileReader(string fileName)
        {
            _reader = new VorbisReader(fileName);
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_reader.SampleRate, _reader.Channels);
        }

        public override WaveFormat WaveFormat => _waveFormat;

        public override long Length => (long)(_reader.TotalTime.TotalSeconds * _waveFormat.AverageBytesPerSecond);

        public override long Position
        {
            get { lock (_lock) return (long)(_reader.TimePosition.TotalSeconds * _waveFormat.AverageBytesPerSecond); }
            set { lock (_lock) _reader.TimePosition = TimeSpan.FromSeconds((double)value / _waveFormat.AverageBytesPerSecond); }
        }

        public override TimeSpan TotalTime => _reader.TotalTime;

        public override TimeSpan CurrentTime
        {
            get { lock (_lock) return _reader.TimePosition; }
            set { lock (_lock) _reader.TimePosition = value; }
        }

        // ISampleProvider.Read — returns float samples directly from NVorbis
        public int Read(float[] buffer, int offset, int count)
        {
            lock (_lock) return _reader.ReadSamples(buffer, offset, count);
        }

        // WaveStream.Read — not used when consumed as ISampleProvider, but required by base class
        public override int Read(byte[] buffer, int offset, int count)
        {
            var floatCount = count / sizeof(float);
            var floatBuffer = new float[floatCount];
            int samplesRead;
            lock (_lock) samplesRead = _reader.ReadSamples(floatBuffer, 0, floatCount);
            Buffer.BlockCopy(floatBuffer, 0, buffer, offset, samplesRead * sizeof(float));
            return samplesRead * sizeof(float);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _reader?.Dispose();
            base.Dispose(disposing);
        }
    }
}
