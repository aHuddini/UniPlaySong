using System;
using System.IO;
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
    // All blocking SMTC calls (.GetAwaiter().GetResult()) run on the worker thread,
    // never on the caller/UI thread.
    public class SpotifySmtcClient : ISpotifyClient, IDisposable
    {
        private readonly FileLogger _fileLogger;
        private MediaManager _manager;
        private bool _started;
        private bool _disposed;
        private readonly SpotifySmtcWorker _worker;
        private volatile bool _cachedAvailable;
        private volatile bool _cachedPlaying;

        public event Action AvailabilityChanged;

        public SpotifySmtcClient(FileLogger fileLogger)
        {
            _fileLogger = fileLogger;
            _worker = new SpotifySmtcWorker(fileLogger);
            _worker.Start();
            // WinRT init OFF the constructing thread: the plugin ctor runs on Playnite's UI thread,
            // and MediaManager.Start() + the first cache read block on the WinRT broker (can take
            // seconds on a cold start). The client just reports unavailable until startup completes;
            // TryStart announces readiness via AvailabilityChanged.
            _worker.PostRequest(TryStart);
        }

        private void TryStart()
        {
            if (_disposed) return;
            try
            {
                _manager = new MediaManager();
                _manager.OnAnySessionOpened += OnSessionsChanged;
                _manager.OnAnySessionClosed += OnSessionsChanged;
                _manager.OnAnyPlaybackStateChanged += OnPlaybackChanged;
                _manager.OnAnyMediaPropertyChanged += OnMediaPropertyChanged;
                _manager.Start();
                _started = true;
                RefreshCache();
                // Disposed while starting (e.g. Playnite shutting down mid-launch): tear down now.
                if (_disposed) { try { _manager?.Dispose(); } catch { } return; }
                // Announce readiness so subscribers recompute immediately (radio engages without
                // waiting for the periodic poll).
                AvailabilityChanged?.Invoke();
            }
            catch (Exception ex)
            {
                // SMTC unavailable (e.g. pre-Windows-10-1809). Permanent no-op.
                _fileLogger?.Warn($"[Spotify] SMTC unavailable, control disabled: {ex.Message}");
                _started = false;
            }
        }

        private void OnSessionsChanged(MediaManager.MediaSession session) { RefreshCache(); AvailabilityChanged?.Invoke(); }
        private void OnPlaybackChanged(MediaManager.MediaSession session, GlobalSystemMediaTransportControlsSessionPlaybackInfo info) { RefreshCache(); AvailabilityChanged?.Invoke(); }
        // The OS pushes this when the current track's metadata changes (a track change). Without this
        // subscription the now-playing UI never learned Spotify changed tracks (the mini-player went
        // stale). Fan out through the existing AvailabilityChanged → SpotifyControlService.Recompute →
        // NowPlayingChanged → NowPlayingPublisher chain, which refreshes now-playing. Cheap handler:
        // it only raises the event; the publisher does the (already-existing) metadata fetch.
        private void OnMediaPropertyChanged(MediaManager.MediaSession session, GlobalSystemMediaTransportControlsSessionMediaProperties props) => AvailabilityChanged?.Invoke();

        // Recompute the cached availability/playing snapshot from the current Spotify session.
        // Runs on the worker (startup) or the MediaManager's callback threads — never the UI
        // thread — so the synchronous WinRT reads here are safe. UI reads the cached volatiles.
        private void RefreshCache()
        {
            try
            {
                var s = FindSpotify();
                _cachedAvailable = s != null;
                bool playing = false;
                if (s != null)
                {
                    try
                    {
                        playing = s.ControlSession?.GetPlaybackInfo()?.PlaybackStatus
                            == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    }
                    catch { playing = false; }
                }
                _cachedPlaying = playing;
            }
            catch (Exception ex) { _fileLogger?.Debug($"[Spotify] RefreshCache failed: {ex.Message}"); }
        }

        // Re-pull the Spotify session every call rather than caching — the library's
        // close events are unreliable, so we never trust a stale reference.
        private MediaManager.MediaSession FindSpotify()
        {
            if (!_started || _manager == null) return null;
            try
            {
                // Identify Spotify by a case-insensitive substring on the session id — works for
                // both the Win32 ("Spotify.exe") and Store ("SpotifyAB.SpotifyMusic_…!Spotify") builds.
                return _manager.CurrentMediaSessions.Values.FirstOrDefault(s =>
                    (s.Id ?? string.Empty).IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[Spotify] FindSpotify failed: {ex.Message}");
                return null;
            }
        }

        public bool IsAvailable => _cachedAvailable;

        public bool IsPlaying => _cachedPlaying;

        public bool TryPause()
        {
            _worker.PostControl(SpotifyControlIntent.Pause, _ => DoPause());
            return true; // accepted for dispatch (non-blocking)
        }

        public bool TryResume()
        {
            _worker.PostControl(SpotifyControlIntent.Play, _ => DoResume());
            return true;
        }

        public bool TrySkipNext() { _worker.PostRequest(DoSkipNext); return true; }
        public bool TrySkipPrevious() { _worker.PostRequest(DoSkipPrevious); return true; }
        public bool TryTogglePlayPause() { _worker.PostRequest(DoTogglePlayPause); return true; }

        // Runs an action on the dedicated Spotify worker thread (never the UI thread). Used by the
        // auto-launch flow so its Process.Start + poll never block the UI or the recompute lock.
        public void PostToWorker(System.Action work) => _worker.PostRequest(work);

        // The blocking SMTC bodies — now ONLY ever called on the worker thread.
        private void DoPause()
        {
            var s = FindSpotify(); if (s == null) return;
            try
            {
                var info = s.ControlSession?.GetPlaybackInfo();
                if (info?.Controls.IsPauseEnabled != true) return;
                s.ControlSession.TryPauseAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex) { _fileLogger?.Debug($"[Spotify] TryPause failed: {ex.Message}"); }
        }

        private void DoResume()
        {
            var s = FindSpotify(); if (s == null) return;
            try
            {
                var info = s.ControlSession?.GetPlaybackInfo();
                if (info?.Controls.IsPlayEnabled != true) return;
                s.ControlSession.TryPlayAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex) { _fileLogger?.Debug($"[Spotify] TryResume failed: {ex.Message}"); }
        }

        private void DoSkipNext()
        {
            var s = FindSpotify(); if (s == null) return;
            try
            {
                var info = s.ControlSession?.GetPlaybackInfo();
                if (info?.Controls.IsNextEnabled != true) return;
                s.ControlSession.TrySkipNextAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex) { _fileLogger?.Debug($"[Spotify] TrySkipNext failed: {ex.Message}"); }
        }

        private void DoSkipPrevious()
        {
            var s = FindSpotify(); if (s == null) return;
            try
            {
                var info = s.ControlSession?.GetPlaybackInfo();
                if (info?.Controls.IsPreviousEnabled != true) return;
                s.ControlSession.TrySkipPreviousAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex) { _fileLogger?.Debug($"[Spotify] TrySkipPrevious failed: {ex.Message}"); }
        }

        private void DoTogglePlayPause()
        {
            var s = FindSpotify(); if (s == null) return;
            try
            {
                // Resolve intent from current state and route to the capability-gated
                // play/pause commands rather than a blind TryTogglePlayPause: the raw
                // toggle can silently no-op while paused, which (via SpotifyControlService's
                // pause-hold state machine) could leave Spotify stuck unable to resume.
                var info = s.ControlSession?.GetPlaybackInfo();
                bool isPlaying = info?.PlaybackStatus
                    == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (isPlaying)
                {
                    if (info?.Controls.IsPauseEnabled == true)
                        s.ControlSession.TryPauseAsync().GetAwaiter().GetResult();
                }
                else
                {
                    if (info?.Controls.IsPlayEnabled == true)
                        s.ControlSession.TryPlayAsync().GetAwaiter().GetResult();
                    else
                        // Fallback: some sessions expose only the toggle while paused.
                        s.ControlSession.TryTogglePlayPauseAsync().GetAwaiter().GetResult();
                }
            }
            catch (Exception ex) { _fileLogger?.Debug($"[Spotify] TryTogglePlayPause failed: {ex.Message}"); }
        }

        // ISpotifyClient.GetNowPlaying stays for callers that already hold a background thread
        // (none should call it from the UI thread now). Prefer RequestNowPlaying from UI code.
        public SpotifyNowPlaying GetNowPlaying() => DoGetNowPlaying();

        public byte[] TryGetAlbumArtBytes() => DoGetAlbumArtBytes();

        // GetTimelineProperties() is a fast synchronous session read (unlike the async media-props
        // fetch), so this is safe to call inline from the snapshot path without the worker.
        public SpotifyTimeline GetTimeline()
        {
            var s = FindSpotify();
            if (s == null) return SpotifyTimeline.Empty;
            try
            {
                var tl = s.ControlSession?.GetTimelineProperties();
                if (tl == null) return SpotifyTimeline.Empty;

                var duration = tl.EndTime - tl.StartTime;
                if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

                // Position is reported relative to StartTime.
                var position = tl.Position - tl.StartTime;

                // SMTC only refreshes Position when Spotify pushes an update (track change, seek,
                // pause/play) — it does NOT tick between pushes, so a raw read is stale and the bar
                // moves in jumps. While playing, extrapolate: add the wall-clock elapsed since the
                // timeline was last updated. Zero extra SMTC calls — pure arithmetic. Paused tracks
                // are left frozen (correct). Clamped to [0, duration].
                if (_cachedPlaying)
                {
                    var elapsed = DateTimeOffset.UtcNow - tl.LastUpdatedTime;
                    if (elapsed > TimeSpan.Zero) position += elapsed;
                }

                if (position < TimeSpan.Zero) position = TimeSpan.Zero;
                if (duration > TimeSpan.Zero && position > duration) position = duration;

                return new SpotifyTimeline(position, duration);
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[Spotify] GetTimeline failed: {ex.Message}");
                return SpotifyTimeline.Empty;
            }
        }

        // Fetch now-playing OFF the UI thread, then invoke onResult ON the UI thread.
        public void RequestNowPlaying(Action<SpotifyNowPlaying> onResult)
        {
            if (onResult == null) return;
            _worker.PostRequest(() =>
            {
                var np = DoGetNowPlaying();
                MarshalToUi(() => onResult(np));
            });
        }

        public void RequestAlbumArt(Action<byte[]> onResult)
        {
            if (onResult == null) return;
            _worker.PostRequest(() =>
            {
                var bytes = DoGetAlbumArtBytes();
                MarshalToUi(() => onResult(bytes));
            });
        }

        private static void MarshalToUi(Action a)
        {
            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp != null) disp.BeginInvoke(a);
            else a(); // no dispatcher (e.g. unit test) — run inline
        }

        private SpotifyNowPlaying DoGetNowPlaying()
        {
            var s = FindSpotify();
            if (s == null) return SpotifyNowPlaying.Empty;
            try
            {
                var props = s.ControlSession?.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
                if (props == null) return SpotifyNowPlaying.Empty;
                if (string.Equals(props.Title, "Advertisement", StringComparison.OrdinalIgnoreCase))
                    return SpotifyNowPlaying.Empty;

                // SMTC exposes genres as a list; join for a single display string.
                var genre = (props.Genres != null && props.Genres.Count > 0)
                    ? string.Join(", ", props.Genres)
                    : string.Empty;

                // Total track length comes from the timeline (EndTime - StartTime), not media props.
                // Separate read, separately guarded — duration is optional, never fail the whole call.
                var duration = TimeSpan.Zero;
                try
                {
                    var tl = s.ControlSession?.GetTimelineProperties();
                    if (tl != null)
                    {
                        var len = tl.EndTime - tl.StartTime;
                        if (len > TimeSpan.Zero) duration = len;
                    }
                }
                catch (Exception ex)
                {
                    _fileLogger?.Debug($"[Spotify] timeline duration unavailable: {ex.Message}");
                }

                return new SpotifyNowPlaying(props.Title, props.Artist, props.AlbumTitle, genre, duration);
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[Spotify] GetNowPlaying failed: {ex.Message}");
                return SpotifyNowPlaying.Empty;
            }
        }

        private byte[] DoGetAlbumArtBytes()
        {
            var s = FindSpotify();
            if (s == null) return null;
            try
            {
                var props = s.ControlSession?.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
                var thumbRef = props?.Thumbnail;
                if (thumbRef == null) return null;

                using (var ras = thumbRef.OpenReadAsync().GetAwaiter().GetResult())
                {
                    if (ras == null || ras.Size == 0) return null;
                    using (var netStream = ras.AsStreamForRead())
                    using (var ms = new System.IO.MemoryStream())
                    {
                        netStream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[Spotify] TryGetAlbumArtBytes failed: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _manager?.Dispose(); }
            catch (Exception ex) { _fileLogger?.Debug($"[Spotify] Dispose failed: {ex.Message}"); }
            _manager = null;
            try { _worker?.Dispose(); } catch (Exception ex) { _fileLogger?.Debug($"[Spotify] worker dispose failed: {ex.Message}"); }
        }
    }
}
