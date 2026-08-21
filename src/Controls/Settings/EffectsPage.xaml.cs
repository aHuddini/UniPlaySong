using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace UniPlaySong.Controls.Settings
{
    public partial class EffectsPage : UserControl
    {
        public EffectsPage()
        {
            InitializeComponent();
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

            SettingsPageHelpers.ShowButtonFeedback(sender, "Copied!");
        }
    }
}
