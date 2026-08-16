using System.Windows.Controls;

namespace UniPlaySong.Controls.Settings
{
    public partial class VisualizersPage : UserControl
    {
        public VisualizersPage()
        {
            InitializeComponent();
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
                // Presets only set tuning parameters (gain, gravity, smoothing, etc.)
                // Color theme and gradient are independent — user can combine any preset with any color.
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
                    break;

                case VizPreset.Smooth:
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
                    break;

                case VizPreset.Punchy:
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
                    break;

                case VizPreset.Cinematic:
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
                    break;

                case VizPreset.Minimal:
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
                    break;

                case VizPreset.Reactive:
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
                    break;
            }
        }
    }
}
