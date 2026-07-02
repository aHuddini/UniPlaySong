using System.Windows.Markup;
using Playnite.SDK.Controls;

namespace UniPlaySong.Controls
{
    // Minimal one-line transport: title + play/pause + next. For tight Desktop/top-panel spots.
    public partial class MediaControllerCompact : PluginUserControl
    {
        public MediaControllerCompact(ActiveMediaViewModel vm)
        {
            ((IComponentConnector)this).InitializeComponent();
            DataContext = vm;
        }
    }
}
