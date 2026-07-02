using System.Windows.Markup;
using Playnite.SDK.Controls;

namespace UniPlaySong.Controls
{
    // PS5-style Now-Playing popup: large art, source, timeline, full transport + volume.
    public partial class MediaControllerOverlay : PluginUserControl
    {
        public MediaControllerOverlay(ActiveMediaViewModel vm)
        {
            ((IComponentConnector)this).InitializeComponent();
            DataContext = vm;
        }
    }
}
