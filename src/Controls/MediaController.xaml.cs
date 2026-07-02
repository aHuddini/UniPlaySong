using System.Windows.Markup;
using Playnite.SDK.Controls;

namespace UniPlaySong.Controls
{
    // Invisible host exposing the shared ActiveMediaViewModel as DataContext.
    // For fully custom theme layouts binding the VM's commands + properties.
    public partial class MediaController : PluginUserControl
    {
        public MediaController(ActiveMediaViewModel vm)
        {
            ((IComponentConnector)this).InitializeComponent();
            DataContext = vm;
        }
    }
}
