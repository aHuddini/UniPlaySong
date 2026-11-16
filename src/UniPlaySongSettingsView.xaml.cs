using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UniPlaySong.Common;

namespace UniPlaySong
{
    // Shell only. Every tab's content — and the handlers that go with it — lives in its own
    // UserControl under Controls/Settings/.
    public partial class UniPlaySongSettingsView : UserControl
    {
        public UniPlaySongSettingsView()
        {
            // [Perf] lines (debug logging only): build, then time to Loaded, then to the first idle after it,
            // which is when layout and the first render of the visible page are done.
            var perf = SettingsOpenTiming.Start("view");
            InitializeComponent();
            perf.Step("construct (all pages)");

            RoutedEventHandler onLoaded = null;
            onLoaded = (s, e) =>
            {
                Loaded -= onLoaded;
                perf.Step("until Loaded");
                Dispatcher.BeginInvoke(new Action(() => perf.Step("layout + first render")), DispatcherPriority.ContextIdle);
            };
            Loaded += onLoaded;
            // DO NOT set DataContext manually - Playnite sets it automatically
            // to the ISettings object returned by GetSettings()
        }
    }
}
