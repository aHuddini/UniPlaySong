using System;
using System.Linq;
using UniPlaySong.Common;
using Windows.Media.Control;
using WindowsMediaController;

namespace UniPlaySong.Services.Spotify
{
    // SMTC implementation of ISpotifyClient. Finds the Spotify media session by
    // case-insensitive substring on its id (works for both the Win32 "Spotify.exe"
    // and Store "SpotifyAB.SpotifyMusic_...!Spotify" builds), and pauses/resumes it
    // via the OS. All Spotify interaction is funneled here and individually wrapped,
    // so any failure surfaces as "unavailable" rather than throwing.
    public class SpotifySmtcClient : ISpotifyClient, IDisposable
    {
        private readonly FileLogger _fileLogger;
        private MediaManager _manager;
        private bool _started;
        private bool _disposed;

        public event Action AvailabilityChanged;

        public SpotifySmtcClient(FileLogger fileLogger)
        {
            _fileLogger = fileLogger;
            TryStart();
        }

        private void TryStart()
        {
            try
            {
                _manager = new MediaManager();
                _manager.OnAnySessionOpened += OnSessionsChanged;
                _manager.OnAnySessionClosed += OnSessionsChanged;
                _manager.OnAnyPlaybackStateChanged += OnPlaybackChanged;
                _manager.Start();
                _started = true;
            }
            catch (Exception ex)
            {
                // SMTC unavailable (e.g. pre-Windows-10-1809). Permanent no-op.
                _fileLogger?.Warn($"[Spotify] SMTC unavailable, control disabled: {ex.Message}");
                _started = false;
            }
        }

        private void OnSessionsChanged(MediaManager.MediaSession session) => AvailabilityChanged?.Invoke();
        private void OnPlaybackChanged(MediaManager.MediaSession session, GlobalSystemMediaTransportControlsSessionPlaybackInfo info) => AvailabilityChanged?.Invoke();

        // Re-pull the Spotify session every call rather than caching — the library's
        // close events are unreliable, so we never trust a stale reference.
        private MediaManager.MediaSession FindSpotify()
        {
            if (!_started || _manager == null) return null;
            try
            {
                return _manager.CurrentMediaSessions.Values.FirstOrDefault(s =>
                    (s.Id ?? string.Empty).IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[Spotify] FindSpotify failed: {ex.Message}");
                return null;
            }
        }

        public bool IsAvailable => FindSpotify() != null;

        public bool IsPlaying
        {
            get
            {
                var s = FindSpotify();
                if (s == null) return false;
                try
                {
                    return s.ControlSession?.GetPlaybackInfo()?.PlaybackStatus
                        == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                }
                catch { return false; }
            }
        }

        public bool TryPause()
        {
            var s = FindSpotify();
            if (s == null) return false;
            try
            {
                var info = s.ControlSession?.GetPlaybackInfo();
                if (info?.Controls.IsPauseEnabled != true) return false;
                return s.ControlSession.TryPauseAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[Spotify] TryPause failed: {ex.Message}");
                return false;
            }
        }

        public bool TryResume()
        {
            var s = FindSpotify();
            if (s == null) return false;
            try
            {
                var info = s.ControlSession?.GetPlaybackInfo();
                if (info?.Controls.IsPlayEnabled != true) return false;
                return s.ControlSession.TryPlayAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[Spotify] TryResume failed: {ex.Message}");
                return false;
            }
        }

        public SpotifyNowPlaying GetNowPlaying()
        {
            var s = FindSpotify();
            if (s == null) return SpotifyNowPlaying.Empty;
            try
            {
                var props = s.ControlSession?.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
                if (props == null) return SpotifyNowPlaying.Empty;
                if (string.Equals(props.Title, "Advertisement", StringComparison.OrdinalIgnoreCase))
                    return SpotifyNowPlaying.Empty;
                return new SpotifyNowPlaying(props.Title, props.Artist);
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[Spotify] GetNowPlaying failed: {ex.Message}");
                return SpotifyNowPlaying.Empty;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _manager?.Dispose(); }
            catch (Exception ex) { _fileLogger?.Debug($"[Spotify] Dispose failed: {ex.Message}"); }
            _manager = null;
        }
    }
}
