using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace UniPlaySong
{
    public partial class UniPlaySongSettingsView : UserControl
    {
        private readonly UniPlaySong _plugin;

        public UniPlaySongSettingsView(UniPlaySong plugin)
        {
            _plugin = plugin;
            InitializeComponent();
            // DO NOT set DataContext manually - Playnite sets it automatically
            // to the ISettings object returned by GetSettings()
        }

        private void CopyLiveEffectsToClipboard_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as UniPlaySongSettingsViewModel;
            if (vm == null) return;
            var s = vm.Settings;

            var sb = new StringBuilder();
            sb.AppendLine("=== UniPlaySong Live Effects Settings ===");
            sb.AppendLine($"Style Preset: {s.SelectedStylePreset}");
            sb.AppendLine($"Effect Chain Order: {s.EffectChainPreset}");
            sb.AppendLine();
            sb.AppendLine("--- Filters ---");
            sb.AppendLine($"High-Pass: {(s.HighPassEnabled ? "ON" : "OFF")}, Cutoff: {s.HighPassCutoff} Hz");
            sb.AppendLine($"Low-Pass: {(s.LowPassEnabled ? "ON" : "OFF")}, Cutoff: {s.LowPassCutoff} Hz");
            sb.AppendLine();
            sb.AppendLine("--- Reverb ---");
            sb.AppendLine($"Reverb: {(s.ReverbEnabled ? "ON" : "OFF")}");
            sb.AppendLine($"Reverb Preset: {s.SelectedReverbPreset}");
            sb.AppendLine($"Room Size: {s.ReverbRoomSize}, Reverberance: {s.ReverbReverberance}");
            sb.AppendLine($"Damping: {s.ReverbDamping}, Pre-Delay: {s.ReverbPreDelay} ms");
            sb.AppendLine($"Tone Low: {s.ReverbToneLow}, Tone High: {s.ReverbToneHigh}");
            sb.AppendLine($"Wet Gain: {s.ReverbWetGain} dB, Dry Gain: {s.ReverbDryGain} dB");
            sb.AppendLine($"Stereo Width: {s.ReverbStereoWidth}%, Wet/Dry Mix: {s.ReverbMix}%");
            sb.AppendLine();
            sb.AppendLine("--- Slow ---");
            sb.AppendLine($"Slow: {(s.SlowEnabled ? "ON" : "OFF")}, Amount: {s.SlowAmount}%");
            sb.AppendLine();
            sb.AppendLine("--- Stereo Widener ---");
            sb.AppendLine($"Stereo Widener: {(s.StereoWidenerEnabled ? "ON" : "OFF")}, Width: {s.StereoWidenerWidth}%");
            sb.AppendLine();
            sb.AppendLine("--- Chorus ---");
            sb.AppendLine($"Chorus: {(s.ChorusEnabled ? "ON" : "OFF")}");
            sb.AppendLine($"Rate: {s.ChorusRate / 10.0:F1} Hz, Depth: {s.ChorusDepth}%, Mix: {s.ChorusMix}%");
            sb.AppendLine();
            sb.AppendLine("--- Bitcrusher ---");
            sb.AppendLine($"Bitcrusher: {(s.BitcrusherEnabled ? "ON" : "OFF")}");
            sb.AppendLine($"Bit Depth: {s.BitcrusherBitDepth}, Downsample: {s.BitcrusherDownsample}x");
            sb.AppendLine();
            sb.AppendLine("--- Tremolo ---");
            sb.AppendLine($"Tremolo: {(s.TremoloEnabled ? "ON" : "OFF")}");
            sb.AppendLine($"Rate: {s.TremoloRate / 10.0:F1} Hz, Depth: {s.TremoloDepth}%");
            sb.AppendLine();
            sb.AppendLine("--- Makeup Gain ---");
            sb.AppendLine($"Makeup Gain: {(s.MakeupGainEnabled ? "ON" : "OFF")}, Gain: {s.MakeupGain} dB");

            Clipboard.SetText(sb.ToString());

            ShowButtonFeedback(sender, "Copied!");
        }

        private void ResetLiveEffects_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as UniPlaySongSettingsViewModel;
            if (vm == null) return;
            var s = vm.Settings;

            // Reset all live effect parameters to factory defaults
            s.SelectedStylePreset = StylePreset.None;
            s.EffectChainPreset = EffectChainPreset.Standard;

            s.HighPassEnabled = false;
            s.HighPassCutoff = 80;
            s.LowPassEnabled = false;
            s.LowPassCutoff = 8000;

            s.ReverbEnabled = false;
            s.SelectedReverbPreset = ReverbPreset.Custom;
            s.ReverbRoomSize = 75;
            s.ReverbReverberance = 50;
            s.ReverbDamping = 50;
            s.ReverbPreDelay = 10;
            s.ReverbToneLow = 100;
            s.ReverbToneHigh = 100;
            s.ReverbWetGain = -6;
            s.ReverbDryGain = 0;
            s.ReverbStereoWidth = 100;
            s.ReverbMix = 50;

            s.SlowEnabled = false;
            s.SlowAmount = 0;

            s.StereoWidenerEnabled = false;
            s.StereoWidenerWidth = 50;

            s.ChorusEnabled = false;
            s.ChorusRate = 30;
            s.ChorusDepth = 50;
            s.ChorusMix = 40;

            s.BitcrusherEnabled = false;
            s.BitcrusherBitDepth = 8;
            s.BitcrusherDownsample = 1;

            s.TremoloEnabled = false;
            s.TremoloRate = 40;
            s.TremoloDepth = 50;

            s.MakeupGainEnabled = false;
            s.MakeupGain = 0;

            ShowButtonFeedback(sender, "Reset!");
        }

        private void ResetVisualizerDefaults_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as UniPlaySongSettingsViewModel;
            if (vm == null) return;
            var s = vm.Settings;

            ApplyVizPresetValues(s, VizPreset.Default);
            s.SelectedVizPreset = VizPreset.Custom;

            ShowButtonFeedback(sender, "Reset!");
        }

        private void VizPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var vm = DataContext as UniPlaySongSettingsViewModel;
            if (vm == null) return;
            var s = vm.Settings;

            if (s.SelectedVizPreset != VizPreset.Custom)
            {
                ApplyVizPresetValues(s, s.SelectedVizPreset);
            }
        }

        private static void ApplyVizPresetValues(UniPlaySongSettings s, VizPreset preset)
        {
            switch (preset)
            {
                case VizPreset.Default:
                    s.VizOpacityMin = 30;
                    s.VizBarGainBoost = 0;
                    s.VizPeakHoldMs = 80;
                    s.VizGravity = 120;
                    s.VizBassGravityBias = 50;
                    s.VizFftSize = 1024;
                    s.VizBassGain = 100;
                    s.VizTrebleGain = 100;
                    s.VizBleedAmount = 100;
                    s.VizCompression = 60;
                    s.VizSmoothRise = 85;
                    s.VizSmoothFall = 15;
                    s.VizFftRiseLow = 88;
                    s.VizFftRiseHigh = 93;
                    s.VizFftFallLow = 50;
                    s.VizFftFallHigh = 65;
                    s.VizFftTimerMode = false;
                    s.VizColorTheme = (int)VizColorTheme.Classic;
                    s.VizGradientEnabled = true;
                    break;

                case VizPreset.Smooth:
                    // Gentle, flowing bars — smoothness comes from UI EMA + low gravity, not FFT throttling
                    s.VizOpacityMin = 40;
                    s.VizBarGainBoost = -20;
                    s.VizPeakHoldMs = 120;
                    s.VizGravity = 60;
                    s.VizBassGravityBias = 30;
                    s.VizFftSize = 1024;
                    s.VizBassGain = 75;
                    s.VizTrebleGain = 70;
                    s.VizBleedAmount = 160;
                    s.VizCompression = 75;
                    s.VizSmoothRise = 50;
                    s.VizSmoothFall = 8;
                    s.VizFftRiseLow = 85;
                    s.VizFftRiseHigh = 92;
                    s.VizFftFallLow = 45;
                    s.VizFftFallHigh = 60;
                    s.VizColorTheme = (int)VizColorTheme.Ice;
                    s.VizGradientEnabled = true;
                    break;

                case VizPreset.Punchy:
                    // Snappy beats, fast attack — good for hip-hop/EDM
                    s.VizOpacityMin = 20;
                    s.VizBarGainBoost = 0;
                    s.VizPeakHoldMs = 40;
                    s.VizGravity = 160;
                    s.VizBassGravityBias = 70;
                    s.VizFftSize = 1024;
                    s.VizBassGain = 110;
                    s.VizTrebleGain = 90;
                    s.VizBleedAmount = 60;
                    s.VizCompression = 35;
                    s.VizSmoothRise = 95;
                    s.VizSmoothFall = 30;
                    s.VizFftRiseLow = 92;
                    s.VizFftRiseHigh = 95;
                    s.VizFftFallLow = 55;
                    s.VizFftFallHigh = 70;
                    s.VizColorTheme = (int)VizColorTheme.Fire;
                    s.VizGradientEnabled = true;
                    break;

                case VizPreset.Cinematic:
                    // Wide, slow-moving — the "slow" feel comes from UI smoothing + low gravity
                    s.VizOpacityMin = 45;
                    s.VizBarGainBoost = -15;
                    s.VizPeakHoldMs = 150;
                    s.VizGravity = 40;
                    s.VizBassGravityBias = 20;
                    s.VizFftSize = 1024;
                    s.VizBassGain = 65;
                    s.VizTrebleGain = 70;
                    s.VizBleedAmount = 180;
                    s.VizCompression = 85;
                    s.VizSmoothRise = 40;
                    s.VizSmoothFall = 5;
                    s.VizFftRiseLow = 85;
                    s.VizFftRiseHigh = 90;
                    s.VizFftFallLow = 45;
                    s.VizFftFallHigh = 58;
                    s.VizColorTheme = (int)VizColorTheme.Ocean;
                    s.VizGradientEnabled = true;
                    break;

                case VizPreset.Minimal:
                    // Subtle, understated — low gain + high compression tame the output
                    s.VizOpacityMin = 50;
                    s.VizBarGainBoost = -30;
                    s.VizPeakHoldMs = 60;
                    s.VizGravity = 100;
                    s.VizBassGravityBias = 40;
                    s.VizFftSize = 1024;
                    s.VizBassGain = 50;
                    s.VizTrebleGain = 55;
                    s.VizBleedAmount = 120;
                    s.VizCompression = 95;
                    s.VizSmoothRise = 60;
                    s.VizSmoothFall = 10;
                    s.VizFftRiseLow = 85;
                    s.VizFftRiseHigh = 90;
                    s.VizFftFallLow = 45;
                    s.VizFftFallHigh = 58;
                    s.VizColorTheme = (int)VizColorTheme.Classic;
                    s.VizGradientEnabled = true;
                    break;

                case VizPreset.Reactive:
                    // Maximum responsiveness — everything cranked
                    s.VizOpacityMin = 15;
                    s.VizBarGainBoost = 10;
                    s.VizPeakHoldMs = 20;
                    s.VizGravity = 180;
                    s.VizBassGravityBias = 60;
                    s.VizFftSize = 1024;
                    s.VizBassGain = 115;
                    s.VizTrebleGain = 105;
                    s.VizBleedAmount = 40;
                    s.VizCompression = 20;
                    s.VizSmoothRise = 98;
                    s.VizSmoothFall = 45;
                    s.VizFftRiseLow = 93;
                    s.VizFftRiseHigh = 95;
                    s.VizFftFallLow = 60;
                    s.VizFftFallHigh = 75;
                    s.VizColorTheme = (int)VizColorTheme.Neon;
                    s.VizGradientEnabled = true;
                    break;
            }
        }

        private void ShowButtonFeedback(object sender, string message)
        {
            if (sender is Button btn)
            {
                var original = btn.Content;
                btn.Content = message;
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = System.TimeSpan.FromSeconds(2)
                };
                timer.Tick += (s2, e2) =>
                {
                    btn.Content = original;
                    timer.Stop();
                };
                timer.Start();
            }
        }
    }
}
