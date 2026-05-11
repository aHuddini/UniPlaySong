using System;
using NAudio.Dsp;
using NAudio.Wave;
using UniPlaySong.Services;

namespace UniPlaySong.Audio
{
    // Calm Down Mode: a post-mixer audio processor that applies a low-pass filter +
    // volume attenuation, with an S-curve crossfade on toggle so transitions sound
    // gradual rather than abrupt.
    //
    // Placement: between _volumeProvider and _outputDevice in NAudioMusicPlayer. This
    // means it acts on the combined mixer output (works during crossfade, doesn't
    // disturb per-song chains, and is independent of LiveEffectsEnabled).
    //
    // Toggle behavior:
    //   - Reads CalmDownModeEnabled from settings each Read() (samples-driven polling)
    //   - When the setting flips, starts an S-curve ramp over CalmDownTransitionDurationSeconds
    //   - Filter coefficients are interpolated by simply ramping the dry/wet mix:
    //     mix = (1-strength)*dry + strength*filtered. strength S-curves 0→1 (on) or 1→0 (off).
    //   - Volume attenuation similarly ramps from 1.0 to CalmDownVolumeMultiplier.
    public class CalmDownProcessor : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly SettingsService _settingsService;
        private readonly int _sampleRate;
        private readonly int _channels;

        // BiQuad low-pass filters (one per channel — stereo)
        private BiQuadFilter _lowPassL;
        private BiQuadFilter _lowPassR;
        private float _currentCutoffHz;

        // Ramp state (audio-thread owned)
        // _strength: 0.0 = fully bypassed (dry), 1.0 = fully calm (filtered+attenuated)
        private float _currentStrength;
        private float _rampStartStrength;
        private float _rampTargetStrength;
        private int _rampTotalSamples;
        private int _rampPosition;
        private bool _isRamping;

        // Cached toggle state so we can detect flips without taking a lock
        private bool _lastSeenEnabled;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public bool IsCalmActive => _currentStrength > 0.001f || _isRamping;

        public CalmDownProcessor(ISampleProvider source, SettingsService settingsService)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _sampleRate = source.WaveFormat.SampleRate;
            _channels = source.WaveFormat.Channels;

            var s = _settingsService.Current;
            _lastSeenEnabled = s?.CalmDownModeEnabled ?? false;
            // Initialize at the steady-state strength matching the persisted toggle
            // so reopening Playnite with the setting on doesn't fade in audibly.
            _currentStrength = _lastSeenEnabled ? 1f : 0f;
            _rampTargetStrength = _currentStrength;
            _isRamping = false;

            _currentCutoffHz = s?.CalmDownLowPassCutoffHz ?? 3000f;
            _lowPassL = BiQuadFilter.LowPassFilter(_sampleRate, _currentCutoffHz, 0.707f);
            _lowPassR = BiQuadFilter.LowPassFilter(_sampleRate, _currentCutoffHz, 0.707f);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (read == 0) return 0;

            var s = _settingsService.Current;
            if (s == null) return read;

            // --- Detect toggle flip and arm a ramp ----------------------------
            bool wantEnabled = s.CalmDownModeEnabled;
            if (wantEnabled != _lastSeenEnabled)
            {
                _lastSeenEnabled = wantEnabled;
                float durationSeconds = (float)Math.Max(0.05, s.CalmDownTransitionDurationSeconds);
                StartRamp(wantEnabled ? 1f : 0f, durationSeconds);
            }

            // --- Refresh filter coefficients if cutoff changed ----------------
            float wantedCutoff = s.CalmDownLowPassCutoffHz;
            if (Math.Abs(wantedCutoff - _currentCutoffHz) > 0.5f)
            {
                _currentCutoffHz = wantedCutoff;
                _lowPassL.SetLowPassFilter(_sampleRate, _currentCutoffHz, 0.707f);
                _lowPassR.SetLowPassFilter(_sampleRate, _currentCutoffHz, 0.707f);
            }

            // --- Fast bypass when fully dry and not ramping -------------------
            if (!_isRamping && _currentStrength <= 0.0001f)
            {
                return read;
            }

            float volumeMultiplier = s.CalmDownVolumeMultiplier;
            if (volumeMultiplier < 0f) volumeMultiplier = 0f;
            if (volumeMultiplier > 1f) volumeMultiplier = 1f;

            // Process per-sample. Channels are interleaved (stereo float).
            // Each frame = _channels samples (L, R for stereo).
            int frames = read / _channels;
            int idx = offset;
            float strength = _currentStrength;
            float startS = _rampStartStrength;
            float targetS = _rampTargetStrength;
            int total = _rampTotalSamples;
            int pos = _rampPosition;
            bool ramping = _isRamping;

            for (int f = 0; f < frames; f++)
            {
                if (ramping)
                {
                    pos++;
                    if (pos >= total)
                    {
                        strength = targetS;
                        ramping = false;
                    }
                    else
                    {
                        // Smoothstep S-curve: 3t² - 2t³
                        float t = (float)pos / total;
                        float sc = t * t * (3f - 2f * t);
                        strength = startS + (targetS - startS) * sc;
                    }
                }

                // strength 0..1 — mix dry/filtered and attenuate by volume curve
                // Volume curve goes from 1.0 (dry) to volumeMultiplier (full calm)
                float effectiveVolume = 1f + (volumeMultiplier - 1f) * strength;

                if (_channels == 1)
                {
                    float dry = buffer[idx];
                    float wet = _lowPassL.Transform(dry);
                    float mixed = dry + (wet - dry) * strength;
                    buffer[idx] = mixed * effectiveVolume;
                    idx++;
                }
                else
                {
                    // Stereo (or more — but UPS mixer is 2ch). Process first 2 channels
                    // through L/R filters and pass any extras through unfiltered.
                    float dryL = buffer[idx];
                    float wetL = _lowPassL.Transform(dryL);
                    buffer[idx] = (dryL + (wetL - dryL) * strength) * effectiveVolume;

                    float dryR = buffer[idx + 1];
                    float wetR = _lowPassR.Transform(dryR);
                    buffer[idx + 1] = (dryR + (wetR - dryR) * strength) * effectiveVolume;

                    idx += 2;

                    // Skip any extra channels (shouldn't happen at mixer output, but be safe)
                    for (int c = 2; c < _channels; c++)
                    {
                        buffer[idx] *= effectiveVolume;
                        idx++;
                    }
                }
            }

            _currentStrength = strength;
            _rampPosition = pos;
            _isRamping = ramping;
            return read;
        }

        private void StartRamp(float target, float durationSeconds)
        {
            if (durationSeconds <= 0f)
            {
                _currentStrength = target;
                _rampTargetStrength = target;
                _isRamping = false;
                return;
            }

            _rampStartStrength = _currentStrength;
            _rampTargetStrength = target;
            // Ramp is in *frames*, not interleaved samples — Read() advances pos once per frame.
            _rampTotalSamples = (int)(_sampleRate * durationSeconds);
            if (_rampTotalSamples <= 0) _rampTotalSamples = 1;
            _rampPosition = 0;
            _isRamping = true;
        }
    }
}
