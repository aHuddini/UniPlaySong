using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace UniPlaySong.Audio
{
    // Hands the music UPS is playing to the MilkDrop visualizer renderer: a ring of stereo float in shared memory
    // (layout: native/visualizer/visualizer_shared.h, VisAudio). The renderer takes nothing from the speakers, so it
    // reacts to the music only - never to a game, a browser or a notification.
    //
    // Frames are indexed by the player's own OutputClock count, so "newest written" and "being heard now" share units:
    // the output queue holds ~0.35 s, and the renderer feeds projectM up to the heard frame, keeping the visuals on the
    // sound. Only the clock of the audible player writes (VisualizationDataProvider.Current: game music, the library
    // dashboard or a jingle), so two players never interleave in the ring.
    internal sealed class VisualizerAudioFeed : IDisposable
    {
        internal const uint Magic = 0x31445541u; // "AUD1"
        internal const int Capacity = 65536;     // stereo frames, a power of two
        internal const int OffSampleRate = 4, OffCapacity = 8, OffWritten = 16, OffPlayed = 20, OffData = 32;
        internal const int Bytes = OffData + Capacity * 2 * sizeof(float);

        private static VisualizerAudioFeed _active;

        private readonly MemoryMappedFile _map;
        private readonly MemoryMappedViewAccessor _view;
        private readonly IntPtr _base;
        private float[] _stereo = new float[0];
        private bool _formatWritten;

        private VisualizerAudioFeed(string name)
        {
            _map = MemoryMappedFile.CreateNew(name, Bytes);
            _view = _map.CreateViewAccessor();
            _base = _view.SafeMemoryMappedViewHandle.DangerousGetHandle() + (int)_view.PointerOffset;
            Marshal.WriteInt32(_base, OffCapacity, Capacity);
            Marshal.WriteInt32(_base, 0, unchecked((int)Magic));
        }

        // Creates the ring under the given name and makes it the one the player writes to.
        public static VisualizerAudioFeed Open(string name)
        {
            var feed = new VisualizerAudioFeed(name);
            Volatile.Write(ref _active, feed);
            return feed;
        }

        // From OutputClock.Read, on the audio thread: the block just handed to the device, starting at that clock's frame
        // startFrame. Cheap when no visualizer is running (one field read).
        public static void Write(OutputClock clock, long startFrame, float[] buffer, int offset, int samples)
        {
            var feed = Volatile.Read(ref _active);
            if (feed == null || samples <= 0) return;
            if (!ReferenceEquals(clock, VisualizationDataProvider.Current?.Clock)) return;
            feed.Append(clock.WaveFormat.SampleRate, clock.WaveFormat.Channels, startFrame, buffer, offset, samples);
        }

        // Once per displayed frame, on the UI thread: which frame the listener is hearing now.
        public void PublishPlayed()
        {
            var clock = VisualizationDataProvider.Current?.Clock;
            if (clock == null) return;
            Marshal.WriteInt32(_base, OffPlayed, unchecked((int)(uint)clock.FramesPlayed));
        }

        internal void Append(int sampleRate, int channels, long startFrame, float[] buffer, int offset, int samples)
        {
            if (!_formatWritten)
            {
                Marshal.WriteInt32(_base, OffSampleRate, sampleRate);
                _formatWritten = true;
            }
            int frames = samples / Math.Max(1, channels);
            if (frames <= 0) return;
            if (_stereo.Length < frames * 2) _stereo = new float[frames * 2];
            Interleave(buffer, offset, frames, channels, _stereo);

            // Copy into the ring in up to two pieces (the second when it wraps), then move `written`: the renderer only
            // reads up to `written`, so it never sees a half-copied block.
            uint start = (uint)startFrame;
            int at = (int)(start & (Capacity - 1));
            int first = Math.Min(frames, Capacity - at);
            Marshal.Copy(_stereo, 0, _base + OffData + at * 2 * sizeof(float), first * 2);
            if (frames > first)
                Marshal.Copy(_stereo, first * 2, _base + OffData, (frames - first) * 2);
            Thread.MemoryBarrier();
            Marshal.WriteInt32(_base, OffWritten, unchecked((int)(start + (uint)frames)));
        }

        // Any channel count to interleaved stereo: mono doubles, more than two keeps the first pair.
        internal static void Interleave(float[] source, int offset, int frames, int channels, float[] stereo)
        {
            for (int i = 0; i < frames; i++)
            {
                int s = offset + i * channels;
                stereo[2 * i] = source[s];
                stereo[2 * i + 1] = channels > 1 ? source[s + 1] : source[s];
            }
        }

        public void Dispose()
        {
            Interlocked.CompareExchange(ref _active, null, this);
            _view.Dispose();
            _map.Dispose();
        }
    }
}
