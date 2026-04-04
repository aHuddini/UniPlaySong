using NAudio.Wave;

namespace UniPlaySong.Audio;

// Per-sample volume ramp for NAudio pipeline.
// The fader calls SetTargetWithRamp() once per fade phase.
// The audio thread applies linear interpolation per-sample.
// The fader polls IsRamping to detect completion.
class SmoothVolumeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sampleRate;

    // Audio-thread owned state
    private float _currentVolume;
    private float _rampTarget;
    private float _rampStartVolume;
    private int _rampTotalSamples;
    private int _rampPosition;
    private volatile bool _isRamping;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public bool IsRamping => _isRamping;

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

    // Starts a linear ramp from current volume to target over the given duration.
    // Called once per fade phase. If duration <= 0, sets volume immediately.
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
        _isRamping = true;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read == 0) return 0;

        float vol = _currentVolume;

        if (!_isRamping)
        {
            // Steady state: apply constant volume
            if (vol == 0f)
            {
                Array.Clear(buffer, offset, read);
            }
            else if (vol != 1f)
            {
                for (int i = 0; i < read; i++)
                    buffer[offset + i] *= vol;
            }
            return read;
        }

        // Ramping: per-sample linear interpolation
        float startVol = _rampStartVolume;
        float target = _rampTarget;
        int total = _rampTotalSamples;
        int pos = _rampPosition;

        for (int i = 0; i < read; i++)
        {
            pos++;
            if (pos >= total)
            {
                vol = target;
                _isRamping = false;
                buffer[offset + i] *= vol;
                // Fill remaining samples at target volume
                for (int j = i + 1; j < read; j++)
                    buffer[offset + j] *= vol;
                break;
            }

            float progress = (float)pos / total;
            vol = startVol + (target - startVol) * progress;
            buffer[offset + i] *= vol;
        }

        _rampPosition = pos;
        _currentVolume = vol;
        return read;
    }
}
