using System;
using System.Windows.Threading;
using UniPlaySong.Common;

namespace UniPlaySong.Services.Spotify
{
    // Policy layer ("the conductor"). Observes the music engine's audible state and
    // conducts the Spotify client to match, without modifying the engine. Computes
    // SpotifyActive (radio = always; default-source = only in a default-music gap).
    // When Spotify is the active music UPS drives it: PLAY when active and not
    // lifecycle-paused; PAUSE when a game with its own music takes over, the mode turns
    // off, or a lifecycle pause (game launch, video, focus loss, lock) is in effect.
    public class SpotifyControlService : IDisposable
    {
        private readonly IMusicPlaybackService _playback;
        private readonly ISpotifyClient _client;
        private readonly Func<UniPlaySongSettings> _getSettings;
        private readonly FileLogger _fileLogger;

        // True once UPS has taken control of Spotify playback (the first time Spotify needs
        // to play for UPS's purposes). While true, UPS drives Spotify play/pause to match
        // "is Spotify the active music + not lifecycle-paused". Released when Spotify stops
        // being the active music, so the user regains free control until UPS next needs it.
        private bool _drivingSpotify;
        private bool _isActive;
        private bool _disposed;
        private SpotifyRadioState _radioState;
        private bool _radioWasOn; // edge-detect radio engage

        // User pressed Pause via the menu while Spotify was the active music. While this holds,
        // UPS goes hands-off (issues no play/pause commands) so the user's pause sticks instead of
        // being instantly auto-resumed. Cleared when the user toggles back to play, or when Spotify
        // stops being the active music (a fresh takeover should start playing again).
        private bool _manualPauseHold;

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
            // Spotify Radio Mode: standalone continuous source (v1.5.8 — no longer gated on
            // RadioModeEnabled, which caused UPS's pool radio to start alongside Spotify). It is
            // mutually exclusive with RadioModeEnabled (enforced in settings), so when this is on,
            // MusicPlaybackService's RadioMode branch never fires — Spotify alone is the source.
            if (s.SpotifyRadioMode) return true;
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

                // ---- RADIO PATH (v1.5.8 rebuild): minimal two-flag state machine, off-thread. ----
                if (s != null && s.SpotifyRadioMode)
                {
                    bool radioActive = _client != null && _client.IsAvailable;
                    if (radioActive != _isActive)
                    {
                        _isActive = radioActive;
                        s.SpotifyActive = radioActive;
                        raiseNowPlaying = true;
                    }

                    // Engage edge: radio just turned on → seed "UPS owns the pause" so the first
                    // decision issues a single Resume to start the radio (unless the user has Spotify
                    // paused, in which case the decision respects it).
                    if (!_radioWasOn)
                    {
                        _radioWasOn = true;
                        _radioState.LifecyclePausedByUps = true;
                        _radioState.UserPausedExternally = false;
                    }

                    if (radioActive)
                    {
                        bool lifecyclePaused = _playback?.IsPaused == true;
                        bool spotifyPlaying = _client.IsPlaying;
                        var (action, next) = SpotifyRadioDecision.Decide(
                            radioOn: true, lifecyclePaused: lifecyclePaused,
                            spotifyIsPlaying: spotifyPlaying, prev: _radioState);
                        _radioState = next;
                        if (action == SpotifyRadioAction.Play) _client.TryResume();   // non-blocking post
                        else if (action == SpotifyRadioAction.Pause) _client.TryPause();
                    }
                    raiseNowPlaying = raiseNowPlaying || radioActive;

                    if (raiseNowPlaying) NowPlayingChanged?.Invoke();
                    return; // radio handled — do NOT run the gap-fill machinery
                }

                // Radio just turned off: reset the engage edge + radio flags.
                if (_radioWasOn)
                {
                    _radioWasOn = false;
                    _radioState = default(SpotifyRadioState);
                }

                // ---- GAP-FILL PATH (unchanged) ----
                bool active = ComputeActive(s);
                bool wantSpotifyPlaying = active && _playback?.IsPaused != true;
                bool enteringActive = active && !_isActive;

                if (active != _isActive)
                {
                    _isActive = active;
                    if (s != null) s.SpotifyActive = active;
                    raiseNowPlaying = true;
                }

                if (_manualPauseHold && active)
                {
                    // Hands off — issue no command this tick.
                }
                else if (wantSpotifyPlaying)
                {
                    if (enteringActive && !_drivingSpotify && s?.SpotifySkipOnGap == true)
                    {
                        bool skipped = _client?.TrySkipNext() ?? false;
                        if (!skipped && _client?.IsPlaying != true) _client?.TryResume();
                    }
                    else if (_client?.IsPlaying != true)
                    {
                        _client?.TryResume();
                    }
                    _drivingSpotify = true;
                }
                else if (_drivingSpotify)
                {
                    _client?.TryPause();
                    if (!active) { _drivingSpotify = false; _manualPauseHold = false; }
                }

                if (active) raiseNowPlaying = true;
            }

            if (raiseNowPlaying) NowPlayingChanged?.Invoke();
        }


        public SpotifyNowPlaying GetNowPlaying()
            => _isActive ? (_client?.GetNowPlaying() ?? SpotifyNowPlaying.Empty) : SpotifyNowPlaying.Empty;

        // Manual Play/Pause from the menu. Routes through the SERVICE (not the client directly) so
        // we can manage the manual-pause hold: pausing while Spotify is the active music would
        // otherwise be auto-resumed on the next recompute. Toggling to PAUSE sets the hold (UPS
        // goes hands-off); toggling back to PLAY clears it and resumes. When Spotify is NOT the
        // active music, there's no hold to manage — just toggle.
        public void ToggleManualPlayPause()
        {
            if (_disposed || _client == null) return;
            lock (_recomputeLock)
            {
                bool wasPlaying = _client.IsPlaying;
                _client.TryTogglePlayPause();
                // If it was playing we just paused it → hold (only meaningful while Spotify is the
                // active music; harmless otherwise since the hold is checked alongside `active`).
                // If it was paused we just resumed → release any hold.
                _manualPauseHold = wasPlaying && _isActive;
            }
        }

        // Manual Skip from the menu. Skipping implies "play this track," so it clears any manual
        // pause hold. Routes through the service for the same single-source-of-truth reason.
        public void SkipNext()
        {
            if (_disposed || _client == null) return;
            lock (_recomputeLock)
            {
                _manualPauseHold = false;
                _client.TrySkipNext();
            }
        }

        public void SkipPrevious()
        {
            if (_disposed || _client == null) return;
            lock (_recomputeLock)
            {
                _manualPauseHold = false;
                _client.TrySkipPrevious();
            }
        }

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
