using System;
using NAudio.Wave;

namespace UniPlaySong.Audio
{
    // Per-sample volume ramp with exponential curves for NAudio pipeline.
    // The fader calls SetTargetWithRamp() once per fade phase.
    // The audio thread applies an exponential curve per-sample — no discrete steps,
    // no timer jitter, no rate-of-change discontinuities through reverb.
    // Curves match SDL2/WPF: fade-in = progress^2, fade-out = 1-(1-progress)^2.
    // The fader polls Volume (getter) to detect when the ramp completes.
    public class SmoothVolumeSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _sampleRate;

        // Audio-thread owned state
        private float _currentVolume;
        private float _rampTarget;
        private float _rampStartVolume;
        private int _rampTotalSamples;
        private int _rampPosition;
        private bool _isRamping;
        private bool _isRampingDown;

        public WaveFormat WaveFormat => _source.WaveFormat;

        // Getter: returns actual current volume (audio-thread owned).
        // The fader polls this to detect ramp completion.
        // Setter: sets volume immediately (no ramp). Used for instant operations.
        public float Volume
        {
            get => _currentVolume;
            set
            {
                _currentVolume = value;
                _rampTarget = value;
                _isRamping = false;
            }
        }

        public SmoothVolumeSampleProvider(ISampleProvider source)
        {
            _source = source;
            _sampleRate = source.WaveFormat.SampleRate;
            _currentVolume = 0f;
            _rampTarget = 0f;
            _isRamping = false;
        }

        // Starts an exponential-curve ramp from current volume to target over the given duration.
        // Called by fader once at the start of each fade phase.
        // If duration <= 0, sets volume immediately.
        public void SetTargetWithRamp(float target, float durationSeconds)
        {
            if (durationSeconds <= 0f)
            {
                Volume = target;
                return;
            }

            _rampTotalSamples = (int)(_sampleRate * WaveFormat.Channels * durationSeconds);
            if (_rampTotalSamples <= 0) _rampTotalSamples = 1;

            _rampStartVolume = _currentVolume;
            _rampTarget = target;
            _rampPosition = 0;
            _isRampingDown = target < _currentVolume;
            _isRamping = true;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (read == 0) return 0;

            float vol = _currentVolume;

            if (!_isRamping)
            {
                // Steady state: apply constant volume (fast path)
                if (vol == 0f)
                {
                    for (int i = 0; i < read; i++)
                        buffer[offset + i] = 0f;
                }
                else if (vol != 1f)
                {
                    for (int i = 0; i < read; i++)
                        buffer[offset + i] *= vol;
                }
                // vol == 1f: pass through unmodified
                return read;
            }

            // Ramping: per-sample exponential curve
            float startVol = _rampStartVolume;
            float target = _rampTarget;
            int total = _rampTotalSamples;
            int pos = _rampPosition;
            bool down = _isRampingDown;

            for (int i = 0; i < read; i++)
            {
                pos++;
                if (pos >= total)
                {
                    vol = target;
                    _isRamping = false;
                    // Fill remaining samples at target volume
                    buffer[offset + i] *= vol;
                    for (int j = i + 1; j < read; j++)
                        buffer[offset + j] *= vol;
                    break;
                }

                float progress = (float)pos / total;

                if (down)
                {
                    // Fade-out: 1-(1-progress)^2 — starts slow, speeds up
                    float curve = 1f - (1f - progress) * (1f - progress);
                    vol = startVol * (1f - curve);
                }
                else
                {
                    // Fade-in: progress^2 — starts fast, slows down
                    float curve = progress * progress;
                    vol = startVol + (target - startVol) * curve;
                }

                buffer[offset + i] *= vol;
            }

            _rampPosition = pos;
            _currentVolume = vol;
            return read;
        }
    }
}
