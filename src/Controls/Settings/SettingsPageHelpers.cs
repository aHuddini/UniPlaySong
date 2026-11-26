using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UniPlaySong.Controls.Settings
{
    // A scrolling list inside the scrolling page swallows every wheel turn, even with nothing to scroll, so the page
    // stalls under the pointer. With this on, a turn the list can't use goes on to the page.
    public static class WheelPassThrough
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(WheelPassThrough),
                new PropertyMetadata(false, (d, e) =>
                {
                    if (!(d is UIElement el)) return;
                    el.PreviewMouseWheel -= OnWheel;
                    if ((bool)e.NewValue) el.PreviewMouseWheel += OnWheel;
                }));

        public static void SetIsEnabled(DependencyObject d, bool v) => d.SetValue(IsEnabledProperty, v);
        public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

        private static void OnWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindScrollViewer((DependencyObject)sender);
            if (sv != null && (e.Delta > 0 ? sv.VerticalOffset > 0 : sv.VerticalOffset < sv.ScrollableHeight)) return;

            e.Handled = true;
            (VisualTreeHelper.GetParent((DependencyObject)sender) as UIElement)?.RaiseEvent(
                new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender });
        }

        private static ScrollViewer FindScrollViewer(DependencyObject d)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                if (child is ScrollViewer sv) return sv;
                var found = FindScrollViewer(child);
                if (found != null) return found;
            }
            return null;
        }
    }

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
