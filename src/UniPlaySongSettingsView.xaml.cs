using System.Windows.Controls;

namespace UniPlaySong
{
    // Shell only. Every tab's content — and the handlers that go with it — lives in its own
    // UserControl under Controls/Settings/.
    public partial class UniPlaySongSettingsView : UserControl
    {
        public UniPlaySongSettingsView()
        {
            InitializeComponent();
            // DO NOT set DataContext manually - Playnite sets it automatically
            // to the ISettings object returned by GetSettings()
        }
    }
}
