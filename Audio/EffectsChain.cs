using System;
using NAudio.Dsp;
using NAudio.Wave;
using UniPlaySong.Services;

namespace UniPlaySong.Audio
{
    /// <summary>
    /// Audio effects chain implementing ISampleProvider.
    /// Applies high-pass, low-pass, reverb (libSoX/Audacity algorithm), and makeup gain.
    /// Reads effect parameters directly from SettingsService for real-time changes.
    /// </summary>
    public class EffectsChain : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly SettingsService _settingsService;
        private readonly int _channels;
        private readonly int _sampleRate;

        // Pre-reverb filters (one per channel for stereo)
        private BiQuadFilter _highPassL;
        private BiQuadFilter _highPassR;
        private BiQuadFilter _lowPassL;
        private BiQuadFilter _lowPassR;

        // ===== libSoX/Audacity reverb constants =====
        private const int NumCombs = 8;
        private const int NumAllpasses = 4;
        private const int BaseStereoSpread = 12;  // libSoX base stereo spread

        // Base comb filter delays at 44.1kHz (same as Freeverb/libSoX)
        private static readonly int[] BaseCombDelays = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        // Base all-pass filter delays at 44.1kHz (libSoX order)
        private static readonly int[] BaseAllpassDelays = { 225, 341, 441, 556 };

        // Soft-knee limiter constants
        private const float LimiterThreshold = 0.9f;  // Start limiting at ~-1dB

        // ===== Advanced Tuning Constants =====
        // These affect the character and intensity of the reverb effect.
        // WARNING: Extreme values can cause loud output that may damage hearing!
        //
        // WetGainMultiplier: Scales the reverb wet signal output.
        //   - Default: 0.08 (balanced for most presets)
        //   - Range: 0.01 (very subtle) to 0.25 (extremely pronounced)
        //   - The 8 parallel comb filters produce loud output; this scales it down.
        //   - Higher values = more reverb presence but risk of clipping/distortion.
        //
        // AllpassFeedback: Controls diffusion in all-pass filters.
        //   - Default: 0.5 (standard Freeverb value)
        //   - Range: 0.3 (less diffuse, more distinct echoes) to 0.7 (smoother, more blended)
        //   - Higher values create smoother, more "washed out" reverb.
        //
        // HfDampingMin/Max: Controls the range of high-frequency damping.
        //   - Default: 0.2 to 0.5 (maps 0-100% damping slider)
        //   - Lower min = brighter reverb tail at low damping settings
        //   - Higher max = darker, more muffled reverb at high damping settings
        //
        // RoomScaleMin/Max: Controls the range of room size scaling.
        //   - Default: 0.1 to 1.0 (maps 0-100% room size slider)
        //   - Affects comb filter delay lengths (perceived room size)
        private const float DefaultWetGainMultiplier = 0.03f;
        private const float DefaultAllpassFeedback = 0.5f;
        private const float DefaultHfDampingMin = 0.2f;
        private const float DefaultHfDampingMax = 0.5f;
        private const float DefaultRoomScaleMin = 0.1f;
        private const float DefaultRoomScaleMax = 1.0f;

        // Pre-delay buffer (FIFO)
        private float[] _preDelayBufferL;
        private float[] _preDelayBufferR;
        private int _preDelayIndex;
        private int _preDelaySize;
        private int _maxPreDelaySize;

        // Comb filter state (L/R channels)
        private readonly float[][] _combBuffersL = new float[NumCombs][];
        private readonly float[][] _combBuffersR = new float[NumCombs][];
        private readonly int[] _combIndexL = new int[NumCombs];
        private readonly int[] _combIndexR = new int[NumCombs];
        private readonly float[] _combStoreL = new float[NumCombs];
        private readonly float[] _combStoreR = new float[NumCombs];
        private readonly int[] _combSizeL = new int[NumCombs];
        private readonly int[] _combSizeR = new int[NumCombs];

        // All-pass filter state (L/R channels)
        private readonly float[][] _allpassBuffersL = new float[NumAllpasses][];
        private readonly float[][] _allpassBuffersR = new float[NumAllpasses][];
        private readonly int[] _allpassIndexL = new int[NumAllpasses];
        private readonly int[] _allpassIndexR = new int[NumAllpasses];
        private readonly int[] _allpassSizeL = new int[NumAllpasses];
        private readonly int[] _allpassSizeR = new int[NumAllpasses];

        // One-pole filters for post-reverb tone control (L/R)
        private OnePoleFilter _toneHpL, _toneHpR;
        private OnePoleFilter _toneLpL, _toneLpR;

        // Slow effect (resampling) state
        private float[] _resampleBuffer;
        private double _resamplePosition;

        // Cache last settings to detect changes
        private int _lastHighPassCutoff;
        private int _lastLowPassCutoff;
        private int _lastRoomSize;
        private int _lastPreDelay;
        private int _lastToneLow;
        private int _lastToneHigh;

        public WaveFormat WaveFormat => _source.WaveFormat;

        /// <summary>
        /// Resets all effect buffers to clear any residual audio.
        /// Call this when switching songs to prevent reverb tail bleed.
        /// </summary>
        public void Reset()
        {
            // Clear pre-delay buffers
            if (_preDelayBufferL != null)
                Array.Clear(_preDelayBufferL, 0, _preDelayBufferL.Length);
            if (_preDelayBufferR != null)
                Array.Clear(_preDelayBufferR, 0, _preDelayBufferR.Length);
            _preDelayIndex = 0;

            // Clear comb filter buffers and state
            for (int i = 0; i < NumCombs; i++)
            {
                if (_combBuffersL[i] != null)
                    Array.Clear(_combBuffersL[i], 0, _combBuffersL[i].Length);
                if (_combBuffersR[i] != null)
                    Array.Clear(_combBuffersR[i], 0, _combBuffersR[i].Length);
                _combIndexL[i] = 0;
                _combIndexR[i] = 0;
                _combStoreL[i] = 0;
                _combStoreR[i] = 0;
            }

            // Clear all-pass filter buffers
            for (int i = 0; i < NumAllpasses; i++)
            {
                if (_allpassBuffersL[i] != null)
                    Array.Clear(_allpassBuffersL[i], 0, _allpassBuffersL[i].Length);
                if (_allpassBuffersR[i] != null)
                    Array.Clear(_allpassBuffersR[i], 0, _allpassBuffersR[i].Length);
                _allpassIndexL[i] = 0;
                _allpassIndexR[i] = 0;
            }

            // Reinitialize tone filters to clear state
            InitializeToneFilters(_sampleRate, 0, 100);

            // Reset slow effect state
            _resamplePosition = 0.0;

            // Reinitialize pre-reverb filters to clear state
            var settings = _settingsService.Current;
            _highPassL = BiQuadFilter.HighPassFilter(_sampleRate, settings.HighPassCutoff, 1f);
            _highPassR = BiQuadFilter.HighPassFilter(_sampleRate, settings.HighPassCutoff, 1f);
            _lowPassL = BiQuadFilter.LowPassFilter(_sampleRate, settings.LowPassCutoff, 1f);
            _lowPassR = BiQuadFilter.LowPassFilter(_sampleRate, settings.LowPassCutoff, 1f);
        }

        public EffectsChain(ISampleProvider source, SettingsService settingsService)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _channels = source.WaveFormat.Channels;
            _sampleRate = source.WaveFormat.SampleRate;

            // Initialize pre-reverb filters
            InitializeFilters(_sampleRate);

            // Initialize reverb with current settings
            var settings = _settingsService.Current;
            InitializeReverb(_sampleRate, settings.ReverbRoomSize);
            InitializePreDelay(_sampleRate, settings.ReverbPreDelay);
            _lastRoomSize = settings.ReverbRoomSize;
            _lastPreDelay = settings.ReverbPreDelay;

            // Initialize tone filters with current settings
            InitializeToneFilters(_sampleRate, settings.ReverbToneLow, settings.ReverbToneHigh);
            _lastToneLow = settings.ReverbToneLow;
            _lastToneHigh = settings.ReverbToneHigh;
        }

        private void InitializeFilters(int sampleRate)
        {
            var settings = _settingsService.Current;

            _highPassL = BiQuadFilter.HighPassFilter(sampleRate, settings.HighPassCutoff, 1f);
            _highPassR = BiQuadFilter.HighPassFilter(sampleRate, settings.HighPassCutoff, 1f);
            _lowPassL = BiQuadFilter.LowPassFilter(sampleRate, settings.LowPassCutoff, 1f);
            _lowPassR = BiQuadFilter.LowPassFilter(sampleRate, settings.LowPassCutoff, 1f);

            _lastHighPassCutoff = settings.HighPassCutoff;
            _lastLowPassCutoff = settings.LowPassCutoff;
        }

        private void InitializePreDelay(int sampleRate, int preDelayMs)
        {
            // Max pre-delay of 200ms
            _maxPreDelaySize = (int)(sampleRate * 0.2) + 1;
            _preDelayBufferL = new float[_maxPreDelaySize];
            _preDelayBufferR = new float[_maxPreDelaySize];
            _preDelaySize = Math.Max(1, (int)(preDelayMs / 1000.0 * sampleRate));
            _preDelayIndex = 0;
        }

        private void InitializeReverb(int sampleRate, int roomSizePercent)
        {
            double r = sampleRate / 44100.0;
            double scale = roomSizePercent / 100.0 * 0.9 + 0.1;  // libSoX: 0.1 to 1.0

            // Initialize comb filters
            for (int i = 0; i < NumCombs; i++)
            {
                // Left channel
                _combSizeL[i] = (int)(scale * r * BaseCombDelays[i] + 0.5);
                if (_combSizeL[i] < 1) _combSizeL[i] = 1;
                _combBuffersL[i] = new float[_combSizeL[i]];
                _combIndexL[i] = 0;
                _combStoreL[i] = 0;

                // Right channel (with stereo spread)
                _combSizeR[i] = (int)(scale * r * (BaseCombDelays[i] + BaseStereoSpread) + 0.5);
                if (_combSizeR[i] < 1) _combSizeR[i] = 1;
                _combBuffersR[i] = new float[_combSizeR[i]];
                _combIndexR[i] = 0;
                _combStoreR[i] = 0;
            }

            // Initialize all-pass filters
            for (int i = 0; i < NumAllpasses; i++)
            {
                // Left channel
                _allpassSizeL[i] = (int)(r * BaseAllpassDelays[i] + 0.5);
                if (_allpassSizeL[i] < 1) _allpassSizeL[i] = 1;
                _allpassBuffersL[i] = new float[_allpassSizeL[i]];
                _allpassIndexL[i] = 0;

                // Right channel (with stereo spread)
                _allpassSizeR[i] = (int)(r * (BaseAllpassDelays[i] + BaseStereoSpread) + 0.5);
                if (_allpassSizeR[i] < 1) _allpassSizeR[i] = 1;
                _allpassBuffersR[i] = new float[_allpassSizeR[i]];
                _allpassIndexR[i] = 0;
            }
        }

        private void InitializeToneFilters(int sampleRate, double toneLow, double toneHigh)
        {
            // libSoX formula: fc = midi_to_freq(72 +/- tone/100 * 48)
            double fcHighpass = MidiToFreq(72 - toneLow / 100.0 * 48);
            double fcLowpass = MidiToFreq(72 + toneHigh / 100.0 * 48);

            _toneHpL = new OnePoleFilter(sampleRate, fcHighpass, true);
            _toneHpR = new OnePoleFilter(sampleRate, fcHighpass, true);
            _toneLpL = new OnePoleFilter(sampleRate, fcLowpass, false);
            _toneLpR = new OnePoleFilter(sampleRate, fcLowpass, false);
        }

        private void UpdateToneFiltersIfNeeded(UniPlaySongSettings settings)
        {
            if (_lastToneLow != settings.ReverbToneLow || _lastToneHigh != settings.ReverbToneHigh)
            {
                InitializeToneFilters(_sampleRate, settings.ReverbToneLow, settings.ReverbToneHigh);
                _lastToneLow = settings.ReverbToneLow;
                _lastToneHigh = settings.ReverbToneHigh;
            }
        }

        private static double MidiToFreq(double midiNote)
        {
            return 440.0 * Math.Pow(2, (midiNote - 69) / 12.0);
        }

        /// <summary>
        /// Process input through pre-delay buffer, returns delayed sample
        /// </summary>
        private float ProcessPreDelay(float input, float[] buffer, int preDelaySize)
        {
            // Read from buffer (delayed output)
            int readIndex = (_preDelayIndex - preDelaySize + _maxPreDelaySize) % _maxPreDelaySize;
            float output = buffer[readIndex];

            // Write input to current position
            buffer[_preDelayIndex] = input;

            return output;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var settings = _settingsService.Current;
            bool slowActive = settings.SlowEnabled && settings.SlowAmount > 0;

            int samplesRead;

            if (slowActive)
            {
                // Speed: SlowAmount 0-50 maps to 1.0x-0.5x
                double speed = 1.0 - (settings.SlowAmount / 100.0);

                // Calculate source samples needed (fewer than output) + margin for interpolation
                int sourceSamplesNeeded = (int)(count * speed) + _channels + 2;

                // Ensure resample buffer is large enough
                if (_resampleBuffer == null || _resampleBuffer.Length < sourceSamplesNeeded)
                    _resampleBuffer = new float[sourceSamplesNeeded + 64];

                int sourceRead = _source.Read(_resampleBuffer, 0, sourceSamplesNeeded);
                if (sourceRead == 0)
                    return 0;

                // Linear interpolation resampling (frame-based for stereo correctness)
                int outputFrames = count / _channels;
                int sourceFrames = sourceRead / _channels;
                int outputSampleIndex = 0;

                for (int frame = 0; frame < outputFrames; frame++)
                {
                    int srcFrame = (int)_resamplePosition;
                    double frac = _resamplePosition - srcFrame;

                    if (srcFrame + 1 >= sourceFrames)
                        break;

                    for (int ch = 0; ch < _channels; ch++)
                    {
                        float s0 = _resampleBuffer[srcFrame * _channels + ch];
                        float s1 = _resampleBuffer[(srcFrame + 1) * _channels + ch];
                        buffer[offset + outputSampleIndex] = (float)(s0 + (s1 - s0) * frac);
                        outputSampleIndex++;
                    }

                    _resamplePosition += speed;
                }

                // Carry over fractional position for next call
                int framesConsumed = (int)_resamplePosition;
                _resamplePosition -= framesConsumed;

                samplesRead = outputSampleIndex;
            }
            else
            {
                samplesRead = _source.Read(buffer, offset, count);
                // Reset resample position when slow is disabled
                _resamplePosition = 0.0;
            }

            if (samplesRead == 0)
                return 0;

            // Update filters if cutoff frequencies changed
            if (_lastHighPassCutoff != settings.HighPassCutoff ||
                _lastLowPassCutoff != settings.LowPassCutoff)
            {
                UpdateFilters(_sampleRate, settings);
            }

            // Update pre-delay size if changed
            if (_lastPreDelay != settings.ReverbPreDelay)
            {
                _preDelaySize = Math.Max(1, (int)(settings.ReverbPreDelay / 1000.0 * _sampleRate));
                _lastPreDelay = settings.ReverbPreDelay;
            }

            // Update reverb if room size changed (reinitialize comb/allpass filter buffers)
            if (_lastRoomSize != settings.ReverbRoomSize)
            {
                InitializeReverb(_sampleRate, settings.ReverbRoomSize);
                _lastRoomSize = settings.ReverbRoomSize;
            }

            // Check if any effects are enabled
            bool anyEffectEnabled = settings.HighPassEnabled || settings.LowPassEnabled
                                    || settings.ReverbEnabled || slowActive;
            bool hasMakeupGain = settings.MakeupGainEnabled && settings.MakeupGain != 0;

            if (!anyEffectEnabled && !hasMakeupGain)
            {
                return samplesRead;
            }

            // Process based on channel count
            if (_channels == 1)
            {
                ProcessMono(buffer, offset, samplesRead, settings);
            }
            else
            {
                ProcessStereo(buffer, offset, samplesRead, settings);
            }

            return samplesRead;
        }

        private void ProcessStereo(float[] buffer, int offset, int count, UniPlaySongSettings settings)
        {
            // Calculate libSoX/Audacity reverb parameters (only used if reverb is enabled)
            float feedback = 0, hfDamping = 0, wetGain = 0, dryGain = 0, stereoWidth = 0;
            if (settings.ReverbEnabled)
            {
                // Audacity uses Reverberance (not RoomSize) for feedback/tail length
                // libSoX formula for feedback from reverberance percentage
                double a = -1.0 / Math.Log(1.0 - 0.3);
                double b = 100.0 / (Math.Log(1.0 - 0.98) * a + 1.0);
                feedback = (float)(1.0 - Math.Exp((settings.ReverbReverberance - b) / (a * b)));

                // HF damping from settings - use advanced tuning if enabled
                float hfDampMin = settings.AdvancedReverbTuningEnabled
                    ? settings.ReverbHfDampingMin / 100f
                    : DefaultHfDampingMin;
                float hfDampMax = settings.AdvancedReverbTuningEnabled
                    ? settings.ReverbHfDampingMax / 100f
                    : DefaultHfDampingMax;
                hfDamping = settings.ReverbDamping / 100f * (hfDampMax - hfDampMin) + hfDampMin;

                // Wet/dry gains - use advanced wet gain multiplier if enabled
                float wetMultiplier = settings.AdvancedReverbTuningEnabled
                    ? settings.ReverbWetGainMultiplier / 100f
                    : DefaultWetGainMultiplier;
                wetGain = DbToLinear(settings.ReverbWetGain) * wetMultiplier;
                dryGain = DbToLinear(settings.ReverbDryGain);
                stereoWidth = settings.ReverbStereoWidth / 100f;

                // Update tone filters if ToneLow/ToneHigh changed
                UpdateToneFiltersIfNeeded(settings);
            }

            float makeupGain = settings.MakeupGainEnabled ? DbToLinear(settings.MakeupGain) : 1.0f;

            // Get effect chain preset from settings
            var chainPreset = settings.EffectChainPreset;

            for (int i = 0; i < count; i += 2)
            {
                float left = buffer[offset + i];
                float right = (i + 1 < count) ? buffer[offset + i + 1] : left;

                // Apply effects based on preset (hardcoded order for safety)
                switch (chainPreset)
                {
                    case EffectChainPreset.Standard:
                        // High-Pass → Low-Pass → Reverb (default/recommended)
                        ApplyHighPassStereo(ref left, ref right, settings);
                        ApplyLowPassStereo(ref left, ref right, settings);
                        ApplyReverbStereo(ref left, ref right, settings, feedback, hfDamping, wetGain, dryGain, stereoWidth);
                        break;

                    case EffectChainPreset.ReverbFirst:
                        // Reverb → High-Pass → Low-Pass
                        ApplyReverbStereo(ref left, ref right, settings, feedback, hfDamping, wetGain, dryGain, stereoWidth);
                        ApplyHighPassStereo(ref left, ref right, settings);
                        ApplyLowPassStereo(ref left, ref right, settings);
                        break;

                    case EffectChainPreset.LowPassFirst:
                        // Low-Pass → High-Pass → Reverb
                        ApplyLowPassStereo(ref left, ref right, settings);
                        ApplyHighPassStereo(ref left, ref right, settings);
                        ApplyReverbStereo(ref left, ref right, settings, feedback, hfDamping, wetGain, dryGain, stereoWidth);
                        break;

                    case EffectChainPreset.LowPassThenReverb:
                        // Low-Pass → Reverb → High-Pass
                        ApplyLowPassStereo(ref left, ref right, settings);
                        ApplyReverbStereo(ref left, ref right, settings, feedback, hfDamping, wetGain, dryGain, stereoWidth);
                        ApplyHighPassStereo(ref left, ref right, settings);
                        break;

                    case EffectChainPreset.HighPassThenReverb:
                        // High-Pass → Reverb → Low-Pass
                        ApplyHighPassStereo(ref left, ref right, settings);
                        ApplyReverbStereo(ref left, ref right, settings, feedback, hfDamping, wetGain, dryGain, stereoWidth);
                        ApplyLowPassStereo(ref left, ref right, settings);
                        break;

                    case EffectChainPreset.ReverbThenLowPass:
                        // Reverb → Low-Pass → High-Pass
                        ApplyReverbStereo(ref left, ref right, settings, feedback, hfDamping, wetGain, dryGain, stereoWidth);
                        ApplyLowPassStereo(ref left, ref right, settings);
                        ApplyHighPassStereo(ref left, ref right, settings);
                        break;

                    default:
                        // Fallback to standard
                        ApplyHighPassStereo(ref left, ref right, settings);
                        ApplyLowPassStereo(ref left, ref right, settings);
                        ApplyReverbStereo(ref left, ref right, settings, feedback, hfDamping, wetGain, dryGain, stereoWidth);
                        break;
                }

                // Makeup gain (always applied after effect chain if enabled)
                if (settings.MakeupGainEnabled)
                {
                    left *= makeupGain;
                    right *= makeupGain;
                }

                // Soft-knee limiter to prevent clipping (always last)
                left = SoftKneeLimiter(left);
                right = SoftKneeLimiter(right);

                buffer[offset + i] = left;
                if (i + 1 < count)
                    buffer[offset + i + 1] = right;
            }
        }

        private void ProcessMono(float[] buffer, int offset, int count, UniPlaySongSettings settings)
        {
            // Calculate libSoX/Audacity reverb parameters (only used if reverb is enabled)
            float feedback = 0, hfDamping = 0, wetGain = 0, dryGain = 0;
            if (settings.ReverbEnabled)
            {
                // Audacity uses Reverberance (not RoomSize) for feedback/tail length
                double a = -1.0 / Math.Log(1.0 - 0.3);
                double b = 100.0 / (Math.Log(1.0 - 0.98) * a + 1.0);
                feedback = (float)(1.0 - Math.Exp((settings.ReverbReverberance - b) / (a * b)));

                // HF damping from settings - use advanced tuning if enabled
                float hfDampMin = settings.AdvancedReverbTuningEnabled
                    ? settings.ReverbHfDampingMin / 100f
                    : DefaultHfDampingMin;
                float hfDampMax = settings.AdvancedReverbTuningEnabled
                    ? settings.ReverbHfDampingMax / 100f
                    : DefaultHfDampingMax;
                hfDamping = settings.ReverbDamping / 100f * (hfDampMax - hfDampMin) + hfDampMin;

                // Wet/dry gains - use advanced wet gain multiplier if enabled
                float wetMultiplier = settings.AdvancedReverbTuningEnabled
                    ? settings.ReverbWetGainMultiplier / 100f
                    : DefaultWetGainMultiplier;
                wetGain = DbToLinear(settings.ReverbWetGain) * wetMultiplier;
                dryGain = DbToLinear(settings.ReverbDryGain);

                // Update tone filters if ToneLow/ToneHigh changed
                UpdateToneFiltersIfNeeded(settings);
            }

            float makeupGain = settings.MakeupGainEnabled ? DbToLinear(settings.MakeupGain) : 1.0f;

            // Get effect chain preset from settings
            var chainPreset = settings.EffectChainPreset;

            for (int i = 0; i < count; i++)
            {
                float sample = buffer[offset + i];

                // Apply effects based on preset (hardcoded order for safety)
                switch (chainPreset)
                {
                    case EffectChainPreset.Standard:
                        ApplyHighPassMono(ref sample, settings);
                        ApplyLowPassMono(ref sample, settings);
                        ApplyReverbMono(ref sample, settings, feedback, hfDamping, wetGain, dryGain);
                        break;

                    case EffectChainPreset.ReverbFirst:
                        ApplyReverbMono(ref sample, settings, feedback, hfDamping, wetGain, dryGain);
                        ApplyHighPassMono(ref sample, settings);
                        ApplyLowPassMono(ref sample, settings);
                        break;

                    case EffectChainPreset.LowPassFirst:
                        ApplyLowPassMono(ref sample, settings);
                        ApplyHighPassMono(ref sample, settings);
                        ApplyReverbMono(ref sample, settings, feedback, hfDamping, wetGain, dryGain);
                        break;

                    case EffectChainPreset.LowPassThenReverb:
                        ApplyLowPassMono(ref sample, settings);
                        ApplyReverbMono(ref sample, settings, feedback, hfDamping, wetGain, dryGain);
                        ApplyHighPassMono(ref sample, settings);
                        break;

                    case EffectChainPreset.HighPassThenReverb:
                        ApplyHighPassMono(ref sample, settings);
                        ApplyReverbMono(ref sample, settings, feedback, hfDamping, wetGain, dryGain);
                        ApplyLowPassMono(ref sample, settings);
                        break;

                    case EffectChainPreset.ReverbThenLowPass:
                        ApplyReverbMono(ref sample, settings, feedback, hfDamping, wetGain, dryGain);
                        ApplyLowPassMono(ref sample, settings);
                        ApplyHighPassMono(ref sample, settings);
                        break;

                    default:
                        ApplyHighPassMono(ref sample, settings);
                        ApplyLowPassMono(ref sample, settings);
                        ApplyReverbMono(ref sample, settings, feedback, hfDamping, wetGain, dryGain);
                        break;
                }

                // Makeup gain (always applied after effect chain if enabled)
                if (settings.MakeupGainEnabled)
                {
                    sample *= makeupGain;
                }

                // Soft-knee limiter to prevent clipping (always last)
                buffer[offset + i] = SoftKneeLimiter(sample);
            }
        }

        // ===== Helper methods for effect application =====

        private void ApplyHighPassStereo(ref float left, ref float right, UniPlaySongSettings settings)
        {
            if (settings.HighPassEnabled)
            {
                left = _highPassL.Transform(left);
                right = _highPassR.Transform(right);
            }
        }

        private void ApplyLowPassStereo(ref float left, ref float right, UniPlaySongSettings settings)
        {
            if (settings.LowPassEnabled)
            {
                left = _lowPassL.Transform(left);
                right = _lowPassR.Transform(right);
            }
        }

        private void ApplyReverbStereo(ref float left, ref float right, UniPlaySongSettings settings,
            float feedback, float hfDamping, float wetGain, float dryGain, float stereoWidth)
        {
            if (settings.ReverbEnabled)
            {
                float dryL = left;
                float dryR = right;

                // Apply pre-delay
                float delayedL = ProcessPreDelay(left, _preDelayBufferL, _preDelaySize);
                float delayedR = ProcessPreDelay(right, _preDelayBufferR, _preDelaySize);

                // Advance pre-delay index (once per stereo sample pair)
                _preDelayIndex = (_preDelayIndex + 1) % _maxPreDelaySize;

                // Process left channel through reverb
                float wetL = ProcessReverbChannel(
                    delayedL,
                    _combBuffersL, _combIndexL, _combSizeL, _combStoreL,
                    _allpassBuffersL, _allpassIndexL, _allpassSizeL,
                    feedback, hfDamping);

                // Process right channel through reverb
                float wetR = ProcessReverbChannel(
                    delayedR,
                    _combBuffersR, _combIndexR, _combSizeR, _combStoreR,
                    _allpassBuffersR, _allpassIndexR, _allpassSizeR,
                    feedback, hfDamping);

                // Apply tone filters (post-reverb EQ)
                wetL = _toneHpL.Process(wetL);
                wetL = _toneLpL.Process(wetL);
                wetR = _toneHpR.Process(wetR);
                wetR = _toneLpR.Process(wetR);

                // Apply stereo width (crossfade between stereo and mono)
                if (stereoWidth < 1.0f)
                {
                    float mono = (wetL + wetR) * 0.5f;
                    wetL = mono + (wetL - mono) * stereoWidth;
                    wetR = mono + (wetR - mono) * stereoWidth;
                }

                // Mix wet and dry
                left = dryL * dryGain + wetL * wetGain;
                right = dryR * dryGain + wetR * wetGain;
            }
        }

        private void ApplyHighPassMono(ref float sample, UniPlaySongSettings settings)
        {
            if (settings.HighPassEnabled)
            {
                sample = _highPassL.Transform(sample);
            }
        }

        private void ApplyLowPassMono(ref float sample, UniPlaySongSettings settings)
        {
            if (settings.LowPassEnabled)
            {
                sample = _lowPassL.Transform(sample);
            }
        }

        private void ApplyReverbMono(ref float sample, UniPlaySongSettings settings,
            float feedback, float hfDamping, float wetGain, float dryGain)
        {
            if (settings.ReverbEnabled)
            {
                float dry = sample;

                // Apply pre-delay
                float delayed = ProcessPreDelay(sample, _preDelayBufferL, _preDelaySize);
                _preDelayIndex = (_preDelayIndex + 1) % _maxPreDelaySize;

                float wet = ProcessReverbChannel(
                    delayed,
                    _combBuffersL, _combIndexL, _combSizeL, _combStoreL,
                    _allpassBuffersL, _allpassIndexL, _allpassSizeL,
                    feedback, hfDamping);

                // Apply tone filters
                wet = _toneHpL.Process(wet);
                wet = _toneLpL.Process(wet);

                sample = dry * dryGain + wet * wetGain;
            }
        }

        /// <summary>
        /// Process a single sample through the reverb algorithm (libSoX style).
        /// </summary>
        private static float ProcessReverbChannel(
            float input,
            float[][] combBuffers, int[] combIndex, int[] combSize, float[] combStore,
            float[][] allpassBuffers, int[] allpassIndex, int[] allpassSize,
            float feedback, float hfDamping)
        {
            float output = 0;

            // Process 8 parallel comb filters
            for (int c = NumCombs - 1; c >= 0; c--)
            {
                float bufOut = combBuffers[c][combIndex[c]];
                output += bufOut;

                // Lowpass in feedback path: store = output + (store - output) * damping
                combStore[c] = bufOut + (combStore[c] - bufOut) * hfDamping;
                combBuffers[c][combIndex[c]] = input + combStore[c] * feedback;

                // Advance index with wrap
                if (--combIndex[c] < 0)
                    combIndex[c] = combSize[c] - 1;
            }

            // Process 4 series all-pass filters
            for (int a = NumAllpasses - 1; a >= 0; a--)
            {
                float bufOut = allpassBuffers[a][allpassIndex[a]];
                allpassBuffers[a][allpassIndex[a]] = output + bufOut * 0.5f;
                output = bufOut - output;  // libSoX: return output - input

                // Advance index with wrap
                if (--allpassIndex[a] < 0)
                    allpassIndex[a] = allpassSize[a] - 1;
            }

            return output;
        }

        private void UpdateFilters(int sampleRate, UniPlaySongSettings settings)
        {
            _highPassL = BiQuadFilter.HighPassFilter(sampleRate, settings.HighPassCutoff, 1f);
            _highPassR = BiQuadFilter.HighPassFilter(sampleRate, settings.HighPassCutoff, 1f);
            _lowPassL = BiQuadFilter.LowPassFilter(sampleRate, settings.LowPassCutoff, 1f);
            _lowPassR = BiQuadFilter.LowPassFilter(sampleRate, settings.LowPassCutoff, 1f);

            _lastHighPassCutoff = settings.HighPassCutoff;
            _lastLowPassCutoff = settings.LowPassCutoff;
        }

        /// <summary>
        /// Convert dB to linear gain
        /// </summary>
        private static float DbToLinear(int db)
        {
            return (float)Math.Pow(10, db / 20.0);
        }

        /// <summary>
        /// Soft-knee limiter to prevent harsh clipping.
        /// </summary>
        private static float SoftKneeLimiter(float sample)
        {
            float absValue = Math.Abs(sample);

            if (absValue <= LimiterThreshold)
            {
                return sample;
            }

            float sign = sample >= 0 ? 1f : -1f;

            // Soft saturation curve
            float overshoot = absValue - LimiterThreshold;
            float compressed = LimiterThreshold + overshoot / (1f + overshoot);

            // Hard ceiling at 1.0
            if (compressed > 1f)
                compressed = 1f;

            return sign * compressed;
        }

        /// <summary>
        /// Simple one-pole filter for post-reverb tone control (from libSoX).
        /// </summary>
        private class OnePoleFilter
        {
            private double _b0, _b1, _a1;
            private double _i1, _o1;

            public OnePoleFilter(double sampleRate, double fc, bool isHighpass)
            {
                _a1 = -Math.Exp(-2 * Math.PI * fc / sampleRate);
                if (isHighpass)
                {
                    // Highpass: b0 = (1-a1)/2, b1 = -b0
                    _b0 = (1 - _a1) / 2;
                    _b1 = -_b0;
                }
                else
                {
                    // Lowpass: b0 = 1+a1, b1 = 0
                    _b0 = 1 + _a1;
                    _b1 = 0;
                }
                _i1 = 0;
                _o1 = 0;
            }

            public float Process(float input)
            {
                double output = input * _b0 + _i1 * _b1 - _o1 * _a1;
                _i1 = input;
                _o1 = output;
                return (float)output;
            }
        }
    }
}
