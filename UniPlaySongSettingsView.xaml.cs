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
