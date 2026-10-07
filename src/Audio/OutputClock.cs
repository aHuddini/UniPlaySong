using System.Threading;
using NAudio.Wave;

namespace UniPlaySong.Audio
{
    // Sits directly before the output device and counts the frames handed to it. With the device's own played
    // position that says how far playback has really got, so the visualizer can show what is being heard rather
    // than what was just queued - measured at ~350 ms ahead on the default device buffers.
    //
    // One per player, for its whole life: the device and mixer behind it are rebuilt after an audio-device release,
    // but visualizer taps hold on to the clock. Frame counts carry on across devices.
    public sealed class OutputClock : ISampleProvider
    {
        private volatile ISampleProvider _source;
        private volatile IWavePosition _device;
        private long _framesSubmitted;
        private long _framesBeforeDevice; // submitted when the current device was attached

        public OutputClock(WaveFormat format)
        {
            WaveFormat = format;
        }

        public WaveFormat WaveFormat { get; }

        // Points the clock at a freshly built mixer and device. Call after the device's Init and before its Play.
        public void Attach(ISampleProvider source, IWavePosition device)
        {
            _source = source;
            Interlocked.Exchange(ref _framesBeforeDevice, FramesSubmitted);
            _device = device;
        }

        public void Detach()
        {
            _device = null;
            _source = null;
        }

        public long FramesSubmitted => Interlocked.Read(ref _framesSubmitted);

        // Frames the device has played. With no device, everything submitted counts as played.
        public long FramesPlayed
        {
            get
            {
                long submitted = FramesSubmitted;
                var device = _device;
                if (device == null) return submitted;
                long before = Interlocked.Read(ref _framesBeforeDevice);
                try
                {
                    return before + Played(submitted - before, device.GetPosition(), device.OutputWaveFormat.BlockAlign);
                }
                catch
                {
                    return submitted; // device torn down under us
                }
            }
        }

        // SPIKE (feature/music-visual-control only, remove before merge): how promptly the device asks for audio.
        // WaveOutEvent pulls one 150 ms block at a time with two queued, so a gap well past 150 ms between pulls means
        // the queue ran low; past ~300 ms it ran dry and the music audibly dropped out.
        internal static long SpikeReads, SpikeLateReads, SpikeMaxGapTicks;
        private long _spikeLastReadTicks;

        public int Read(float[] buffer, int offset, int count)
        {
            long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_spikeLastReadTicks != 0)
            {
                long gap = nowTicks - _spikeLastReadTicks;
                SpikeReads++;
                if (gap > SpikeMaxGapTicks) SpikeMaxGapTicks = gap;
                if (gap > System.Diagnostics.Stopwatch.Frequency / 4) SpikeLateReads++; // > 250 ms
            }
            _spikeLastReadTicks = nowTicks;

            var source = _source;
            if (source == null) return 0;
            int read = source.Read(buffer, offset, count);
            Interlocked.Add(ref _framesSubmitted, read / WaveFormat.Channels);
            return read;
        }

        // waveOut reports its position as a 32-bit byte count, which wraps after about 3.4 hours of 44.1 kHz float
        // stereo - and the persistent device runs for the whole Playnite session. The queue is small, so the
        // difference between submitted and played is taken modulo 2^32, which survives the wrap; a device reading
        // ahead of what was submitted counts as nothing queued.
        internal static long Played(long framesSubmitted, long devicePositionBytes, int blockAlign)
        {
            if (blockAlign <= 0) return framesSubmitted;
            uint queuedBytes = unchecked((uint)(framesSubmitted * blockAlign) - (uint)devicePositionBytes);
            if (queuedBytes > int.MaxValue) return framesSubmitted;
            long queuedFrames = queuedBytes / blockAlign;
            if (queuedFrames > framesSubmitted) return 0;
            return framesSubmitted - queuedFrames;
        }
    }
}
