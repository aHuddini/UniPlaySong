using System;
using NAudio.Wave;

namespace UniPlaySong.Audio
{
    // Dedicated preview player for NSF Track Manager.
    // Owns its own libgme emulator + WaveOutEvent so it can play a specific
    // track without disturbing the main music player.
    internal sealed class GmePreviewPlayer : IDisposable
    {
        private const int SampleRate = 44100;

        private readonly object _lock = new object();
        private IntPtr _emu = IntPtr.Zero;
        private WaveOutEvent _waveOut;
        private GmePreviewSampleProvider _sampleProvider;
        private bool _disposed;

        public event EventHandler TrackEnded;

        private float _volume = 1.0f;
        public float Volume
        {
            get { return _volume; }
            set
            {
                _volume = Math.Max(0f, Math.Min(1f, value));
                lock (_lock)
                {
                    if (_waveOut != null) _waveOut.Volume = _volume;
                }
            }
        }

        public bool IsPlaying
        {
            get
            {
                lock (_lock)
                {
                    return _waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing;
                }
            }
        }

        // Load NSF file. Safe to call multiple times (re-opens emulator).
        public void Load(string nsfPath)
        {
            lock (_lock)
            {
                StopInternal();
                DisposeEmu();

                IntPtr emu;
                var err = GmeNative.gme_open_file(nsfPath, out emu, SampleRate);
                var msg = GmeNative.GetError(err);
                if (msg != null)
                    throw new InvalidOperationException("gme_open_file failed: " + msg);
                _emu = emu;
            }
        }

        // Play a specific track (0-based). Stops any current playback first.
        public void Play(int trackIndex0Based, int maxDurationSeconds)
        {
            lock (_lock)
            {
                if (_emu == IntPtr.Zero)
                    throw new InvalidOperationException("Load() must be called before Play().");

                StopInternal();

                var startErr = GmeNative.gme_start_track(_emu, trackIndex0Based);
                var startMsg = GmeNative.GetError(startErr);
                if (startMsg != null)
                    throw new InvalidOperationException("gme_start_track failed: " + startMsg);

                // Schedule fade-out at (maxDurationSeconds - 2)*1000 ms so the
                // preview ends gracefully before hitting the hard cap.
                int fadeStartMs = Math.Max(0, (maxDurationSeconds - 2) * 1000);
                GmeNative.gme_set_fade(_emu, fadeStartMs);

                _sampleProvider = new GmePreviewSampleProvider(_emu, SampleRate);
                _sampleProvider.Ended += OnSampleProviderEnded;

                _waveOut = new WaveOutEvent();
                _waveOut.Init(_sampleProvider);
                _waveOut.Volume = _volume;
                _waveOut.Play();
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            if (_waveOut != null)
            {
                try { _waveOut.Stop(); } catch { /* swallow — disposing */ }
                _waveOut.Dispose();
                _waveOut = null;
            }
            if (_sampleProvider != null)
            {
                _sampleProvider.Ended -= OnSampleProviderEnded;
                _sampleProvider = null;
            }
        }

        private void OnSampleProviderEnded(object sender, EventArgs e)
        {
            lock (_lock)
            {
                if (_disposed) return;
            }
            var handler = TrackEnded;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void DisposeEmu()
        {
            if (_emu != IntPtr.Zero)
            {
                GmeNative.gme_delete(_emu);
                _emu = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_lock)
            {
                StopInternal();
                DisposeEmu();
            }
        }
    }

    // Minimal ISampleProvider that pulls 16-bit stereo samples from gme_play
    // and converts to 32-bit float for NAudio. Fires Ended when gme_track_ended.
    internal sealed class GmePreviewSampleProvider : ISampleProvider
    {
        private readonly IntPtr _emu;
        private readonly WaveFormat _format;
        private readonly short[] _shortBuf;
        private bool _endedFired;

        public event EventHandler Ended;

        public GmePreviewSampleProvider(IntPtr emu, int sampleRate)
        {
            _emu = emu;
            _format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
            _shortBuf = new short[4096];
        }

        public WaveFormat WaveFormat { get { return _format; } }

        public int Read(float[] buffer, int offset, int count)
        {
            if (_emu == IntPtr.Zero) return 0;

            int totalWritten = 0;
            while (totalWritten < count)
            {
                int request = Math.Min(_shortBuf.Length, count - totalWritten);
                if ((request & 1) != 0) request--; // ensure stereo-pair count
                if (request <= 0) break;

                var err = GmeNative.gme_play(_emu, request, _shortBuf);
                if (GmeNative.GetError(err) != null) break;

                for (int i = 0; i < request; i++)
                    buffer[offset + totalWritten + i] = _shortBuf[i] / 32768f;

                totalWritten += request;

                if (GmeNative.gme_track_ended(_emu) != 0)
                {
                    if (!_endedFired)
                    {
                        _endedFired = true;
                        var handler = Ended;
                        if (handler != null) handler(this, EventArgs.Empty);
                    }
                    break;
                }
            }
            return totalWritten;
        }
    }
}
