# NAudio Smooth Volume Fix — Implementation Plan

> **Status:** Attempted and reverted (2026-02-22). See design doc for test results and next steps.

**Goal:** Eliminate tremolo/stutter artifact in NAudio mode by replacing `VolumeSampleProvider` with a per-sample linear ramp provider.

**Architecture:** Drop-in swap — one new file, three lines changed in NAudioMusicPlayer.cs. Zero changes to MusicFader, MusicPlaybackService, or SDL2MusicPlayer.

---

## Task 1: Create SmoothVolumeSampleProvider

**Files:**
- Create: `Audio/SmoothVolumeSampleProvider.cs`

**Implementation:**

```csharp
using System.Runtime.CompilerServices;
using NAudio.Wave;

namespace UniPlaySong.Audio
{
    // Per-sample linear volume ramp: smooths the fader's ~60 discrete volume steps/sec
    // into continuous interpolation on the audio thread. Eliminates discontinuities that
    // reverb's comb filters would otherwise amplify into audible tremolo.
    public class SmoothVolumeSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _rampSamples;
        private float _currentVolume;
        private volatile float _targetVolume;
        private float _rampIncrement;
        private bool _isRamping;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public float Volume
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _currentVolume;
            set
            {
                float target = value;
                _targetVolume = target;
                float delta = target - _currentVolume;
                if (delta == 0f || _rampSamples <= 1)
                {
                    _currentVolume = target;
                    _isRamping = false;
                    return;
                }
                _rampIncrement = delta / _rampSamples;
                _isRamping = true;
            }
        }

        public SmoothVolumeSampleProvider(ISampleProvider source)
        {
            _source = source;
            _currentVolume = 0f;
            _targetVolume = 0f;
            _isRamping = false;
            // ~16ms of samples at source sample rate x channels (interleaved)
            _rampSamples = (int)(source.WaveFormat.SampleRate * source.WaveFormat.Channels * 0.016f);
            if (_rampSamples < 1) _rampSamples = 1;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (read == 0) return 0;

            float vol = _currentVolume;

            if (!_isRamping)
            {
                // Steady state: constant volume (fast paths)
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

            // Ramping: per-sample linear interpolation
            float inc = _rampIncrement;
            float target = _targetVolume;
            bool rampingUp = inc > 0f;

            for (int i = 0; i < read; i++)
            {
                buffer[offset + i] *= vol;
                vol += inc;
                // Clamp at target
                if (rampingUp ? vol >= target : vol <= target)
                {
                    vol = target;
                    _isRamping = false;
                    inc = 0f; // Rest of buffer at constant target volume
                }
            }

            _currentVolume = vol;
            return read;
        }
    }
}
```

## Task 2: Wire into NAudioMusicPlayer

**Files:**
- Modify: `Services/NAudioMusicPlayer.cs`

**Changes (3 lines):**

1. Remove `using NAudio.Wave.SampleProviders;` (only used for VolumeSampleProvider)
2. Change field: `private VolumeSampleProvider _volumeProvider;` → `private SmoothVolumeSampleProvider _volumeProvider;`
3. Change construction: `new VolumeSampleProvider(_visualizationProvider)` → `new SmoothVolumeSampleProvider(_visualizationProvider)`

No other changes needed — `Volume` property setter already casts to `(float)value`, and `using UniPlaySong.Audio;` already exists.

## Task 3: Build and Package

```bash
dotnet clean -c Release
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```

## Test Results

**Initial testing (2026-02-22):** Artifact was absent for the first few game switches, then returned. This suggests per-sample volume smoothing helps but does not fully resolve the issue. The changes were reverted.

**Next steps:** Consider Option 1 (fader rewrite with `SetVolumeRamp()` + `DispatcherTimer` monitor) or investigate whether the per-song WaveOutEvent recreation contributes to the artifact independently of volume stepping.
