using System.Windows;
using System.Windows.Controls;

namespace UniPlaySong.Controls.Settings
{
    // Shared by the per-tab settings pages. These two used to be private members of the one
    // settings view; every page needs them now, so they live here rather than being copied
    // sixteen times.
    internal static class SettingsPageHelpers
    {
        // Per-tab reset: confirms with the user, returns the settings object or null if cancelled.
        internal static UniPlaySongSettings ConfirmAndGetSettings(FrameworkElement page, string tabName)
        {
            var vm = page.DataContext as UniPlaySongSettingsViewModel;
            if (vm == null) return null;

            var result = vm.PlayniteApi.Dialogs.ShowMessage(
                $"Reset {tabName} settings to defaults?",
                $"Reset {tabName}",
                MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes) return null;

            return vm.Settings;
        }

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
