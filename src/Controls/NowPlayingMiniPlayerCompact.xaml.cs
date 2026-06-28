using System.Windows.Markup;
using Playnite.SDK.Controls;

namespace UniPlaySong.Controls
{
    // One-line mini-player: ♪ + title · artist · duration (duration Spotify-only).
    public partial class NowPlayingMiniPlayerCompact : PluginUserControl
    {
        private readonly NowPlayingMiniPlayerModel _model;

        public NowPlayingMiniPlayerCompact(UniPlaySongSettings settings)
        {
            _model = new NowPlayingMiniPlayerModel(settings);
            ((IComponentConnector)this).InitializeComponent();
            DataContext = _model;
            Loaded += (s, e) => _model.Attach();
            Unloaded += (s, e) => _model.Detach();
        }
    }
}
