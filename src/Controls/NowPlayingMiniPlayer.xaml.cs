using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Markup;
using Playnite.SDK.Controls;

namespace UniPlaySong.Controls
{
    // Horizontal bar mini-player: album art + title/artist + Spotify album/genre/duration sub-line.
    public partial class NowPlayingMiniPlayer : PluginUserControl, INotifyPropertyChanged
    {
        private readonly UniPlaySongSettings _settings;
        private bool _subscribed;

        public NowPlayingMiniPlayer(UniPlaySongSettings settings)
        {
            ((IComponentConnector)this).InitializeComponent();
            _settings = settings;
            DataContext = this;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_subscribed || _settings == null) return;
            _settings.PropertyChanged += OnSettingsChanged;
            _subscribed = true;
            RaiseAll();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_subscribed || _settings == null) return;
            _settings.PropertyChanged -= OnSettingsChanged;
            _subscribed = false;
        }

        // The publisher may raise PropertyChanged off the UI thread; marshal the refresh.
        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(UniPlaySongSettings.NowPlayingTitle) &&
                e.PropertyName != nameof(UniPlaySongSettings.NowPlayingArtist) &&
                e.PropertyName != nameof(UniPlaySongSettings.NowPlayingAlbumArtPath) &&
                e.PropertyName != nameof(UniPlaySongSettings.NowPlayingAlbum) &&
                e.PropertyName != nameof(UniPlaySongSettings.NowPlayingGenre) &&
                e.PropertyName != nameof(UniPlaySongSettings.NowPlayingDuration))
                return;

            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                Application.Current.Dispatcher.Invoke(RaiseAll);
            else
                RaiseAll();
        }

        private void RaiseAll()
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Artist));
            OnPropertyChanged(nameof(AlbumArtPath));
            OnPropertyChanged(nameof(Album));
            OnPropertyChanged(nameof(Genre));
            OnPropertyChanged(nameof(Duration));
            OnPropertyChanged(nameof(HasArt));
            OnPropertyChanged(nameof(HasNoArt));
            OnPropertyChanged(nameof(HasAlbum));
            OnPropertyChanged(nameof(HasGenre));
            OnPropertyChanged(nameof(HasDuration));
            OnPropertyChanged(nameof(IsPlaying));
        }

        public string Title => _settings?.NowPlayingTitle ?? string.Empty;
        public string Artist => _settings?.NowPlayingArtist ?? string.Empty;
        public string AlbumArtPath => _settings?.NowPlayingAlbumArtPath ?? string.Empty;
        public string Album => _settings?.NowPlayingAlbum ?? string.Empty;
        public string Genre => _settings?.NowPlayingGenre ?? string.Empty;
        public string Duration => _settings?.NowPlayingDuration ?? string.Empty;

        public bool HasArt => !string.IsNullOrEmpty(_settings?.NowPlayingAlbumArtPath);
        public bool HasNoArt => !HasArt;
        public bool HasAlbum => !string.IsNullOrEmpty(_settings?.NowPlayingAlbum);
        public bool HasGenre => !string.IsNullOrEmpty(_settings?.NowPlayingGenre);
        public bool HasDuration => !string.IsNullOrEmpty(_settings?.NowPlayingDuration);
        // Whole-control visibility: collapse when nothing is playing.
        public bool IsPlaying => !string.IsNullOrEmpty(_settings?.NowPlayingTitle);

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
