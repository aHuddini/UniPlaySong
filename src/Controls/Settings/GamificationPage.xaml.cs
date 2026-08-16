using System.Windows;
using System.Windows.Controls;
using UniPlaySong.Services;

namespace UniPlaySong.Controls.Settings
{
    public partial class GamificationPage : UserControl
    {
        public GamificationPage()
        {
            InitializeComponent();
        }

        private void ResetGamificationTab_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsPageHelpers.ConfirmAndGetSettings(this, "Gamification");
            if (s == null) return;

            // Completion celebration
            s.EnableCompletionCelebration = true;
            s.CelebrateBeaten = false;
            s.CelebrationSoundType = CelebrationSoundType.BundledJingle;
            s.SelectedCelebrationJingle = "Streets of Rage 1 - Sega Genesis - Level Clear.mp3";
            s.CelebrationSoundPath = string.Empty;
            s.ShowCelebrationToast = true;
            s.CelebrationToastDurationSeconds = 8;
            s.CelebrationToastTheme = CelebrationToastTheme.Gold;
            s.ApplyLiveEffectsToJingles = true;
            // Achievement sound (master fallback) — bundled "Trophy Notif" default
            const string defaultAchJingle = "Achievements/Trophy_Notif.mp3";
            s.EnableAchievementSound = false;
            s.AchievementSoundType = CelebrationSoundType.BundledJingle;
            s.SelectedAchievementJingle = defaultAchJingle;
            s.AchievementSoundPath = string.Empty;
            // Achievement sound pack — PA Starter Pack default; clear all custom per-rarity files
            s.AchievementSoundPack = AchievementSoundPack.PAStarterPack;
            s.CommonAchievementSoundPath = string.Empty;
            s.UncommonAchievementSoundPath = string.Empty;
            s.RareAchievementSoundPath = string.Empty;
            s.UltraRareAchievementSoundPath = string.Empty;
            s.HiddenAchievementSoundPath = string.Empty;
            s.CapstoneAchievementSoundPath = string.Empty;
            // ControlUp events — bundled "Coin Pickup" default
            s.EnableControlUpDetectSound = false;
            s.ControlUpDetectSoundType = CelebrationSoundType.BundledJingle;
            s.SelectedControlUpDetectJingle = BundledJingleService.DefaultControlUpJingle;
            s.ControlUpDetectSoundPath = string.Empty;
            // Abandoned status
            s.EnableAbandonedSound = false;
            s.AbandonedSoundType = CelebrationSoundType.BundledJingle;
            s.SelectedAbandonedJingle = "Abandoned/Shinobi III - Sega Genesis - Round Clear.mp3";
            s.AbandonedSoundPath = string.Empty;
            s.ShowAbandonedToast = true;
            s.AbandonedToastDurationSeconds = 6;
            s.AbandonedToastTheme = AbandonedToastTheme.Tombstone;
            s.AbandonedToastMessage = "Filed away without finishing {gameName}.";

            SettingsPageHelpers.ShowButtonFeedback(sender, "Reset!");
        }
    }
}
