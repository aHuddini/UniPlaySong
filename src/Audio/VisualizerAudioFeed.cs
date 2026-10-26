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
        // The song, after the audio (VisSong): a stamp per block of where the song was, and the analysis request
        internal const uint SongMagic = 0x31474E53u; // "SNG1"
        internal const int Stamps = 64, PathChars = 260, StampSize = 16;
        internal const int OffSong = OffData + Capacity * 2 * sizeof(float);
        internal const int SongOffStamps = 4, SongOffSeq = 8, SongOffSong = 12, SongOffBytes = 16, SongOffTime = 24, SongOffStamp = 32;
        internal const int SongOffAnalysis = SongOffStamp + Stamps * StampSize, SongOffPcm = SongOffAnalysis + PathChars * 2;
        internal const int SongOffCoverSeq = SongOffPcm + PathChars * 2, SongOffCoverOn = SongOffCoverSeq + 4, SongOffCoverA = SongOffCoverOn + 4,
            SongOffCoverB = SongOffCoverA + 12, SongOffCoverC = SongOffCoverB + 12;
        internal const int Bytes = OffSong + ((SongOffCoverC + 12 + 7) & ~7);   // the C struct rounds up to 8

        private static VisualizerAudioFeed _active;

        private readonly MemoryMappedFile _map;
        private readonly MemoryMappedViewAccessor _view;
        private readonly IntPtr _base;
        private float[] _stereo = new float[0];
        private bool _formatWritten;
        private int _lastSoundTick = Environment.TickCount;

        private VisualizerAudioFeed(string name)
        {
            _map = MemoryMappedFile.CreateNew(name, Bytes);
            _view = _map.CreateViewAccessor();
            _base = _view.SafeMemoryMappedViewHandle.DangerousGetHandle() + (int)_view.PointerOffset;
            Marshal.WriteInt32(_base, OffCapacity, Capacity);
            Marshal.WriteInt32(_base, OffSong, unchecked((int)SongMagic));
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
            var provider = VisualizationDataProvider.Current;
            if (!ReferenceEquals(clock, provider?.Clock)) return;
            int frames = feed.Append(clock.WaveFormat.SampleRate, clock.WaveFormat.Channels, startFrame, buffer, offset, samples);
            // where the song was as this block ended (the provider read it just now, on this thread); 0 = not a song file
            if (frames > 0) feed.Stamp((uint)(startFrame + frames), provider.SongPath != null ? (uint)provider.SongId : 0u, provider.SongSeconds);
        }

        // Environment.TickCount when the ring last carried anything audible. The player's clock can't say when the music
        // stopped - the always-on mixer keeps playing silence - so the visualizer idles on this instead.
        public int LastSoundTick => Volatile.Read(ref _lastSoundTick);

        // Once per displayed frame, on the UI thread: which frame the listener is hearing now.
        public void PublishPlayed()
        {
            var clock = VisualizationDataProvider.Current?.Clock;
            if (clock == null) return;
            Marshal.WriteInt32(_base, OffPlayed, unchecked((int)(uint)clock.FramesPlayed));
        }

        // The block into the ring; returns its frames.
        internal int Append(int sampleRate, int channels, long startFrame, float[] buffer, int offset, int samples)
        {
            if (!_formatWritten)
            {
                Marshal.WriteInt32(_base, OffSampleRate, sampleRate);
                _formatWritten = true;
            }
            int frames = samples / Math.Max(1, channels);
            if (frames <= 0) return 0;
            if (_stereo.Length < frames * 2) _stereo = new float[frames * 2];
            Interleave(buffer, offset, frames, channels, _stereo);
            if (Audible(_stereo, frames * 2)) Volatile.Write(ref _lastSoundTick, Environment.TickCount);

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
            return frames;
        }

        // On the audio thread, after the block: the frame it ended at, the song and where the song was then. The stamp is
        // written whole before the count moves, so the renderer reads only finished ones.
        internal void Stamp(uint frame, uint song, double seconds)
        {
            uint n = (uint)Marshal.ReadInt32(_base, OffSong + SongOffStamps);
            int at = OffSong + SongOffStamp + (int)(n % Stamps) * StampSize;
            Marshal.WriteInt32(_base, at, unchecked((int)frame));
            Marshal.WriteInt32(_base, at + 4, unchecked((int)song));
            Marshal.WriteInt64(_base, at + 8, BitConverter.DoubleToInt64Bits(seconds));
            Thread.MemoryBarrier();
            Marshal.WriteInt32(_base, OffSong + SongOffStamps, unchecked((int)(n + 1)));
        }

        private readonly object _requestGate = new object();

        // The song analysis to use: the song it's for, its file, and the song as mono 48 kHz float to analyse into that
        // file when it isn't there yet (null when it is). Between two seq bumps (odd while writing), so the renderer
        // never takes half of one. Any thread.
        public void PublishSong(uint song, string analysisPath, string pcmPath, long sourceBytes, long sourceTime)
        {
            lock (_requestGate)
            {
                int seq = Marshal.ReadInt32(_base, OffSong + SongOffSeq);
                Marshal.WriteInt32(_base, OffSong + SongOffSeq, seq + 1);
                Thread.MemoryBarrier();
                Marshal.WriteInt32(_base, OffSong + SongOffSong, unchecked((int)song));
                Marshal.WriteInt64(_base, OffSong + SongOffBytes, sourceBytes);
                Marshal.WriteInt64(_base, OffSong + SongOffTime, sourceTime);
                WritePath(OffSong + SongOffAnalysis, analysisPath);
                WritePath(OffSong + SongOffPcm, pcmPath);
                Thread.MemoryBarrier();
                Marshal.WriteInt32(_base, OffSong + SongOffSeq, seq + 2);
            }
        }

        // The cover's three colours for the Jukebox Fountain's palette (on = false: no art, the style's own palette). Between
        // two seq bumps, as the request. UI thread.
        public void PublishCover(bool on, System.Windows.Media.Color a, System.Windows.Media.Color b, System.Windows.Media.Color c)
        {
            int seq = Marshal.ReadInt32(_base, OffSong + SongOffCoverSeq);
            Marshal.WriteInt32(_base, OffSong + SongOffCoverSeq, seq + 1);
            Thread.MemoryBarrier();
            Marshal.WriteInt32(_base, OffSong + SongOffCoverOn, on ? 1 : 0);
            WriteRgb(OffSong + SongOffCoverA, a);
            WriteRgb(OffSong + SongOffCoverB, b);
            WriteRgb(OffSong + SongOffCoverC, c);
            Thread.MemoryBarrier();
            Marshal.WriteInt32(_base, OffSong + SongOffCoverSeq, seq + 2);
        }

        private void WriteRgb(int offset, System.Windows.Media.Color c) =>
            Marshal.Copy(new[] { c.R / 255f, c.G / 255f, c.B / 255f }, 0, _base + offset, 3);

        // UTF-16, 0-terminated; null or a path too long for the field is written empty.
        private void WritePath(int offset, string path)
        {
            if (path == null || path.Length >= PathChars) path = string.Empty;
            for (int i = 0; i < path.Length; i++) Marshal.WriteInt16(_base, offset + i * 2, (short)path[i]);
            Marshal.WriteInt16(_base, offset + path.Length * 2, 0);
        }

        // Anything above -80 dBFS.
        internal static bool Audible(float[] samples, int count)
        {
            for (int i = 0; i < count; i++)
                if (samples[i] > 1e-4f || samples[i] < -1e-4f) return true;
            return false;
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
