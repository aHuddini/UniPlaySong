using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using UniPlaySong.Services;

namespace UniPlaySong.Controls
{
    // Shared DataContext model for the now-playing mini-player views (bar + one-liner).
    // Proxies the live NowPlaying* properties off UniPlaySongSettings (set by NowPlayingPublisher)
    // and raises change notifications so the views' XAML can bind them. Display-only. Composition
    // over inheritance: the views set this as their DataContext rather than subclassing a control
    // base (a XAML-rooted derived control base is not visible to WPF's markup-compile pass).
    //
    // Reads settings through SettingsService.Current (never a captured object): a settings SAVE
    // replaces the whole settings object (UpdateSettings), so a captured reference goes stale and
    // the view freezes until restart. We track SettingsChanged and move our PropertyChanged handler
    // onto the new object (same pattern as SidebarGlowManager).
    public class NowPlayingMiniPlayerModel : INotifyPropertyChanged
    {
        private readonly ISettingsProvider _svc;
        private UniPlaySongSettings _subscribedTo; // the object our PropertyChanged handler is on
        private bool _subscribed;

        public NowPlayingMiniPlayerModel(ISettingsProvider svc)
        {
            _svc = svc;
        }

        private UniPlaySongSettings Settings => _svc?.Current;

        // Call from the view's Loaded; guarded so repeated Loaded can't double-subscribe.
        public void Attach()
        {
            if (_subscribed || _svc == null) return;
            _subscribedTo = _svc.Current;
            if (_subscribedTo != null) _subscribedTo.PropertyChanged += OnSettingsChanged;
            _svc.SettingsChanged += OnSettingsReplaced;
            _subscribed = true;
            RaiseAll(); // initial paint
        }

        // Call from the view's Unloaded.
        public void Detach()
        {
            if (!_subscribed || _svc == null) return;
            if (_subscribedTo != null) _subscribedTo.PropertyChanged -= OnSettingsChanged;
            _subscribedTo = null;
            _svc.SettingsChanged -= OnSettingsReplaced;
            _subscribed = false;
        }

        // A save/load swaps the settings object wholesale. Move our PropertyChanged handler to the
        // new instance and repaint (the new object holds the current NowPlaying* values).
        private void OnSettingsReplaced(object sender, SettingsChangedEventArgs e)
        {
            if (_subscribedTo != null) _subscribedTo.PropertyChanged -= OnSettingsChanged;
            _subscribedTo = e.NewSettings;
            if (_subscribedTo != null) _subscribedTo.PropertyChanged += OnSettingsChanged;

            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                Application.Current.Dispatcher.BeginInvoke(new Action(RaiseAll));
            else
                RaiseAll();
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

        public string Title => Settings?.NowPlayingTitle ?? string.Empty;
        public string Artist => Settings?.NowPlayingArtist ?? string.Empty;
        public string AlbumArtPath => Settings?.NowPlayingAlbumArtPath ?? string.Empty;
        public string Album => Settings?.NowPlayingAlbum ?? string.Empty;
        public string Genre => Settings?.NowPlayingGenre ?? string.Empty;
        public string Duration => Settings?.NowPlayingDuration ?? string.Empty;

        public bool HasArt => !string.IsNullOrEmpty(Settings?.NowPlayingAlbumArtPath);
        public bool HasNoArt => !HasArt;
        public bool HasAlbum => !string.IsNullOrEmpty(Settings?.NowPlayingAlbum);
        public bool HasGenre => !string.IsNullOrEmpty(Settings?.NowPlayingGenre);
        public bool HasDuration => !string.IsNullOrEmpty(Settings?.NowPlayingDuration);
        public bool IsPlaying => !string.IsNullOrEmpty(Settings?.NowPlayingTitle);

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
