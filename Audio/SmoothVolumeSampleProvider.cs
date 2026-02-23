using System;
using NAudio.Wave;

namespace UniPlaySong.Audio
{
    // Per-sample volume ramp with configurable curves for NAudio pipeline.
    // The fader calls SetTargetWithRamp() once per fade phase.
    // The audio thread applies the selected curve per-sample — no discrete steps,
    // no timer jitter, no rate-of-change discontinuities through reverb.
    // The fader polls Volume (getter) to detect when the ramp completes.
    public class SmoothVolumeSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _sampleRate;
        private readonly Func<FadeCurveType> _getFadeInCurve;
        private readonly Func<FadeCurveType> _getFadeOutCurve;

        // Audio-thread owned state
        private float _currentVolume;
        private float _rampTarget;
        private float _rampStartVolume;
        private int _rampTotalSamples;
        private int _rampPosition;
        private bool _isRamping;
        private bool _isRampingDown;

        // Snapshotted curve for current ramp (captured when ramp starts)
        private FadeCurveType _activeCurve;

        // Diagnostic: track ramp transitions for logging
        private int _rampCompletionCount;
        private float _lastRampStartVol;
        private float _lastRampEndVol;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public bool IsRamping => _isRamping;

        // Diagnostic snapshot for logging
        public string DiagSnapshot => $"vol={_currentVolume:F4}, target={_rampTarget:F4}, ramping={_isRamping}, down={_isRampingDown}, pos={_rampPosition}/{_rampTotalSamples}, completions={_rampCompletionCount}, curve={_activeCurve}";

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

        public SmoothVolumeSampleProvider(ISampleProvider source, Func<FadeCurveType> getFadeInCurve = null, Func<FadeCurveType> getFadeOutCurve = null)
        {
            _source = source;
            _sampleRate = source.WaveFormat.SampleRate;
            _currentVolume = 0f;
            _rampTarget = 0f;
            _isRamping = false;
            _getFadeInCurve = getFadeInCurve ?? (() => FadeCurveType.Quadratic);
            _getFadeOutCurve = getFadeOutCurve ?? (() => FadeCurveType.Cubic);
        }

        // Starts a curve ramp from current volume to target over the given duration.
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
            _lastRampStartVol = _currentVolume;
            _rampTarget = target;
            _rampPosition = 0;
            _isRampingDown = target < _currentVolume;
            _activeCurve = _isRampingDown ? _getFadeOutCurve() : _getFadeInCurve();
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

            // Ramping: per-sample curve
            float startVol = _rampStartVolume;
            float target = _rampTarget;
            int total = _rampTotalSamples;
            int pos = _rampPosition;
            bool down = _isRampingDown;
            var curve = _activeCurve;

            for (int i = 0; i < read; i++)
            {
                pos++;
                if (pos >= total)
                {
                    vol = target;
                    _isRamping = false;
                    _lastRampEndVol = vol;
                    _rampCompletionCount++;
                    // Fill remaining samples at target volume
                    buffer[offset + i] *= vol;
                    for (int j = i + 1; j < read; j++)
                        buffer[offset + j] *= vol;
                    break;
                }

                float progress = (float)pos / total;
                vol = ApplyCurve(progress, startVol, target, down, curve);
                buffer[offset + i] *= vol;
            }

            _rampPosition = pos;
            _currentVolume = vol;
            return read;
        }

        private static float ApplyCurve(float progress, float startVol, float target, bool down, FadeCurveType curve)
        {
            float range = target - startVol;

            switch (curve)
            {
                case FadeCurveType.Linear:
                    return startVol + range * progress;

                case FadeCurveType.Quadratic:
                    if (down)
                    {
                        float inv = 1f - progress;
                        return startVol * inv * inv;
                    }
                    else
                    {
                        float c = progress * progress;
                        return startVol + range * c;
                    }

                case FadeCurveType.Cubic:
                    if (down)
                    {
                        float inv = 1f - progress;
                        return startVol * inv * inv * inv;
                    }
                    else
                    {
                        float c = progress * progress * progress;
                        return startVol + range * c;
                    }

                case FadeCurveType.SCurve:
                    // Smoothstep: 3t²-2t³ — gentle start and end, fast middle
                    float s = progress * progress * (3f - 2f * progress);
                    return startVol + range * s;

                case FadeCurveType.Logarithmic:
                    if (down)
                    {
                        // Log decay: fast initial drop, slow tail
                        float logScale = 1f - (float)(Math.Log(1.0 + progress * 9.0) / Math.Log(10.0));
                        return startVol * Math.Max(0f, logScale);
                    }
                    else
                    {
                        // Log rise: fast initial rise, slow approach to target
                        float logScale = (float)(Math.Log(1.0 + progress * 9.0) / Math.Log(10.0));
                        return startVol + range * logScale;
                    }

                default:
                    return startVol + range * progress;
            }
        }
    }
}
