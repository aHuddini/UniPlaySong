using System.Windows;
using System.Windows.Controls;
using UniPlaySong.Services;

namespace UniPlaySong.Controls.Settings
{
    public partial class CalmDownPage : UserControl
    {
        // Guards the slider handler while a preset is writing the three values. Without it the
        // first value the preset assigns raises ValueChanged, which flips the preset to Custom,
        // and the combo snaps back the instant the user picks anything.
        private bool _applyingPreset;

        public CalmDownPage()
        {
            InitializeComponent();
        }

        private void CalmDownPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var vm = DataContext as UniPlaySongSettingsViewModel;
            if (vm?.Settings == null) return;

            var preset = vm.Settings.SelectedCalmDownPreset;
            if (preset == CalmDownPreset.Custom) return;

            _applyingPreset = true;
            try
            {
                ApplyPresetValues(vm.Settings, preset);
            }
            finally
            {
                _applyingPreset = false;
            }
        }

        // Moving any slider means the values are no longer the preset's, so the combo says Custom
        // rather than naming a preset the sound no longer matches.
        private void Tuning_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_applyingPreset) return;

            var vm = DataContext as UniPlaySongSettingsViewModel;
            if (vm?.Settings == null) return;

            vm.Settings.SelectedCalmDownPreset = CalmDownPreset.Custom;
        }

        // Cutoff is where the roll-off starts, volume is the level it settles at, and seconds is
        // how long it takes to get there. The three move together: a heavy cut with no volume
        // drop reads as "dull", a volume drop with no cut reads as "quiet", and only both at once
        // sounds like the music moved into the next room.
        private static void ApplyPresetValues(UniPlaySongSettings s, CalmDownPreset preset)
        {
            switch (preset)
            {
                // What Calm Down has always done. Kept exactly so picking Default is a way back to
                // the shipped sound, not an approximation of it.
                case CalmDownPreset.Default:
                    s.CalmDownLowPassCutoffHz = 1500f;
                    s.CalmDownVolumeMultiplier = 0.5f;
                    s.CalmDownTransitionDurationSeconds = 1.5f;
                    break;

                case CalmDownPreset.Subtle:
                    s.CalmDownLowPassCutoffHz = 4000f;
                    s.CalmDownVolumeMultiplier = 0.75f;
                    s.CalmDownTransitionDurationSeconds = 1.5f;
                    break;

                // Tone change carries this one: a low cutoff with the level nearly untouched, for
                // when the music should stay present but stop being bright.
                case CalmDownPreset.Warm:
                    s.CalmDownLowPassCutoffHz = 1200f;
                    s.CalmDownVolumeMultiplier = 0.85f;
                    s.CalmDownTransitionDurationSeconds = 2.0f;
                    break;

                case CalmDownPreset.Muffled:
                    s.CalmDownLowPassCutoffHz = 700f;
                    s.CalmDownVolumeMultiplier = 0.5f;
                    s.CalmDownTransitionDurationSeconds = 2.0f;
                    break;

                case CalmDownPreset.Distant:
                    s.CalmDownLowPassCutoffHz = 500f;
                    s.CalmDownVolumeMultiplier = 0.3f;
                    s.CalmDownTransitionDurationSeconds = 3.0f;
                    break;

                case CalmDownPreset.Whisper:
                    s.CalmDownLowPassCutoffHz = 900f;
                    s.CalmDownVolumeMultiplier = 0.10f;
                    s.CalmDownTransitionDurationSeconds = 3.0f;
                    break;
            }
        }

        // Names the properties but not their values — those come off a pristine settings object,
        // so this cannot drift from the shipped defaults.
        private static readonly string[] CalmDownSettings =
        {
            nameof(UniPlaySongSettings.SelectedCalmDownPreset),
            nameof(UniPlaySongSettings.CalmDownLowPassCutoffHz),
            nameof(UniPlaySongSettings.CalmDownVolumeMultiplier),
            nameof(UniPlaySongSettings.CalmDownTransitionDurationSeconds),
        };

        private void ResetCalmDown_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as UniPlaySongSettingsViewModel;
            if (vm?.Settings == null) return;

            _applyingPreset = true;
            try
            {
                SettingsResetService.ResetProperties(vm.Settings, CalmDownSettings);
            }
            finally
            {
                _applyingPreset = false;
            }

            SettingsPageHelpers.ShowButtonFeedback(sender, "Reset!");
        }
    }
}
