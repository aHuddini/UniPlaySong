using System.Windows;
using System.Windows.Markup;
using Playnite.SDK.Controls;
using UniPlaySong.Services;

namespace UniPlaySong.Controls
{
    // UPS_SpotifyRecentTracks / UPS_GameRecentTracks: a ready-made recent-tracks list. A theme restyles the rows with a
    // DataTemplate keyed by the element's name + "Template" (UPS_SpotifyRecentTracksTemplate), found where the element
    // sits; for a layout of its own it binds SpotifyRecentTracks / GameRecentTracks directly instead.
    public partial class RecentTracksControl : PluginUserControl
    {
        public RecentTracksControl(RecentTracks source, string templateKey)
        {
            ((IComponentConnector)this).InitializeComponent();
            List.ItemsSource = source.Items;
            Loaded += (s, e) => { if (TryFindResource(templateKey) is DataTemplate row) List.ItemTemplate = row; };
        }
    }
}
