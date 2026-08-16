using System.Windows;
using System.Windows.Controls;

namespace UniPlaySong.Controls.Settings
{
    public partial class EditingPage : UserControl
    {
        public EditingPage()
        {
            InitializeComponent();
        }

        private void ResetEditingTab_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsPageHelpers.ConfirmAndGetSettings(this, "Editing");
            if (s == null) return;

            // FFmpegPath is preserved
            s.NormalizationTargetLoudness = -16.0;
            s.NormalizationTruePeak = -1.5;
            s.NormalizationLoudnessRange = 11.0;
            s.NormalizationCodec = "auto";
            s.NormalizationSuffix = "-normalized";
            s.SkipAlreadyNormalized = true;
            s.DoNotPreserveOriginals = true;
            s.AutoNormalizeAfterDownload = false;
            s.TrimSuffix = "-trimmed";
            s.PreciseTrimSuffix = "-ptrimmed";
            s.ConversionTargetFormat = "ogg";
            s.ConversionBitrate = "192";
            s.ConversionKeepOriginals = false;

            SettingsPageHelpers.ShowButtonFeedback(sender, "Reset!");
        }
    }
}
