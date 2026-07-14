using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace UniPlaySong.Common
{
    // Managed side of the Spotify process-loopback capture. P/Invokes SpotifyLoopback.dll,
    // marshals native PCM callbacks into a ring buffer, exposes it as bytes + WaveFormat.
    // Falls silent (zero-fill) on underrun rather than glitching. Raises CaptureStopped if
    // the native callback stops while we still think we're capturing (capture died).
    public class SpotifyLoopbackClient : IDisposable
    {
        // --- lock-free-enough single-producer/single-consumer byte ring ---
        public class RingBuffer
        {
            private readonly byte[] _buf;
            private int _readPos, _writePos, _count;
            private readonly object _lock = new object();
            public RingBuffer(int capacity) { _buf = new byte[capacity]; }

            // Chunked copy, max two segments over the wrap; overflow drops the OLDEST bytes.
            public void Write(byte[] src, int offset, int len)
            {
                if (len <= 0) return;
                lock (_lock)
                {
                    int cap = _buf.Length;
                    if (len >= cap)
                    {
                        // Larger than the whole ring: keep only the newest `cap` bytes.
                        offset += len - cap;
                        len = cap;
                        _readPos = 0; _writePos = 0; _count = 0;
                    }

                    int overflow = _count + len - cap;
                    if (overflow > 0)
                    {
                        _readPos = (_readPos + overflow) % cap; // drop oldest
                        _count -= overflow;
                    }

                    int first = Math.Min(len, cap - _writePos);
                    Buffer.BlockCopy(src, offset, _buf, _writePos, first);
                    if (len > first)
                        Buffer.BlockCopy(src, offset + first, _buf, 0, len - first);
                    _writePos = (_writePos + len) % cap;
                    _count += len;
                }
            }

            public void Clear() { lock (_lock) { _readPos = 0; _writePos = 0; _count = 0; } }

            // Always writes `count` bytes; zero-fills whatever the buffer can't supply. Returns count.
            public int Read(byte[] dst, int offset, int count)
            {
                if (count <= 0) return 0;
                lock (_lock)
                {
                    int cap = _buf.Length;
                    int avail = Math.Min(_count, count);
                    if (avail > 0)
                    {
                        int first = Math.Min(avail, cap - _readPos);
                        Buffer.BlockCopy(_buf, _readPos, dst, offset, first);
                        if (avail > first)
                            Buffer.BlockCopy(_buf, 0, dst, offset + first, avail - first);
                        _readPos = (_readPos + avail) % cap;
                        _count -= avail;
                    }
                    if (avail < count)
                        Array.Clear(dst, offset + avail, count - avail); // underrun -> silence
                    return count;
                }
            }
        }

        private delegate void PcmCallback(IntPtr user, IntPtr data, int byteCount,
                                          int sampleRate, int channels, int bitsPerSample);

        [DllImport("SpotifyLoopback.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int SpotifyLoopback_Start(uint pid, PcmCallback cb, IntPtr user);
        [DllImport("SpotifyLoopback.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern void SpotifyLoopback_Stop();
        [DllImport("SpotifyLoopback.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int SpotifyLoopback_IsCapturing();

        private readonly RingBuffer _ring = new RingBuffer(88200); // ~250ms @ 44.1k float32 stereo (352800 B/s)
        private PcmCallback _cbDelegate; // keep rooted against GC
        private volatile bool _started;
        private DateTime _lastCallback = DateTime.MinValue;

        public WaveFormat Format { get; private set; } = new WaveFormat(44100, 16, 2);
        public bool IsCapturing => _started && SpotifyLoopback_IsCapturing() == 1;
        public event Action CaptureStopped;

        public bool Start()
        {
            if (_started) return false;
            uint pid = ResolveSpotifyPid();
            if (pid == 0) return false;
            _cbDelegate = OnPcm;
            _lastCallback = DateTime.Now;
            int hr = SpotifyLoopback_Start(pid, _cbDelegate, IntPtr.Zero);
            _started = hr == 0;
            return _started;
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;
            try { SpotifyLoopback_Stop(); } catch { }
        }

        public int Read(byte[] buffer, int offset, int count) => _ring.Read(buffer, offset, count);

        // Drops buffered audio. Called when the duck engages — the ring may hold up to 250ms of
        // PRE-duck full-volume samples, which the 1024x duck-compensation would turn into a blast.
        public void FlushRing() => _ring.Clear();

        // Test seam: pushes PCM bytes into the ring exactly as the native callback would.
        internal void PushForTest(byte[] pcm) => _ring.Write(pcm, 0, pcm.Length);

        // Top Spotify process (window/parent). Tree mode covers its children where the audio renders.
        private static uint ResolveSpotifyPid()
        {
            try
            {
                var procs = Process.GetProcessesByName("Spotify");
                if (procs.Length == 0) return 0;
                foreach (var p in procs)
                    if (p.MainWindowHandle != IntPtr.Zero) return (uint)p.Id;
                return (uint)procs[0].Id;
            }
            catch { return 0; }
        }

        // Reused marshal buffer — safe: one callback thread, ring copies out synchronously.
        private byte[] _pcmScratch = new byte[8192];

        private void OnPcm(IntPtr user, IntPtr data, int byteCount, int sampleRate, int channels, int bits)
        {
            _lastCallback = DateTime.Now;
            if (Format.SampleRate != sampleRate || Format.Channels != channels || Format.BitsPerSample != bits)
                Format = bits == 32
                    ? WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels)  // shim requests IEEE float
                    : new WaveFormat(sampleRate, bits, channels);
            if (data == IntPtr.Zero || byteCount <= 0) return; // silent packet
            if (_pcmScratch.Length < byteCount) _pcmScratch = new byte[byteCount];
            Marshal.Copy(data, _pcmScratch, 0, byteCount);
            _ring.Write(_pcmScratch, 0, byteCount);
        }

        public void Dispose() => Stop();
    }
}
