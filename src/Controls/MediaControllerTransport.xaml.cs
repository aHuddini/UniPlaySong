using System.Windows.Markup;
using Playnite.SDK.Controls;

namespace UniPlaySong.Controls
{
    // Transport buttons alone, for theme pages that lay out their own art, title and timeline.
    public partial class MediaControllerTransport : PluginUserControl
    {
        public MediaControllerTransport(ActiveMediaViewModel vm)
        {
            ((IComponentConnector)this).InitializeComponent();
            DataContext = vm;
        }
    }
}
