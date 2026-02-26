using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using UniPlaySong.Common;

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

        // Per-tab reset helper: shows confirmation, returns settings object or null if cancelled
        private UniPlaySongSettings ConfirmAndGetSettings(string tabName)
        {
            var vm = DataContext as UniPlaySongSettingsViewModel;
            if (vm == null) return null;

            var result = vm.PlayniteApi.Dialogs.ShowMessage(
                $"Reset {tabName} settings to defaults?",
                $"Reset {tabName}",
                System.Windows.MessageBoxButton.YesNo);
            if (result != System.Windows.MessageBoxResult.Yes) return null;

            return vm.Settings;
        }

        private void ResetGeneralTab_Click(object sender, RoutedEventArgs e)
        {
            var s = ConfirmAndGetSettings("General");
            if (s == null) return;

            s.EnableMusic = true;
            s.SuppressPlayniteBackgroundMusic = true;
            s.MusicState = AudioState.Always;
            s.SkipFirstSelectionAfterModeSwitch = false;
            s.ThemeCompatibleSilentSkip = true;
            s.ShowDesktopMediaControls = true;
            s.ShowTaskbarMediaControls = true;
            s.ShowNowPlayingInTopPanel = true;
            s.HideNowPlayingForDefaultMusic = false;
            s.ShowDefaultMusicIndicator = true;
            s.ShowProgressBar = false;
            s.ProgressBarPosition = ProgressBarPosition.AfterSkipButton;
            s.AutoTagOnLibraryUpdate = true;
            s.AutoDeleteMusicOnGameRemoval = true;
            s.EnableSongListCache = false;
            s.EnableDebugLogging = false;

            ShowButtonFeedback(sender, "Reset!");
        }

        private void ResetPlaybackTab_Click(object sender, RoutedEventArgs e)
        {
            var s = ConfirmAndGetSettings("Playback");
            if (s == null) return;

            s.MusicVolume = Constants.DefaultMusicVolume;
            s.LowerVolumeOnIdle = false;
            s.IdleVolumeTimeoutMinutes = 15;
            s.FadeInDuration = Constants.DefaultFadeInDuration;
            s.FadeOutDuration = Constants.DefaultFadeOutDuration;
            s.EnablePreviewMode = false;
            s.PreviewDuration = Constants.DefaultPreviewDuration;
            s.PauseOnTrailer = true;
            s.RandomizeOnEverySelect = true;
            s.RandomizeOnMusicEnd = true;
            s.StopAfterSongEnds = false;
            s.EnableDefaultMusic = true;
            s.DefaultMusicSourceOption = DefaultMusicSource.BundledPreset;
            s.SelectedBundledPreset = "tunetank-dark-ambient-soundscape-music.mp3";
            s.DefaultMusicPath = string.Empty;
            s.DefaultMusicFolderPath = string.Empty;
            s.CustomRotationGameIds = new List<Guid>();
            s.DefaultMusicContinueSameSong = false;
            s.BackupCustomMusicPath = string.Empty;
            s.MusicOnlyForInstalledGames = false;
            s.NostalgiaMode = false;
            s.NostalgiaStatusIds = new List<Guid>();
            s.DefaultMusicStatusPoolIds = new List<Guid>();
            s.GamePropFilterEnabled = false;
            s.GamePropFilterPlatformIds = new List<Guid>();
            s.GamePropFilterGenreIds = new List<Guid>();
            s.GamePropFilterSourceIds = new List<Guid>();
            s.FilterModeEnabled = false;
            s.RadioModeEnabled = false;
            s.RadioMusicSource = RadioMusicSource.FullLibrary;
            s.EnableCompletionCelebration = true;
            s.CelebrationSoundType = CelebrationSoundType.BundledJingle;
            s.SelectedCelebrationJingle = "Streets of Rage 1 - Sega Genesis - Level Clear.mp3";
            s.CelebrationSoundPath = string.Empty;
            s.PlaySoundOnDownloadComplete = false;
            s.ApplyLiveEffectsToJingles = true;
            s.ShowCelebrationToast = true;
            s.CelebrationToastDurationSeconds = 8;
            s.CelebrationToastTheme = CelebrationToastTheme.Gold;
            s.EnableRandomPickerMusic = true;

            ShowButtonFeedback(sender, "Reset!");
        }

        private void ResetPausesTab_Click(object sender, RoutedEventArgs e)
        {
            var s = ConfirmAndGetSettings("Pauses");
            if (s == null) return;

            s.PauseOnGameStart = true;
            s.PauseOnSystemLock = false;
            s.PauseOnFocusLoss = false;
            s.FocusLossStayPaused = false;
            s.FocusLossIgnoreBrief = false;
            s.PauseOnMinimize = true;
            s.PauseWhenInSystemTray = true;
            s.PauseOnExternalAudio = false;
            s.ExternalAudioDebounceSeconds = 0;
            s.ExternalAudioInstantPause = false;
            s.ExternalAudioExcludedApps = "obs64, obs32";
            s.PauseOnIdle = false;
            s.IdleTimeoutMinutes = 15;

            ShowButtonFeedback(sender, "Reset!");
        }

        private void ResetLiveEffectsTab_Click(object sender, RoutedEventArgs e)
        {
            var s = ConfirmAndGetSettings("Live Effects");
            if (s == null) return;

            // Reset master toggles
            s.LiveEffectsEnabled = true;
            s.ShowSpectrumVisualizer = true;
            s.ShowPeakMeter = false;

            // Reset all effects (reuses same logic as existing inline reset)
            s.SelectedStylePreset = StylePreset.HuddiniRehearsal;
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

            // Reset advanced reverb tuning
            s.AdvancedReverbTuningEnabled = false;
            s.ReverbWetGainMultiplier = 3;
            s.ReverbAllpassFeedback = 50;
            s.ReverbHfDampingMin = 20;
            s.ReverbHfDampingMax = 50;

            // Reset visualizer tuning to defaults
            ApplyVizPresetValues(s, VizPreset.Default);
            s.SelectedVizPreset = VizPreset.Punchy;
            s.VizColorTheme = 0; // Dynamic
            s.VizGradientEnabled = true;

            // Reset dynamic color tuning
            s.DynMinBrightnessBottom = 200;
            s.DynMinBrightnessTop = 150;
            s.DynMinSatBottom = 30;
            s.DynMinSatTop = 35;

            ShowButtonFeedback(sender, "Reset!");
        }

        private void ResetEditingTab_Click(object sender, RoutedEventArgs e)
        {
            var s = ConfirmAndGetSettings("Editing");
            if (s == null) return;

            // FFmpegPath is preserved
            s.NormalizationTargetLoudness = -16.0;
            s.NormalizationTruePeak = -1.5;
            s.NormalizationLoudnessRange = 11.0;
            s.NormalizationCodec = "libmp3lame";
            s.NormalizationSuffix = "-normalized";
            s.SkipAlreadyNormalized = true;
            s.DoNotPreserveOriginals = true;
            s.AutoNormalizeAfterDownload = false;
            s.TrimSuffix = "-trimmed";
            s.PreciseTrimSuffix = "-ptrimmed";

            ShowButtonFeedback(sender, "Reset!");
        }

        private void ResetDownloadsTab_Click(object sender, RoutedEventArgs e)
        {
            var s = ConfirmAndGetSettings("Downloads");
            if (s == null) return;

            // YtDlpPath is preserved
            s.CookieMode = CookieMode.None;
            s.CustomCookiesFilePath = string.Empty;
            s.AutoDownloadOnLibraryUpdate = true;
            s.AutoDownloadOnGameInstall = true;
            s.MaxConcurrentDownloads = 3;

            ShowButtonFeedback(sender, "Reset!");
        }

        private void ResetSearchTab_Click(object sender, RoutedEventArgs e)
        {
            var s = ConfirmAndGetSettings("Search");
            if (s == null) return;

            s.EnableSearchCache = true;
            s.SearchCacheDurationDays = 7;
            s.AutoCheckHintsOnStartup = false;

            ShowButtonFeedback(sender, "Reset!");
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
