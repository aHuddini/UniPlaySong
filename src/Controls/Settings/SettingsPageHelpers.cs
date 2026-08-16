using System.Windows.Controls;

namespace UniPlaySong.Controls.Settings
{
    // Shared by the settings pages.
    //
    // This also held ConfirmAndGetSettings, which every per-tab reset handler called. Reset is now
    // per rail group and lives on the view model's ResetGroupCommand, which does its own
    // confirmation, so nothing calls it any more.
    internal static class SettingsPageHelpers
    {
        // Momentarily swaps a button's caption to confirm the click landed, then restores it.
        internal static void ShowButtonFeedback(object sender, string message)
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
