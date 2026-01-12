using System.Windows.Controls;

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
    }
}
