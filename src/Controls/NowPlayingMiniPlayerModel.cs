using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace UniPlaySong.Controls
{
    // Shared DataContext model for the now-playing mini-player views (bar + one-liner).
    // Proxies the live NowPlaying* properties off UniPlaySongSettings (set by NowPlayingPublisher)
    // and raises change notifications so the views' XAML can bind them. Display-only. Composition
    // over inheritance: the views set this as their DataContext rather than subclassing a control
    // base (a XAML-rooted derived control base is not visible to WPF's markup-compile pass).
    public class NowPlayingMiniPlayerModel : INotifyPropertyChanged
    {
        private readonly UniPlaySongSettings _settings;
        private bool _subscribed;

        public NowPlayingMiniPlayerModel(UniPlaySongSettings settings)
        {
            _settings = settings;
        }

        // Call from the view's Loaded; guarded so repeated Loaded can't double-subscribe.
        public void Attach()
        {
            if (_subscribed || _settings == null) return;
            _settings.PropertyChanged += OnSettingsChanged;
            _subscribed = true;
            RaiseAll(); // initial paint
        }

        // Call from the view's Unloaded.
        public void Detach()
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

            // BeginInvoke (async), never Invoke: a sync Invoke from a non-UI thread can deadlock
            // when the raiser holds a lock the UI thread wants (the Spotify-radio launch freeze).
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                Application.Current.Dispatcher.BeginInvoke(new Action(RaiseAll));
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
        public bool IsPlaying => !string.IsNullOrEmpty(_settings?.NowPlayingTitle);

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
