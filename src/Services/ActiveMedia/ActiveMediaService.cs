using System;
using UniPlaySong.Common;
using UniPlaySong.DeskMediaControl;
using UniPlaySong.Models;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.Services.ActiveMedia
{
    // Resolves the single audible active source and routes transport to it.
    // Spotify-active-wins precedence: reuses SpotifyControlService.IsSpotifyActive.
    // External source is Spotify-only in this build; the resolution seam is
    // ResolveSource() — widening to any SMTC app happens there later.
    public class ActiveMediaService : IActiveMediaService
    {
        private readonly IMusicPlaybackService _playback;
        private readonly SpotifyControlService _spotifyControl;
        private readonly ISpotifyClient _spotifyClient;
        private readonly FileLogger _fileLogger;

        // Player volume captured when UPS music was muted, so unmute restores that exact level
        // (which already includes MusicVolume + Calm Down attenuation). -1 = not currently muted.
        private double _upsVolumeBeforeMute = -1.0;

        public event Action Changed;

        public ActiveMediaService(
            IMusicPlaybackService playback,
            SpotifyControlService spotifyControl,
            ISpotifyClient spotifyClient,
            FileLogger fileLogger)
        {
            _playback = playback;
            _spotifyControl = spotifyControl;
            _spotifyClient = spotifyClient;
            _fileLogger = fileLogger;

            if (_spotifyControl != null) _spotifyControl.NowPlayingChanged += RaiseChanged;
            if (_spotifyClient != null) _spotifyClient.AvailabilityChanged += RaiseChanged;
            if (_playback != null)
            {
                // Pause/resume/mute/volume transitions.
                _playback.OnPlaybackStateChanged += RaiseChanged;
                // Game-music song load/switch: OnPlaybackStateChanged does NOT fire for a
                // plain song change, so without these the snapshot (and HasActiveMedia,
                // which gates element visibility) would never refresh for game music —
                // elements would stay collapsed/stale. Spotify already refreshes via
                // NowPlayingChanged; these give UPS's own player the equivalent signal.
                _playback.OnMusicStarted += OnMusicStarted;
                _playback.OnSongChanged += OnSongChanged;
            }
        }

        private void OnMusicStarted(UniPlaySongSettings settings) => RaiseChanged();
        private void OnSongChanged(string songPath) => RaiseChanged();

        private void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { _fileLogger?.Debug($"[ActiveMedia] Changed handler threw: {ex.Message}"); }
        }

        // The resolution seam. Spotify-only external source for now.
        private ActiveMediaSourceKind ResolveSource()
        {
            if (_spotifyControl?.IsSpotifyActive == true) return ActiveMediaSourceKind.Spotify;
            if (_playback?.IsLoaded == true) return ActiveMediaSourceKind.Ups;
            return ActiveMediaSourceKind.None;
        }

        public ActiveMediaSnapshot GetSnapshot()
        {
            try
            {
                var kind = ResolveSource();
                if (kind == ActiveMediaSourceKind.Spotify) return BuildSpotifySnapshot();
                if (kind == ActiveMediaSourceKind.Ups) return BuildUpsSnapshot();
                return ActiveMediaSnapshot.Empty;
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[ActiveMedia] GetSnapshot failed: {ex.Message}");
                return ActiveMediaSnapshot.Empty;
            }
        }

        private ActiveMediaSnapshot BuildSpotifySnapshot()
        {
            bool playing = _spotifyClient?.IsPlaying ?? false;

            // Timeline from SMTC (position + duration). Position is a stepped read, not a smooth
            // per-second tick — Spotify updates it periodically — so the progress bar advances in
            // jumps. Blank when the timeline is unavailable.
            var tl = _spotifyClient?.GetTimeline() ?? SpotifyTimeline.Empty;
            double progress = 0.0;
            string posText = string.Empty, durText = string.Empty;
            if (tl.HasDuration)
            {
                progress = Math.Max(0.0, Math.Min(100.0,
                    tl.Position.TotalSeconds / tl.Duration.TotalSeconds * 100.0));
                posText = SongTitleCleaner.FormatDuration(tl.Position);
                durText = SongTitleCleaner.FormatDuration(tl.Duration);
            }

            return new ActiveMediaSnapshot(
                hasActiveMedia: true,
                sourceKind: ActiveMediaSourceKind.Spotify,
                sourceName: "Spotify",
                isPlaying: playing,
                isMuted: false,
                progress: progress,
                positionText: posText,
                durationText: durText,
                volume: 0.0,
                canNext: true,
                canPrevious: true);
        }

        private ActiveMediaSnapshot BuildUpsSnapshot()
        {
            var cur = _playback?.GetCurrentSongCurrentTime();
            var total = _playback?.GetCurrentSongTotalTime();
            double progress = 0.0;
            string posText = string.Empty, durText = string.Empty;
            if (cur.HasValue && total.HasValue && total.Value.TotalSeconds > 0)
            {
                progress = Math.Max(0.0, Math.Min(100.0,
                    cur.Value.TotalSeconds / total.Value.TotalSeconds * 100.0));
                posText = SongTitleCleaner.FormatDuration(cur.Value);
                durText = SongTitleCleaner.FormatDuration(total.Value);
            }
            double vol = (_playback?.GetInternalVolume() ?? 0.0) * 100.0;
            bool canNext = (_playback?.CurrentGameSongCount ?? 0) >= 2
                           || _playback?.IsPlayingPoolBasedDefault == true;
            return new ActiveMediaSnapshot(
                hasActiveMedia: true,
                sourceKind: ActiveMediaSourceKind.Ups,
                sourceName: "UniPlaySong",
                // IsPlaying alone is the raw backend "stream active" flag, which stays true when
                // UPS is logically paused (pause sources + fader ride volume to 0 but the NAudio
                // persistent mixer keeps the stream active). That left theme play/pause icons stuck
                // on the "pause" glyph after pausing UPS game music. Gate on the logical pause state
                // so ActiveMediaIsPlaying reflects an actual pause across every UPS pause path.
                isPlaying: (_playback?.IsPlaying ?? false) && !(_playback?.IsPaused ?? false),
                isMuted: vol <= 0.0,
                progress: progress,
                positionText: posText,
                durationText: durText,
                volume: vol,
                canNext: canNext,
                canPrevious: true);
        }

        // Spotify transport MUST route through SpotifyControlService, not the raw client:
        // SpotifyControlService owns the two-flag pause/ownership state machine (radio
        // engage/disengage + "respect a pause made in the app"). Calling _spotifyClient
        // directly bypasses that bookkeeping, which left UserPausedExternally stuck true
        // and Spotify unable to resume until an app restart. UPS's own player has no such
        // shared state, so it's driven directly.
        public void PlayPause()
        {
            if (ResolveSource() == ActiveMediaSourceKind.Spotify) _spotifyControl?.ToggleManualPlayPause();
            else _playback?.TogglePlayPauseInternal();
            RaiseChanged();
        }

        public void Next()
        {
            if (ResolveSource() == ActiveMediaSourceKind.Spotify) _spotifyControl?.SkipNext();
            else _playback?.SkipToNextSong();
            RaiseChanged();
        }

        public void Previous()
        {
            if (ResolveSource() == ActiveMediaSourceKind.Spotify) _spotifyControl?.SkipPrevious();
            else _playback?.RestartCurrentSong();
            RaiseChanged();
        }

        public void ToggleMute()
        {
            // UPS-only: Spotify's own volume is owned by Windows/SMTC, not UPS — out of scope
            // here (the client contract has no mute command). This mutes ONLY UPS radio/game music.
            if (ResolveSource() != ActiveMediaSourceKind.Ups) return;

            double v = _playback?.GetInternalVolume() ?? 0.0;
            if (v > 0.0)
            {
                // Muting: remember the exact live level (bakes in MusicVolume + Calm Down), silence.
                _upsVolumeBeforeMute = v;
                _playback?.SetInternalVolume(0.0);
            }
            else
            {
                // Unmuting: restore the saved level. Fall back to the target volume (GetVolume,
                // = _targetVolume) if we have no saved value — NEVER a hardcoded 1.0, which was
                // the "way louder / wipes Calm Down" bug. First press when already silent also
                // lands here and restores a sane level instead of blasting to 100%.
                double restore = _upsVolumeBeforeMute > 0.0
                    ? _upsVolumeBeforeMute
                    : (_playback?.GetVolume() ?? 0.0);
                _playback?.SetInternalVolume(restore);
                _upsVolumeBeforeMute = -1.0;
            }
            _fileLogger?.Debug($"[ActiveMedia] ToggleMute: {(v > 0.0 ? "muted" : "unmuted")} (was={v:F3}, saved={_upsVolumeBeforeMute:F3})");
            RaiseChanged();
        }

        public void SetVolume(double volume0to100)
        {
            if (ResolveSource() == ActiveMediaSourceKind.Ups)
            {
                double clamped = Math.Max(0.0, Math.Min(100.0, volume0to100));
                _playback?.SetInternalVolume(clamped / 100.0);
                // An explicit volume set clears any mute memory: the new level IS the level.
                _upsVolumeBeforeMute = -1.0;
                RaiseChanged();
            }
            // Spotify app volume not settable via the SMTC client contract — ignored.
        }

        public void Poll()
        {
            // Position advances without an event while playing; re-publish the snapshot.
            RaiseChanged();
        }
    }
}
