using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;

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

