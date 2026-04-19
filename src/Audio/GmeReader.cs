using System;
using System.IO;
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

        // GME's C API is NOT thread-safe on a single emu handle. The audio thread calls
        // Read() -> gme_play() while the UI thread can call Position/CurrentTime setters
        // -> gme_seek() (expensive: GME rewinds and fast-forwards from track start, can
        // take hundreds of milliseconds). Without a lock, concurrent gme_play/gme_seek
        // on the same handle corrupts emulator state and leaves the reader silent.
        private readonly object _gmeLock = new object();

        public GmeReader(string fileName)
        {
            // Pre-flight: for .vgm / .vgz, peek the header and reject files whose
            // chip references GME can't emulate (e.g. YM2610 Neo Geo, YM2151 arcade).
            // Without this check, GME opens the file successfully but produces silence
            // because it has no emulator for those chips. Failing fast lets the
            // playback service log a clear reason and skip the track.
            var ext = Path.GetExtension(fileName);
            if (ext != null &&
                (ext.Equals(".vgm", StringComparison.OrdinalIgnoreCase) ||
                 ext.Equals(".vgz", StringComparison.OrdinalIgnoreCase)))
            {
                var sniff = VgmHeaderSniffer.Inspect(fileName);
                if (sniff.IsValidVgm && sniff.UnsupportedChip != null)
                {
                    throw new InvalidOperationException(
                        $"GME cannot play this VGM/VGZ file: it uses {sniff.UnsupportedChip}. " +
                        $"GME only emulates SN76489, YM2413, and YM2612 chips " +
                        $"(Sega Genesis / Master System / Game Gear). See docs/dev_docs/SUPPORTED_FILE_FORMATS.md.");
                }
            }

            // For NSF files, honor the header's starting_song byte (offset 7, 1-based).
            // This is how single-track mini-NSFs produced by NsfHeaderPatcher signal
            // which song in the shared 6502 code blob to play. GME's gme_start_track
            // overrides the header default, so we must explicitly pass the patched index.
            // Other chiptune formats (VGM, SPC, GBS, HES, KSS, SAP, AY, NSFE, GYM) always
            // start at track 0.
            int trackIndex = 0;
            if (ext != null && ext.Equals(".nsf", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var headerBytes = new byte[8];
                    using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        int read = fs.Read(headerBytes, 0, 8);
                        if (read == 8 &&
                            headerBytes[0] == 0x4E && headerBytes[1] == 0x45 &&
                            headerBytes[2] == 0x53 && headerBytes[3] == 0x4D &&
                            headerBytes[4] == 0x1A)
                        {
                            int startingSong = headerBytes[7]; // 1-based in NSF header
                            if (startingSong >= 1)
                                trackIndex = startingSong - 1;
                        }
                    }
                }
                catch
                {
                    // If the header read fails, fall through to track 0 — GME will either
                    // open and play track 0 successfully, or gme_open_file below will fail
                    // with a clearer error.
                }
            }

            IntPtr emu;
            string err = GmeNative.GetError(GmeNative.gme_open_file(fileName, out emu, SampleRate));
            if (err != null)
                throw new InvalidOperationException($"GME failed to open '{fileName}': {err}");
            _emu = emu;

            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

            // Get track duration from metadata for the actual track we'll play.
            IntPtr info;
            err = GmeNative.GetError(GmeNative.gme_track_info(_emu, out info, trackIndex));
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

            // Don't use GME's internal fade — let NAudio's SongEndFade and fader handle transitions.
            // GME's gme_set_fade overlaps with our fader and can cause silent next-track issues.

            err = GmeNative.GetError(GmeNative.gme_start_track(_emu, trackIndex));
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
                lock (_gmeLock)
                {
                    if (_emu == IntPtr.Zero) return 0;
                    int ms = GmeNative.gme_tell(_emu);
                    return (long)ms * SampleRate / 1000 * Channels * sizeof(float);
                }
            }
            set
            {
                lock (_gmeLock)
                {
                    if (_emu == IntPtr.Zero) return;
                    int ms = (int)(value * 1000 / (SampleRate * Channels * sizeof(float)));
                    GmeNative.gme_seek(_emu, ms);
                }
            }
        }

        public override TimeSpan TotalTime => TimeSpan.FromMilliseconds(_playLengthMs);

        public override TimeSpan CurrentTime
        {
            get
            {
                lock (_gmeLock)
                {
                    if (_emu == IntPtr.Zero) return TimeSpan.Zero;
                    return TimeSpan.FromMilliseconds(GmeNative.gme_tell(_emu));
                }
            }
            set
            {
                lock (_gmeLock)
                {
                    if (_emu == IntPtr.Zero) return;
                    GmeNative.gme_seek(_emu, (int)value.TotalMilliseconds);
                }
            }
        }

        // ISampleProvider.Read — the hot path used by NAudio's mixer pipeline.
        // Generates int16 samples from GME, converts to float32 in-place.
        // EOF is signaled by returning 0 when play_length is reached (no GME internal fade).
        public int Read(float[] buffer, int offset, int count)
        {
            lock (_gmeLock)
            {
                if (_emu == IntPtr.Zero)
                    return 0;

                // Check if we've reached the play length
                int posMs = GmeNative.gme_tell(_emu);
                if (posMs >= _playLengthMs)
                    return 0;

                // Clamp to remaining samples so we don't overshoot play_length
                int remainingMs = _playLengthMs - posMs;
                long remainingSamples = (long)remainingMs * SampleRate * Channels / 1000;
                int toRead = (int)Math.Min(count, remainingSamples);
                if (toRead <= 0)
                    return 0;

                // Reuse short buffer if possible
                if (_shortBuffer == null || _shortBuffer.Length < toRead)
                    _shortBuffer = new short[toRead];

                string err = GmeNative.GetError(GmeNative.gme_play(_emu, toRead, _shortBuffer));
                if (err != null)
                    return 0;

                // Convert int16 → float32 with gain boost (retro chips are quieter than modern audio)
                for (int i = 0; i < toRead; i++)
                    buffer[offset + i] = _shortBuffer[i] / 32768f * OutputGain;

                return toRead;
            }
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
                lock (_gmeLock)
                {
                    if (_emu != IntPtr.Zero)
                    {
                        GmeNative.gme_delete(_emu);
                        _emu = IntPtr.Zero;
                    }
                    _shortBuffer = null;
                    _disposed = true;
                }
            }
            base.Dispose(disposing);
        }
    }
}
