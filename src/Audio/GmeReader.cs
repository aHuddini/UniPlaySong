using System;
using System.Collections.Generic;
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
        private int _playLengthMs;
        private readonly long _totalSamples;
        // Lowered below _playLengthMs when GME signals an explicit track end
        // (short NSFs with end markers). Initialized to _playLengthMs in ctor.
        private int _effectiveEndMs;
        private short[] _shortBuffer;
        private bool _disposed;

        // Multi-track HES support. When the file is .hes AND a sibling .m3u sidecar
        // exists, _hesTracks holds the parsed track list and Read() auto-advances
        // through them in M3U order, so the entire file plays as one continuous
        // stream that NAudio sees as a single song. Empty/null for non-HES paths
        // and HES files without a sidecar (legacy single-track behavior).
        private List<HesTrackEntry> _hesTracks;
        private int _hesCurrentIndex;     // position within _hesTracks
        // Accumulated playtime in ms from prior HES tracks (for global Position reporting).
        // gme_tell resets to 0 after each gme_start_track call, so we add this offset
        // to convert "current track pos" → "position into the whole file".
        private int _hesPriorTracksMs;

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
            //
            // For HES files, look for a sibling .m3u sidecar. If found, parse it into
            // an ordered track list and start at the first M3U entry (NOT the header's
            // first_track, which is often "$00" but the M3U may legitimately re-order).
            // Read() then auto-advances through the M3U so all 26+ scattered tracks
            // play sequentially as one logical song. See docs/dev_docs/HES_FORMAT.md.
            //
            // Other chiptune formats (VGM, SPC, GBS, KSS, SAP, AY, NSFE, GYM) always
            // start at track 0.
            int trackIndex = 0;
            if (ext != null && ext.Equals(".hes", StringComparison.OrdinalIgnoreCase))
            {
                _hesTracks = HesM3uParser.LoadFor(fileName);
                if (_hesTracks != null && _hesTracks.Count > 0)
                {
                    _hesCurrentIndex = 0;
                    _hesPriorTracksMs = 0;
                    trackIndex = _hesTracks[0].TrackIndex;
                }
            }
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

            // For NSF files, check nsf-loops.json in the containing folder for a
            // per-file override. Applies only when the user has explicitly saved one
            // via the NSF Manager's Edit Loops tab. Silent fallback on any error.
            if (ext != null && ext.Equals(".nsf", StringComparison.OrdinalIgnoreCase))
            {
                int? overrideMs = NsfLoopManifest.ReadMillisecondsFor(fileName);
                if (overrideMs.HasValue)
                    _playLengthMs = overrideMs.Value;
            }

            // For multi-track HES, _playLengthMs reported above came from gme_track_info
            // for just the first track. Replace with the sum of all M3U track durations
            // so NAudio's Length/TotalTime reflect the actual file playtime. Tracks
            // without a duration in the M3U fall back to the GME default (2.5 minutes).
            if (_hesTracks != null && _hesTracks.Count > 0)
            {
                int total = 0;
                foreach (var t in _hesTracks)
                {
                    total += t.DurationMs ?? 150000;
                }
                _playLengthMs = total;
                _effectiveEndMs = _hesTracks[0].DurationMs ?? 150000;
            }
            else
            {
                _effectiveEndMs = _playLengthMs;
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
                    // For multi-track HES, add the accumulated duration of finished
                    // tracks so the reported position is monotonic across the whole file.
                    int ms = GmeNative.gme_tell(_emu) + _hesPriorTracksMs;
                    return (long)ms * SampleRate / 1000 * Channels * sizeof(float);
                }
            }
            set
            {
                lock (_gmeLock)
                {
                    if (_emu == IntPtr.Zero) return;
                    // Seeking across HES track boundaries isn't supported; pass through
                    // to the current track's position. UPS doesn't seek inside chiptune
                    // files in practice, so this is acceptable.
                    int ms = (int)(value * 1000 / (SampleRate * Channels * sizeof(float)));
                    GmeNative.gme_seek(_emu, Math.Max(0, ms - _hesPriorTracksMs));
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
                    return TimeSpan.FromMilliseconds(GmeNative.gme_tell(_emu) + _hesPriorTracksMs);
                }
            }
            set
            {
                lock (_gmeLock)
                {
                    if (_emu == IntPtr.Zero) return;
                    GmeNative.gme_seek(_emu, Math.Max(0, (int)value.TotalMilliseconds - _hesPriorTracksMs));
                }
            }
        }

        // ISampleProvider.Read — the hot path used by NAudio's mixer pipeline.
        // Generates int16 samples from GME, converts to float32 in-place.
        // EOF is signaled by returning 0 when play_length is reached OR GME reports
        // an explicit track-end (short NSF jingles, tracks with end markers).
        // For multi-track HES with an M3U sidecar, advances to the next M3U track
        // instead of returning 0; only signals EOF after the last track ends.
        public int Read(float[] buffer, int offset, int count)
        {
            lock (_gmeLock)
            {
                if (_emu == IntPtr.Zero)
                    return 0;

                // Check if we've reached the effective end (either play_length or
                // an earlier boundary set by GME's track-end signal).
                int posMs = GmeNative.gme_tell(_emu);
                if (posMs >= _effectiveEndMs)
                {
                    // Multi-track HES: advance to the next M3U track and keep going.
                    // Only signal real EOF after the last track. Returns 0 from this
                    // call so NAudio's buffer boundary stays clean; the next Read()
                    // call will start producing samples from the new track.
                    if (TryAdvanceHesTrack())
                        return 0;
                    return 0;
                }

                // Clamp to remaining samples so we don't overshoot the effective end
                int remainingMs = _effectiveEndMs - posMs;
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

                // Honor GME's internal track-end signal for short NSFs with explicit end
                // markers (jingles, stingers). Guarded by a 2-second minimum to avoid
                // false EOF at track init when chips emit brief silence. Tracks without
                // an explicit end marker (looping BGM) never trigger this.
                if (posMs >= 2000 && GmeNative.gme_track_ended(_emu) != 0)
                    _effectiveEndMs = posMs;

                return toRead;
            }
        }

        // For multi-track HES files with an M3U sidecar: when the current track ends,
        // start the next M3U entry, accumulate its duration into the global offset,
        // and reset _effectiveEndMs to the new track's duration. Returns true while
        // there are more tracks to play, false after the last one (real EOF).
        // Caller MUST hold _gmeLock.
        private bool TryAdvanceHesTrack()
        {
            if (_hesTracks == null || _hesTracks.Count == 0) return false;
            if (_hesCurrentIndex + 1 >= _hesTracks.Count) return false;
            if (_emu == IntPtr.Zero) return false;

            // Bank the duration of the track we just finished into the global offset
            // so Position/CurrentTime keep advancing monotonically across the whole file.
            var finishedTrack = _hesTracks[_hesCurrentIndex];
            _hesPriorTracksMs += finishedTrack.DurationMs ?? 150000;

            _hesCurrentIndex++;
            var next = _hesTracks[_hesCurrentIndex];

            string err = GmeNative.GetError(GmeNative.gme_start_track(_emu, next.TrackIndex));
            if (err != null)
            {
                // Couldn't start the next track. Treat as EOF so we don't loop forever
                // on a broken sidecar entry. The user will hear the file end early but
                // won't lose playback entirely.
                return false;
            }

            // gme_tell now reads 0 (start of new track). Set the effective-end window
            // to this track's duration; Read() will compare against gme_tell directly.
            _effectiveEndMs = next.DurationMs ?? 150000;
            return true;
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
