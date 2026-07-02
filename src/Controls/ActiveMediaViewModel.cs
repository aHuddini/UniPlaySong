using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using UniPlaySong.Common;
using UniPlaySong.Models;
using UniPlaySong.Services;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Controls
{
    // Thin shared ViewModel over IActiveMediaService. Proxies now-playing metadata
    // from settings (like NowPlayingMiniPlayerModel), adds transport commands + timeline
    // state, and mirrors the snapshot back into the settings ActiveMedia* props for
    // decoupled {PluginSettings} binding. One instance shared by all UPS media elements.
    //
    // Settings are read through SettingsService.Current (never a captured object): a settings
    // SAVE replaces the whole settings object, so a captured reference goes stale and the
    // element freezes until restart. We track SettingsChanged and move our metadata
    // PropertyChanged handler onto the new object (same pattern as SidebarGlowManager).
    public class ActiveMediaViewModel : INotifyPropertyChanged
    {
        private readonly ISettingsProvider _svc;
        private readonly IActiveMediaService _service;
        private UniPlaySongSettings _subscribedTo; // object our metadata PropertyChanged handler is on
        private bool _subscribed;
        private ActiveMediaSnapshot _snap = ActiveMediaSnapshot.Empty;

        private UniPlaySongSettings Settings => _svc?.Current;

        public ActiveMediaViewModel(ISettingsProvider svc, IActiveMediaService service)
        {
            _svc = svc;
            _service = service;

            PlayPauseCommand = new RelayCommand(() => _service?.PlayPause());
            NextCommand = new RelayCommand(() => _service?.Next(), () => _snap.CanNext);
            PreviousCommand = new RelayCommand(() => _service?.Previous(), () => _snap.CanPrevious);
            ToggleMuteCommand = new RelayCommand(() => _service?.ToggleMute());
        }

        public void Attach()
        {
            if (_subscribed) return;
            _subscribedTo = _svc?.Current;
            if (_subscribedTo != null) _subscribedTo.PropertyChanged += OnSettingsChanged;
            if (_svc != null) _svc.SettingsChanged += OnSettingsReplaced;
            if (_service != null) _service.Changed += OnServiceChanged;
            _subscribed = true;
            OnServiceChanged();   // initial snapshot
            RaiseMetadata();      // initial metadata paint
        }

        public void Detach()
        {
            if (!_subscribed) return;
            if (_subscribedTo != null) _subscribedTo.PropertyChanged -= OnSettingsChanged;
            _subscribedTo = null;
            if (_svc != null) _svc.SettingsChanged -= OnSettingsReplaced;
            if (_service != null) _service.Changed -= OnServiceChanged;
            _subscribed = false;
        }

        // A save/load swaps the settings object wholesale. Move our metadata PropertyChanged
        // handler to the new instance and repaint the proxied metadata.
        private void OnSettingsReplaced(object sender, SettingsChangedEventArgs e)
        {
            if (_subscribedTo != null) _subscribedTo.PropertyChanged -= OnSettingsChanged;
            _subscribedTo = e.NewSettings;
            if (_subscribedTo != null) _subscribedTo.PropertyChanged += OnSettingsChanged;
            RaiseMetadata();
        }

        // ── transport commands ──
        public ICommand PlayPauseCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand ToggleMuteCommand { get; }

        // ── metadata (proxied from settings, same source as the existing mini-players) ──
        public string Title => Settings?.NowPlayingTitle ?? string.Empty;
        public string Artist => Settings?.NowPlayingArtist ?? string.Empty;
        public string AlbumArtPath => Settings?.NowPlayingAlbumArtPath ?? string.Empty;
        public string Album => Settings?.NowPlayingAlbum ?? string.Empty;
        public string Genre => Settings?.NowPlayingGenre ?? string.Empty;
        public string Duration => Settings?.NowPlayingDuration ?? string.Empty;
        public bool HasArt => !string.IsNullOrEmpty(AlbumArtPath);

        // ── active-media snapshot state ──
        public bool HasActiveMedia => _snap.HasActiveMedia;
        public ActiveMediaSourceKind SourceKind => _snap.SourceKind;
        public string SourceName => _snap.SourceName;
        public bool IsPlaying => _snap.IsPlaying;
        public bool IsMuted => _snap.IsMuted;
        public double Progress => _snap.Progress;
        public string PositionText => _snap.PositionText;
        public string DurationText => _snap.DurationText;
        public bool HasTimeline => !string.IsNullOrEmpty(_snap.DurationText);
        public bool CanNext => _snap.CanNext;
        public bool CanPrevious => _snap.CanPrevious;

        // Volume is two-way: getter from snapshot, setter routes to the service.
        public double Volume
        {
            get => _snap.Volume;
            set { _service?.SetVolume(value); }
        }

        private void OnServiceChanged()
        {
            // GetSnapshot() is a cheap, fail-safe read — fine on the calling thread
            // (may be a non-UI SMTC/WinRT callback thread). Everything that follows
            // touches settings/PropertyChanged and must run on the UI thread, so it's
            // marshalled via OnUi (never a synchronous Dispatcher.Invoke).
            _snap = _service?.GetSnapshot() ?? ActiveMediaSnapshot.Empty;
            OnUi(() =>
            {
                MirrorToSettings(_snap);
                RaiseSnapshotCore();
                CommandManager.InvalidateRequerySuggested();
            });
        }

        private void MirrorToSettings(ActiveMediaSnapshot s)
        {
            var st = Settings;
            if (st == null) return;
            st.ActiveMediaProgress = s.Progress;
            st.ActiveMediaPositionText = s.PositionText;
            st.ActiveMediaDurationText = s.DurationText;
            st.ActiveMediaVolume = s.Volume;
            st.ActiveMediaIsPlaying = s.IsPlaying;
            st.ActiveMediaSourceName = s.SourceName;
            st.ActiveMediaSourceKind = s.SourceKind;
            st.ActiveMediaHasMedia = s.HasActiveMedia;
            st.ActiveMediaCanNext = s.CanNext;
            st.ActiveMediaCanPrevious = s.CanPrevious;
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(UniPlaySongSettings.NowPlayingTitle) &&
                e.PropertyName != nameof(UniPlaySongSettings.NowPlayingArtist) &&
                e.PropertyName != nameof(UniPlaySongSettings.NowPlayingAlbumArtPath) &&
                e.PropertyName != nameof(UniPlaySongSettings.NowPlayingAlbum) &&
                e.PropertyName != nameof(UniPlaySongSettings.NowPlayingGenre) &&
                e.PropertyName != nameof(UniPlaySongSettings.NowPlayingDuration))
                return;
            RaiseMetadata();
        }

        // ── change notification (BeginInvoke, never sync Invoke — deadlock-fix rule) ──
        // Unmarshalled core — call only from within an OnUi(...) block (or another
        // already-UI-thread context) to avoid nested/double dispatching.
        private void RaiseSnapshotCore()
        {
            Raise(nameof(HasActiveMedia)); Raise(nameof(SourceKind)); Raise(nameof(SourceName));
            Raise(nameof(IsPlaying)); Raise(nameof(IsMuted)); Raise(nameof(Progress));
            Raise(nameof(PositionText)); Raise(nameof(DurationText)); Raise(nameof(HasTimeline));
            Raise(nameof(Volume)); Raise(nameof(CanNext)); Raise(nameof(CanPrevious));
        }

        private void RaiseMetadata()
        {
            OnUi(() =>
            {
                Raise(nameof(Title)); Raise(nameof(Artist)); Raise(nameof(AlbumArtPath));
                Raise(nameof(Album)); Raise(nameof(Genre)); Raise(nameof(Duration)); Raise(nameof(HasArt));
            });
        }

        private static void OnUi(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d != null && !d.CheckAccess()) d.BeginInvoke(a);
            else a();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
