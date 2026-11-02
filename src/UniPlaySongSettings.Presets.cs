using System.Windows.Media;
using UniPlaySong.Models;

namespace UniPlaySong
{
    // The preset tables: icon glow, reverb (Audacity factory values) and style presets. Split out of
    // UniPlaySongSettings.cs to keep that file to the settings themselves; same class, so nothing changes.
    public partial class UniPlaySongSettings
    {
        private void ApplyIconGlowPreset(IconGlowPreset preset)
        {
            switch (preset)
            {
                case IconGlowPreset.Pulse:
                    IconGlowIntensity = 2.5;
                    IconGlowSize = 5.0;
                    IconGlowPulseSpeed = 1.0;
                    IconGlowAudioSensitivity = 2.8;
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = true;
                    IconGlowSpinSpeed = 35.0; // slow drift
                    break;
                case IconGlowPreset.Ambient:
                    IconGlowIntensity = 1.4;
                    IconGlowSize = 9.0;
                    IconGlowPulseSpeed = 4.0; // very slow breathing
                    IconGlowAudioSensitivity = 1.0;
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = true;
                    IconGlowSpinSpeed = 30.0; // barely drifting
                    break;
                case IconGlowPreset.Neon:
                    IconGlowIntensity = 2.5;
                    IconGlowSize = 6.0;
                    IconGlowPulseSpeed = 1.2;
                    IconGlowAudioSensitivity = 2.8;
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = true;
                    IconGlowSpinSpeed = 10.0; // fast spin, electric energy
                    break;
                case IconGlowPreset.Subtle:
                    IconGlowIntensity = 0.6;
                    IconGlowSize = 3.0;
                    IconGlowPulseSpeed = 3.5;
                    IconGlowAudioSensitivity = 0.8;
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = true;
                    IconGlowSpinSpeed = 30.0; // barely perceptible drift
                    break;
                case IconGlowPreset.SharpStatic:
                    IconGlowIntensity = 0.8;
                    IconGlowSize = 2.0;
                    IconGlowPulseSpeed = 2.5;
                    IconGlowAudioSensitivity = 0.8;
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = false;
                    IconGlowSpinSpeed = 20.0;
                    break;
                case IconGlowPreset.SharpBright:
                    IconGlowIntensity = 2.5;
                    IconGlowSize = 3.0;
                    IconGlowPulseSpeed = 1.5; // noticeable breathing
                    IconGlowAudioSensitivity = 0.8;
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = false;
                    IconGlowSpinSpeed = 20.0;
                    break;
                case IconGlowPreset.SharpVivid:
                    IconGlowIntensity = 3.0;
                    IconGlowSize = 3.5;
                    IconGlowPulseSpeed = 0.8; // fast, dramatic flash
                    IconGlowAudioSensitivity = 0.8;
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = false;
                    IconGlowSpinSpeed = 20.0;
                    break;
                case IconGlowPreset.SharpWarm:
                    IconGlowIntensity = 1.5;
                    IconGlowSize = 2.5;
                    IconGlowPulseSpeed = 2.0; // moderate breathing to show off colors
                    IconGlowAudioSensitivity = 0.8;
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = false;
                    IconGlowSpinSpeed = 20.0;
                    break;
                case IconGlowPreset.SharpCool:
                    IconGlowIntensity = 1.5;
                    IconGlowSize = 2.5;
                    IconGlowPulseSpeed = 2.0;
                    IconGlowAudioSensitivity = 0.8;
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = false;
                    IconGlowSpinSpeed = 20.0;
                    break;
                case IconGlowPreset.SharpPulse:
                    IconGlowIntensity = 2.0;
                    IconGlowSize = 2.5;
                    IconGlowPulseSpeed = 0.8; // fast heartbeat rhythm
                    IconGlowAudioSensitivity = 0.8;
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = false;
                    IconGlowSpinSpeed = 20.0;
                    break;
                case IconGlowPreset.BassPunch:
                    IconGlowIntensity = 2.2;
                    IconGlowSize = 5.0;
                    IconGlowPulseSpeed = 1.0; // sine fallback speed
                    IconGlowAudioSensitivity = 3.0;
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = true;
                    IconGlowSpinSpeed = 20.0;
                    break;
                case IconGlowPreset.Mellow:
                    IconGlowIntensity = 1.6;
                    IconGlowSize = 8.0;
                    IconGlowPulseSpeed = 3.5; // slow sine fallback
                    IconGlowAudioSensitivity = 2.0; // more sensitive since smooth3-only dampens heavily
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = true;
                    IconGlowSpinSpeed = 28.0; // very slow drift
                    break;
                case IconGlowPreset.Reactive:
                    IconGlowIntensity = 2.5;
                    IconGlowSize = 6.0;
                    IconGlowPulseSpeed = 1.5; // sine fallback speed
                    IconGlowAudioSensitivity = 3.5;
                    EnableIconGlowPulse = true;
                    EnableIconGlowSpin = true;
                    IconGlowSpinSpeed = 15.0;
                    break;
                // Custom: no-op, user controls sliders
            }
        }

        // Applies a reverb preset by setting all reverb parameters. Presets are based on Audacity's exact
        // factory preset values. Format: RoomSize, PreDelay, Reverberance, HfDamping, ToneLow, ToneHigh,
        // WetGain, DryGain, StereoWidth
        public void ApplyReverbPreset(ReverbPreset preset)
        {
            switch (preset)
            {
                // === General Purpose ===
                case ReverbPreset.Acoustic:
                    // Acoustic - natural acoustic sound
                    ReverbRoomSize = 50;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 75;
                    ReverbDamping = 100;
                    ReverbToneLow = 21;
                    ReverbToneHigh = 100;
                    ReverbWetGain = -14;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 80;
                    break;

                case ReverbPreset.Ambience:
                    // Ambience - spacious atmospheric effect (very pronounced)
                    ReverbRoomSize = 100;
                    ReverbPreDelay = 55;
                    ReverbReverberance = 100;
                    ReverbDamping = 50;
                    ReverbToneLow = 53;
                    ReverbToneHigh = 38;
                    ReverbWetGain = 0;
                    ReverbDryGain = -10;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.Artificial:
                    // Artificial - synthetic/electronic reverb character
                    ReverbRoomSize = 81;
                    ReverbPreDelay = 99;
                    ReverbReverberance = 23;
                    ReverbDamping = 62;
                    ReverbToneLow = 16;
                    ReverbToneHigh = 19;
                    ReverbWetGain = -4;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.Clean:
                    // Clean - subtle, transparent reverb
                    ReverbRoomSize = 50;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 75;
                    ReverbDamping = 100;
                    ReverbToneLow = 55;
                    ReverbToneHigh = 100;
                    ReverbWetGain = -18;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 75;
                    break;

                case ReverbPreset.Modern:
                    // Modern - contemporary production style
                    ReverbRoomSize = 50;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 75;
                    ReverbDamping = 100;
                    ReverbToneLow = 55;
                    ReverbToneHigh = 100;
                    ReverbWetGain = -15;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 75;
                    break;

                // === Vocals ===
                case ReverbPreset.VocalI:
                    // Vocal I - subtle vocal reverb
                    ReverbRoomSize = 70;
                    ReverbPreDelay = 20;
                    ReverbReverberance = 40;
                    ReverbDamping = 99;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 50;
                    ReverbWetGain = -12;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 70;
                    break;

                case ReverbPreset.VocalII:
                    // Vocal II - more pronounced vocal reverb
                    ReverbRoomSize = 50;
                    ReverbPreDelay = 0;
                    ReverbReverberance = 50;
                    ReverbDamping = 99;
                    ReverbToneLow = 50;
                    ReverbToneHigh = 100;
                    ReverbWetGain = -1;
                    ReverbDryGain = -1;
                    ReverbStereoWidth = 70;
                    break;

                case ReverbPreset.DanceVocal:
                    // Dance Vocal - modern dance/EDM vocal effect
                    ReverbRoomSize = 90;
                    ReverbPreDelay = 2;
                    ReverbReverberance = 60;
                    ReverbDamping = 77;
                    ReverbToneLow = 30;
                    ReverbToneHigh = 51;
                    ReverbWetGain = -10;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.ModernVocal:
                    // Modern Vocal - contemporary vocal production
                    ReverbRoomSize = 66;
                    ReverbPreDelay = 27;
                    ReverbReverberance = 77;
                    ReverbDamping = 8;
                    ReverbToneLow = 0;
                    ReverbToneHigh = 51;
                    ReverbWetGain = -10;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 68;
                    break;

                case ReverbPreset.VoiceTail:
                    // Voice Tail - long reverb tail for vocals
                    ReverbRoomSize = 66;
                    ReverbPreDelay = 27;
                    ReverbReverberance = 100;
                    ReverbDamping = 8;
                    ReverbToneLow = 0;
                    ReverbToneHigh = 51;
                    ReverbWetGain = -6;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 68;
                    break;

                // === Room Sizes ===
                case ReverbPreset.Bathroom:
                    // Bathroom - small reflective space
                    ReverbRoomSize = 16;
                    ReverbPreDelay = 8;
                    ReverbReverberance = 80;
                    ReverbDamping = 0;
                    ReverbToneLow = 0;
                    ReverbToneHigh = 100;
                    ReverbWetGain = -6;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.SmallRoomBright:
                    // Small Room Bright - bright reflections
                    ReverbRoomSize = 30;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 50;
                    ReverbDamping = 50;
                    ReverbToneLow = 50;
                    ReverbToneHigh = 100;
                    ReverbWetGain = -1;
                    ReverbDryGain = -1;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.SmallRoomDark:
                    // Small Room Dark - muffled reflections
                    ReverbRoomSize = 30;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 50;
                    ReverbDamping = 50;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 0;
                    ReverbWetGain = -1;
                    ReverbDryGain = -1;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.MediumRoom:
                    // Medium Room - balanced room sound
                    ReverbRoomSize = 75;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 40;
                    ReverbDamping = 50;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 70;
                    ReverbWetGain = -1;
                    ReverbDryGain = -1;
                    ReverbStereoWidth = 70;
                    break;

                case ReverbPreset.LargeRoom:
                    // Large Room - spacious room
                    ReverbRoomSize = 85;
                    ReverbPreDelay = 10;
                    ReverbReverberance = 40;
                    ReverbDamping = 50;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 80;
                    ReverbWetGain = 0;
                    ReverbDryGain = -6;
                    ReverbStereoWidth = 90;
                    break;

                case ReverbPreset.ChurchHall:
                    // Church Hall - large reverberant space
                    ReverbRoomSize = 90;
                    ReverbPreDelay = 32;
                    ReverbReverberance = 60;
                    ReverbDamping = 50;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 50;
                    ReverbWetGain = 0;
                    ReverbDryGain = -12;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.Cathedral:
                    // Cathedral - massive cathedral space
                    ReverbRoomSize = 90;
                    ReverbPreDelay = 16;
                    ReverbReverberance = 90;
                    ReverbDamping = 50;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 0;
                    ReverbWetGain = 0;
                    ReverbDryGain = -20;
                    ReverbStereoWidth = 100;
                    break;

                case ReverbPreset.BigCave:
                    // Big Cave - cavernous space (very pronounced)
                    ReverbRoomSize = 100;
                    ReverbPreDelay = 55;
                    ReverbReverberance = 100;
                    ReverbDamping = 50;
                    ReverbToneLow = 53;
                    ReverbToneHigh = 38;
                    ReverbWetGain = 5;
                    ReverbDryGain = -3;
                    ReverbStereoWidth = 100;
                    break;

                // ===== UniPlaySong Custom Presets - Living/Entertainment =====

                case ReverbPreset.LivingRoom:
                    // Living Room - cozy TV viewing environment
                    // Subtle, warm reverb that doesn't overwhelm dialogue
                    ReverbRoomSize = 35;
                    ReverbPreDelay = 8;
                    ReverbReverberance = 30;
                    ReverbDamping = 65;
                    ReverbToneLow = 70;
                    ReverbToneHigh = 60;
                    ReverbWetGain = -12;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 60;
                    break;

                case ReverbPreset.HomeTheater:
                    // Home Theater - movie night experience
                    // Wider soundstage with controlled tail for cinematic feel
                    ReverbRoomSize = 55;
                    ReverbPreDelay = 15;
                    ReverbReverberance = 45;
                    ReverbDamping = 55;
                    ReverbToneLow = 80;
                    ReverbToneHigh = 70;
                    ReverbWetGain = -8;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 85;
                    break;

                case ReverbPreset.LateNightTV:
                    // Late Night TV - subtle, intimate ambience
                    // Very restrained reverb, perfect for quiet viewing
                    ReverbRoomSize = 25;
                    ReverbPreDelay = 5;
                    ReverbReverberance = 20;
                    ReverbDamping = 75;
                    ReverbToneLow = 60;
                    ReverbToneHigh = 40;
                    ReverbWetGain = -15;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 50;
                    break;

                case ReverbPreset.LoungeCafe:
                    // Lounge/Cafe - relaxed background music atmosphere
                    // Warm, smooth reverb like a cozy cafe
                    ReverbRoomSize = 40;
                    ReverbPreDelay = 12;
                    ReverbReverberance = 35;
                    ReverbDamping = 60;
                    ReverbToneLow = 85;
                    ReverbToneHigh = 55;
                    ReverbWetGain = -10;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 70;
                    break;

                case ReverbPreset.JazzClub:
                    // Jazz Club - intimate live performance feel
                    // Tight, controlled reverb with warmth
                    ReverbRoomSize = 45;
                    ReverbPreDelay = 18;
                    ReverbReverberance = 40;
                    ReverbDamping = 45;
                    ReverbToneLow = 90;
                    ReverbToneHigh = 65;
                    ReverbWetGain = -6;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 75;
                    break;

                case ReverbPreset.NightClub:
                    // Night Club - energetic dance floor
                    // Punchy with controlled low-end, wider stereo
                    ReverbRoomSize = 60;
                    ReverbPreDelay = 25;
                    ReverbReverberance = 55;
                    ReverbDamping = 40;
                    ReverbToneLow = 70;
                    ReverbToneHigh = 80;
                    ReverbWetGain = -4;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 95;
                    break;

                case ReverbPreset.ConcertHall:
                    // Concert Hall - live music experience
                    // Large, natural acoustic space
                    ReverbRoomSize = 80;
                    ReverbPreDelay = 22;
                    ReverbReverberance = 65;
                    ReverbDamping = 35;
                    ReverbToneLow = 95;
                    ReverbToneHigh = 75;
                    ReverbWetGain = -2;
                    ReverbDryGain = -3;
                    ReverbStereoWidth = 100;
                    break;

                // ===== UniPlaySong Custom Presets - Creative/Stylistic =====

                case ReverbPreset.Dreamy:
                    // Dreamy - ethereal, lush wash for ambient/chill vibes
                    // Rich reverb engine, mix controls how much washes over the dry signal
                    ReverbRoomSize = 85;
                    ReverbPreDelay = 30;
                    ReverbReverberance = 75;
                    ReverbDamping = 60;
                    ReverbToneLow = 85;
                    ReverbToneHigh = 35;
                    ReverbWetGain = 0;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 100;
                    ReverbMix = 30;
                    break;

                case ReverbPreset.LoFi:
                    // Lo-Fi - muffled, tape-like warmth
                    // Dark, damped reverb blended in subtly for texture
                    ReverbRoomSize = 45;
                    ReverbPreDelay = 8;
                    ReverbReverberance = 40;
                    ReverbDamping = 85;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 10;
                    ReverbWetGain = 0;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 45;
                    ReverbMix = 25;
                    break;

                case ReverbPreset.Underwater:
                    // Underwater - deep, submerged, surreal atmosphere
                    // Full reverb character with heavy damping, mix pushes you under
                    ReverbRoomSize = 90;
                    ReverbPreDelay = 40;
                    ReverbReverberance = 75;
                    ReverbDamping = 95;
                    ReverbToneLow = 100;
                    ReverbToneHigh = 5;
                    ReverbWetGain = 2;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 80;
                    ReverbMix = 45;
                    break;

                case ReverbPreset.SciFiCorridor:
                    // Sci-Fi Corridor - metallic, spacious, futuristic echo
                    // Bright, undamped reflections with long pre-delay for distinct echoes
                    ReverbRoomSize = 70;
                    ReverbPreDelay = 80;
                    ReverbReverberance = 50;
                    ReverbDamping = 10;
                    ReverbToneLow = 25;
                    ReverbToneHigh = 95;
                    ReverbWetGain = 0;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 95;
                    ReverbMix = 30;
                    break;

                case ReverbPreset.VinylWarmth:
                    // Vinyl Warmth - subtle warm coloring, like vinyl playback
                    // Small, dark room character, low mix for gentle texture
                    ReverbRoomSize = 30;
                    ReverbPreDelay = 5;
                    ReverbReverberance = 30;
                    ReverbDamping = 75;
                    ReverbToneLow = 95;
                    ReverbToneHigh = 20;
                    ReverbWetGain = 0;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 40;
                    ReverbMix = 20;
                    break;

                case ReverbPreset.Vaporwave:
                    // Vaporwave - the classic slowed+reverb aesthetic
                    // Big lush room cranked up, mix brings it to a dreamy blend
                    ReverbRoomSize = 90;
                    ReverbPreDelay = 25;
                    ReverbReverberance = 60;
                    ReverbDamping = 45;
                    ReverbToneLow = 80;
                    ReverbToneHigh = 40;
                    ReverbWetGain = 2;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 100;
                    ReverbMix = 35;
                    break;

                case ReverbPreset.Custom:
                default:
                    // Custom - don't change anything
                    break;
            }
        }

        // When the user manually changes any individual effect parameter, clear the style preset back to None
        // so it doesn't re-apply on settings reload. Suppressed during ApplyStylePreset so the preset's own
        // property changes don't trigger clearing.
        private void ClearStylePresetIfActive()
        {
            if (!_applyingStylePreset && selectedStylePreset != StylePreset.None)
            {
                selectedStylePreset = StylePreset.None;
                OnPropertyChanged(nameof(SelectedStylePreset));
            }
        }

        public void ApplyStylePreset(StylePreset style)
        {
            _applyingStylePreset = true;
            try
            {
            switch (style)
            {
                case StylePreset.HuddiniRehearsal:
                    // Wide stereo, rich reverb, live rehearsal room feel
                    HighPassEnabled = true;
                    HighPassCutoff = 145;
                    LowPassEnabled = true;
                    LowPassCutoff = 7870;
                    ReverbEnabled = true;
                    ReverbRoomSize = 80;
                    ReverbReverberance = 65;
                    ReverbDamping = 35;
                    ReverbPreDelay = 22;
                    ReverbToneLow = 95;
                    ReverbToneHigh = 75;
                    ReverbWetGain = -1;
                    ReverbDryGain = -3;
                    ReverbStereoWidth = 96;
                    ReverbMix = 50;
                    selectedReverbPreset = ReverbPreset.Custom;
                    OnPropertyChanged(nameof(SelectedReverbPreset));
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 58;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = -1;
                    EffectChainPreset = EffectChainPreset.ReverbFirst;
                    break;

                case StylePreset.HuddiniBrightRoom:
                    // Open, spacious large room with bright tone and wide stereo
                    HighPassEnabled = true;
                    HighPassCutoff = 180;
                    LowPassEnabled = false;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.LargeRoom;
                    ReverbToneHigh = 49;
                    ReverbMix = 40;
                    selectedReverbPreset = ReverbPreset.Custom;
                    OnPropertyChanged(nameof(SelectedReverbPreset));
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 72;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 0;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.HuddiniRetroRadio:
                    // Narrow bandpass, bathroom reverb, bitcrushed AM radio
                    HighPassEnabled = true;
                    HighPassCutoff = 636;
                    LowPassEnabled = true;
                    LowPassCutoff = 3530;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.Bathroom;
                    ReverbWetGain = -3;
                    ReverbMix = 26;
                    selectedReverbPreset = ReverbPreset.Custom;
                    OnPropertyChanged(nameof(SelectedReverbPreset));
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 24;
                    ChorusEnabled = false;
                    BitcrusherEnabled = true;
                    BitcrusherBitDepth = 9;
                    BitcrusherDownsample = 2;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 1;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.HuddiniLoBit:
                    // Dark lo-fi, heavy bitcrusher, aggressive downsample
                    HighPassEnabled = true;
                    HighPassCutoff = 509;
                    LowPassEnabled = true;
                    LowPassCutoff = 5445;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.LoFi;
                    ReverbMix = 35;
                    SlowEnabled = false;
                    StereoWidenerEnabled = false;
                    ChorusEnabled = false;
                    BitcrusherEnabled = true;
                    BitcrusherBitDepth = 12;
                    BitcrusherDownsample = 3;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 1;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.HuddiniSlowedDream:
                    // Deep slow with vaporwave reverb, wide and dreamy
                    HighPassEnabled = false;
                    HighPassCutoff = 200;
                    LowPassEnabled = false;
                    LowPassCutoff = 1270;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.Vaporwave;
                    // Override reverb params from Vaporwave preset
                    ReverbRoomSize = 90;
                    ReverbReverberance = 60;
                    ReverbDamping = 45;
                    ReverbPreDelay = 25;
                    ReverbToneLow = 80;
                    ReverbToneHigh = 40;
                    ReverbWetGain = 2;
                    ReverbDryGain = 0;
                    ReverbStereoWidth = 100;
                    ReverbMix = 35;
                    selectedReverbPreset = ReverbPreset.Custom;
                    OnPropertyChanged(nameof(SelectedReverbPreset));
                    SlowEnabled = true;
                    SlowAmount = 25;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 65;
                    ChorusEnabled = false;
                    ChorusRate = 2;
                    ChorusDepth = 30;
                    ChorusMix = 20;
                    BitcrusherEnabled = false;
                    BitcrusherBitDepth = 14;
                    BitcrusherDownsample = 1;
                    TremoloEnabled = false;
                    TremoloRate = 10;
                    TremoloDepth = 25;
                    MakeupGainEnabled = true;
                    MakeupGain = -2;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.HuddiniCaveLake:
                    // Submerged cave, heavy slow, underwater reverb with chorus drift
                    HighPassEnabled = true;
                    HighPassCutoff = 191;
                    LowPassEnabled = true;
                    LowPassCutoff = 1270;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.Underwater;
                    ReverbMix = 60;
                    SlowEnabled = true;
                    SlowAmount = 40;
                    StereoWidenerEnabled = false;
                    ChorusEnabled = true;
                    ChorusRate = 2;
                    ChorusDepth = 30;
                    ChorusMix = 20;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = -1;
                    EffectChainPreset = EffectChainPreset.LowPassFirst;
                    break;

                case StylePreset.HuddiniHoneyRoom:
                    // Bright sci-fi reverb, subtle bitcrush, wide stereo, slight slow
                    HighPassEnabled = true;
                    HighPassCutoff = 200;
                    LowPassEnabled = false;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.SciFiCorridor;
                    ReverbDamping = 24;
                    ReverbWetGain = 1;
                    ReverbMix = 40;
                    selectedReverbPreset = ReverbPreset.Custom;
                    OnPropertyChanged(nameof(SelectedReverbPreset));
                    SlowEnabled = true;
                    SlowAmount = 4;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 65;
                    ChorusEnabled = false;
                    BitcrusherEnabled = true;
                    BitcrusherBitDepth = 14;
                    BitcrusherDownsample = 1;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 1;
                    EffectChainPreset = EffectChainPreset.ReverbFirst;
                    break;

                case StylePreset.CleanBoost:
                    // Volume boost with stereo widening
                    HighPassEnabled = false;
                    LowPassEnabled = false;
                    ReverbEnabled = false;
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 62;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 1;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.WarmFMRadio:
                    // Cozy radio broadcast with soft top end and lounge cafe warmth
                    HighPassEnabled = false;
                    LowPassEnabled = true;
                    LowPassCutoff = 5500;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.LoungeCafe;
                    ReverbMix = 30;
                    SlowEnabled = false;
                    StereoWidenerEnabled = false;
                    ChorusEnabled = true;
                    ChorusRate = 4;
                    ChorusDepth = 12;
                    ChorusMix = 12;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 1;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.BrightAiry:
                    // Crisp, open, spacious with wide stereo field
                    HighPassEnabled = true;
                    HighPassCutoff = 180;
                    LowPassEnabled = false;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.LargeRoom;
                    ReverbMix = 40;
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 72;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 1;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.Telephone:
                    // Tinny phone speaker with narrow band and collapsed stereo
                    HighPassEnabled = true;
                    HighPassCutoff = 500;
                    LowPassEnabled = true;
                    LowPassCutoff = 3000;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.Bathroom;
                    ReverbMix = 10;
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 10;
                    ChorusEnabled = false;
                    BitcrusherEnabled = true;
                    BitcrusherBitDepth = 10;
                    BitcrusherDownsample = 1;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 1;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.ConcertLive:
                    // Big concert hall with wide stereo and full range
                    HighPassEnabled = true;
                    HighPassCutoff = 80;
                    LowPassEnabled = false;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.ConcertHall;
                    ReverbMix = 45;
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 75;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 1;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.MuffledNextRoom:
                    // Music bleeding through a wall, heavy LP, dark and quiet
                    HighPassEnabled = true;
                    HighPassCutoff = 100;
                    LowPassEnabled = true;
                    LowPassCutoff = 1200;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.SmallRoomDark;
                    ReverbMix = 55;
                    SlowEnabled = false;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 15;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = -5;
                    EffectChainPreset = EffectChainPreset.LowPassFirst;
                    break;

                case StylePreset.LoFiChill:
                    // Lo-fi study beats, dark and warm with bitcrusher texture
                    HighPassEnabled = true;
                    HighPassCutoff = 120;
                    LowPassEnabled = true;
                    LowPassCutoff = 3500;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.LoFi;
                    ReverbMix = 35;
                    SlowEnabled = true;
                    SlowAmount = 6;
                    StereoWidenerEnabled = false;
                    ChorusEnabled = false;
                    BitcrusherEnabled = true;
                    BitcrusherBitDepth = 12;
                    BitcrusherDownsample = 2;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 1;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.SlowedReverb:
                    // Classic vaporwave aesthetic, slowed with lush reverb
                    HighPassEnabled = false;
                    LowPassEnabled = false;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.Vaporwave;
                    ReverbMix = 55;
                    SlowEnabled = true;
                    SlowAmount = 12;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 68;
                    ChorusEnabled = false;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = false;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.VinylNight:
                    // Old record player, warm and gentle with analog pitch drift
                    HighPassEnabled = false;
                    LowPassEnabled = true;
                    LowPassCutoff = 5000;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.VinylWarmth;
                    ReverbMix = 30;
                    SlowEnabled = true;
                    SlowAmount = 5;
                    StereoWidenerEnabled = false;
                    ChorusEnabled = true;
                    ChorusRate = 2;
                    ChorusDepth = 18;
                    ChorusMix = 18;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = false;
                    EffectChainPreset = EffectChainPreset.Standard;
                    break;

                case StylePreset.UnderwaterDream:
                    // Submerged, deep and murky with underwater reverb
                    HighPassEnabled = true;
                    HighPassCutoff = 150;
                    LowPassEnabled = true;
                    LowPassCutoff = 1800;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.Underwater;
                    ReverbMix = 60;
                    SlowEnabled = true;
                    SlowAmount = 10;
                    StereoWidenerEnabled = false;
                    ChorusEnabled = true;
                    ChorusRate = 2;
                    ChorusDepth = 30;
                    ChorusMix = 20;
                    BitcrusherEnabled = false;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = -3;
                    EffectChainPreset = EffectChainPreset.LowPassFirst;
                    break;

                case StylePreset.Cyberpunk:
                    // Neon dystopia with bright metallic reverb and digital edge
                    HighPassEnabled = true;
                    HighPassCutoff = 200;
                    LowPassEnabled = false;
                    ReverbEnabled = true;
                    SelectedReverbPreset = ReverbPreset.SciFiCorridor;
                    ReverbMix = 40;
                    SlowEnabled = true;
                    SlowAmount = 4;
                    StereoWidenerEnabled = true;
                    StereoWidenerWidth = 78;
                    ChorusEnabled = false;
                    BitcrusherEnabled = true;
                    BitcrusherBitDepth = 13;
                    BitcrusherDownsample = 1;
                    TremoloEnabled = false;
                    MakeupGainEnabled = true;
                    MakeupGain = 1;
                    EffectChainPreset = EffectChainPreset.ReverbFirst;
                    break;

                case StylePreset.None:
                default:
                    // None - don't change anything
                    break;
            }
            }
            finally
            {
                _applyingStylePreset = false;
            }
        }
    }
}
