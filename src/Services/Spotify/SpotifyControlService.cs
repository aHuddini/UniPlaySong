using System;
using System.Windows.Threading;
using UniPlaySong.Common;

namespace UniPlaySong.Services.Spotify
{
    // Policy layer ("the conductor"). Observes the music engine's audible state and
    // conducts the Spotify client to match, without modifying the engine. Computes
    // SpotifyActive (radio = always; default-source = only in a default-music gap),
    // and applies the "only resume what UPS paused" ownership discipline.
    public class SpotifyControlService : IDisposable
    {
        private readonly IMusicPlaybackService _playback;
        private readonly ISpotifyClient _client;
        private readonly Func<UniPlaySongSettings> _getSettings;
        private readonly FileLogger _fileLogger;

        // True only while UPS itself paused Spotify, so UPS resumes only its own pauses.
        private bool _pausedByUps;
        private bool _isActive;
        private bool _disposed;

        // Serializes Recompute() across the event (UI thread), AvailabilityChanged
        // (threadpool, from SMTC/WinRT callbacks), and the periodic timer.
        private readonly object _recomputeLock = new object();

        // Low-frequency safety refresh. The SMTC library's change events are unreliable
        // (a missed Spotify-close event can strand SpotifyActive=true), so re-poll periodically.
        private readonly DispatcherTimer _refreshTimer;

        public bool IsSpotifyActive => _isActive;

        // Raised whenever now-playing should be refreshed (active state or track may have changed).
        public event Action NowPlayingChanged;

        public SpotifyControlService(
            IMusicPlaybackService playbackService,
            ISpotifyClient spotifyClient,
            Func<UniPlaySongSettings> getSettings,
            FileLogger fileLogger)
        {
            _playback = playbackService;
            _client = spotifyClient;
            _getSettings = getSettings;
            _fileLogger = fileLogger;

            if (_playback != null) _playback.OnPlaybackStateChanged += Recompute;
            if (_client != null) _client.AvailabilityChanged += Recompute;

            // Spec-mandated periodic refresh: ticks on the UI thread (DispatcherTimer),
            // catching missed availability/close events. Recompute() is idempotent.
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
            _refreshTimer.Tick += (s, e) => Recompute();
            _refreshTimer.Start();
        }

        private bool ComputeActive(UniPlaySongSettings s)
        {
            if (s == null || _client == null || !_client.IsAvailable) return false;
            // Radio precedence: evaluated first, unconditionally.
            if (s.RadioModeEnabled && s.SpotifyRadioMode) return true;
            if (s.DefaultMusicSourceOption == DefaultMusicSource.Spotify
                && _playback?.IsPlayingDefaultMusic == true) return true;
            return false;
        }

        // Recomputes SpotifyActive and conducts Spotify. Safe to call repeatedly.
        // Locked because it's invoked from the playback event (UI thread),
        // AvailabilityChanged (threadpool), and the periodic timer.
        public void Recompute()
        {
            if (_disposed) return;

            bool raiseNowPlaying = false;
            lock (_recomputeLock)
            {
                if (_disposed) return;
                var s = _getSettings?.Invoke();
                bool active = ComputeActive(s);

                if (active != _isActive)
                {
                    _isActive = active;
                    if (s != null) s.SpotifyActive = active;
                    if (!active)
                    {
                        // Leaving Spotify mode: release any pause UPS owns.
                        ReleaseUpsPause();
                    }
                    raiseNowPlaying = true;
                }

                if (active)
                {
                    // Mirror UPS's audible state onto Spotify transport.
                    if (_playback?.IsPaused == true)
                    {
                        if (!_pausedByUps && _client.IsPlaying)
                        {
                            if (_client.TryPause()) _pausedByUps = true;
                        }
                    }
                    else
                    {
                        ReleaseUpsPause();
                    }
                    raiseNowPlaying = true;
                }
            }

            if (raiseNowPlaying) NowPlayingChanged?.Invoke();
        }

        // Resume Spotify only if UPS was the one who paused it.
        private void ReleaseUpsPause()
        {
            if (_pausedByUps)
            {
                _client.TryResume();
                _pausedByUps = false;
            }
        }

        public SpotifyNowPlaying GetNowPlaying()
            => _isActive ? (_client?.GetNowPlaying() ?? SpotifyNowPlaying.Empty) : SpotifyNowPlaying.Empty;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _refreshTimer?.Stop();
            if (_playback != null) _playback.OnPlaybackStateChanged -= Recompute;
            if (_client != null) _client.AvailabilityChanged -= Recompute;
        }
    }
}
