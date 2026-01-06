using System.Windows.Controls;

namespace UniPlaySong
{
    public partial class UniPlaySongSettingsView : UserControl
    {
        public UniPlaySongSettingsView(UniPlaySongSettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
