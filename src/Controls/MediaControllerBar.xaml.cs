using System.Windows.Markup;
using Playnite.SDK.Controls;

namespace UniPlaySong.Controls
{
    // Horizontal transport pill: art + title/artist + prev/playpause/next.
    public partial class MediaControllerBar : PluginUserControl
    {
        public MediaControllerBar(ActiveMediaViewModel vm)
        {
            ((IComponentConnector)this).InitializeComponent();
            DataContext = vm;
        }
    }
}
