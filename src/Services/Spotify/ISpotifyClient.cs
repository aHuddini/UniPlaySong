using System;

namespace UniPlaySong.Services.Spotify
{
    // Mechanism contract for controlling the Spotify desktop app via the OS.
    // Knows nothing about UniPlaySong; every method is fail-safe (never throws).
    public interface ISpotifyClient
    {
        // True when a controllable Spotify session is present (app running, SMTC available).
        bool IsAvailable { get; }

        // True when Spotify is currently playing (PlaybackStatus == Playing).
        bool IsPlaying { get; }

        // Pause Spotify. Returns true if the command was accepted. No-op (returns false)
        // when unavailable or pause is not currently enabled.
        bool TryPause();

        // Resume Spotify. Returns true if accepted. No-op (false) when unavailable or
        // play is not currently enabled.
        bool TryResume();

        // Skip to the next track. Returns true if accepted. No-op (false) when unavailable
        // or next is not currently enabled (e.g. end of queue with no autoplay). On Spotify
        // this also starts playback if paused.
        bool TrySkipNext();

        // Skip to the previous track. Returns true if accepted. No-op (false) when unavailable
        // or previous is not currently enabled.
        bool TrySkipPrevious();

        // Toggle play/pause. Returns true if accepted. No-op (false) when unavailable.
        bool TryTogglePlayPause();

        // Current track metadata, or SpotifyNowPlaying.Empty when unavailable.
        SpotifyNowPlaying GetNowPlaying();

        // Current track's album-art bytes from the SMTC thumbnail, or null when unavailable /
        // no thumbnail / failure. Fail-safe (never throws).
        byte[] TryGetAlbumArtBytes();

        // Fetch the current track OFF the UI thread; onResult is invoked on the UI thread.
        void RequestNowPlaying(Action<SpotifyNowPlaying> onResult);

        // Fetch album-art bytes OFF the UI thread; onResult is invoked on the UI thread.
        void RequestAlbumArt(Action<byte[]> onResult);

        // Raised when Spotify becomes available or unavailable (session opened/closed),
        // or its playback state changes, so the policy layer can recompute.
        event Action AvailabilityChanged;
    }
}
