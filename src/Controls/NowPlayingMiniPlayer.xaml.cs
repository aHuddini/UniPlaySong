using System.Windows.Markup;
using Playnite.SDK.Controls;

namespace UniPlaySong.Controls
{
    // Horizontal bar mini-player: album art + title/artist + Spotify album/genre/duration sub-line.
    public partial class NowPlayingMiniPlayer : PluginUserControl
    {
        private readonly NowPlayingMiniPlayerModel _model;

        public NowPlayingMiniPlayer(UniPlaySongSettings settings)
        {
            _model = new NowPlayingMiniPlayerModel(settings);
            ((IComponentConnector)this).InitializeComponent();
            DataContext = _model;
            Loaded += (s, e) => _model.Attach();
            Unloaded += (s, e) => _model.Detach();
        }
    }
}
