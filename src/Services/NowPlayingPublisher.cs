using System;
using UniPlaySong.Common;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.Services
{
    // Publishes live now-playing data (title, artist, album-art path) onto UniPlaySongSettings so
    // theme devs can bind it via {PluginSettings}. A thin coordination layer (like SpotifyControlService):
    // observes existing now-playing events, resolves the active source, writes the art file, and sets
    // the three settings properties. Owns nothing else.
    public class NowPlayingPublisher : IDisposable
    {
        private readonly SongMetadataService _metadata;
        private readonly SpotifyControlService _spotify;
        private readonly ISpotifyClient _spotifyClient;
        private readonly NowPlayingArtWriter _artWriter;
        private readonly Func<UniPlaySongSettings> _getSettings;
        private readonly Func<string> _getGameCoverArtPath;
        private readonly FileLogger _fileLogger;
        private readonly object _publishLock = new object();
        private bool _disposed;

        // getGameCoverArtPath: returns the current game's cover-art file path (or null/""), used as
        // a fallback for GAME MUSIC when the track has no embedded art. Resolved at the composition
        // root (where the Playnite API lives) so the publisher stays decoupled from it. Optional —
        // pass null to disable the fallback (the ♪ placeholder shows instead). Spotify never uses it.
        public NowPlayingPublisher(
            SongMetadataService metadata,
            SpotifyControlService spotify,
            ISpotifyClient spotifyClient,
            NowPlayingArtWriter artWriter,
            Func<UniPlaySongSettings> getSettings,
            FileLogger fileLogger,
            Func<string> getGameCoverArtPath = null)
        {
            _metadata = metadata;
            _spotify = spotify;
            _spotifyClient = spotifyClient;
            _artWriter = artWriter;
            _getSettings = getSettings;
            _getGameCoverArtPath = getGameCoverArtPath;
            _fileLogger = fileLogger;

            if (_metadata != null) _metadata.OnSongInfoChanged += OnUpsSongInfoChanged;
            if (_spotify != null) _spotify.NowPlayingChanged += Refresh;
        }

        private void OnUpsSongInfoChanged(SongInfo info) => Refresh();

        // Recompute the active source and publish title/artist/art onto the settings object.
        // Idempotent; safe to call from any thread (events arrive on UI thread + threadpool).
        public void Refresh()
        {
            if (_disposed) return;
            lock (_publishLock)
            {
                if (_disposed) return;
                var s = _getSettings?.Invoke();
                if (s == null) return;

                try
                {
                    if (_spotify != null && _spotify.IsSpotifyActive)
                    {
                        // Fetch Spotify metadata OFF the UI thread (worker); publish when it lands.
                        // Two chained requests: now-playing then album art. The final callback
                        // re-acquires the publish lock (Refresh's lock was released on return).
                        _spotifyClient?.RequestNowPlaying(np =>
                        {
                            if (_disposed) return;
                            _spotifyClient?.RequestAlbumArt(artBytes =>
                            {
                                if (_disposed) return;
                                lock (_publishLock)
                                {
                                    if (_disposed) return;
                                    var s2 = _getSettings?.Invoke();
                                    if (s2 == null) return;
                                    // Only publish if Spotify is still the active source (state may
                                    // have changed while the async fetch was in flight).
                                    if (_spotify == null || !_spotify.IsSpotifyActive)
                                    {
                                        return;
                                    }
                                    var title = np.IsEmpty ? string.Empty : (np.Title ?? string.Empty);
                                    var artist = np.IsEmpty ? string.Empty : (np.Artist ?? string.Empty);
                                    var album = np.IsEmpty ? string.Empty : (np.Album ?? string.Empty);
                                    var genre = np.IsEmpty ? string.Empty : (np.Genre ?? string.Empty);
                                    var duration = (np.IsEmpty || np.Duration <= TimeSpan.Zero)
                                        ? string.Empty
                                        : DeskMediaControl.SongTitleCleaner.FormatDuration(np.Duration);
                                    var artPath = _artWriter?.WriteBytes(artBytes) ?? string.Empty;
                                    Publish(s2, title, artist, artPath, album, genre, duration);
                                }
                            });
                        });
                        return; // publish happens in the async callbacks above
                    }

                    var song = _metadata?.CurrentSongInfo;
                    if (song != null && !song.IsEmpty)
                    {
                        var artPath = _artWriter?.WriteFromAudioFile(song.FilePath) ?? string.Empty;
                        if (string.IsNullOrEmpty(artPath))
                        {
                            // No embedded track art — fall back to the game's cover art (game music only).
                            var cover = TryGetGameCoverPath();
                            if (!string.IsNullOrEmpty(cover))
                            {
                                _artWriter?.Clear(); // drop any stale written art; we point at the cover directly
                                artPath = cover;
                            }
                        }
                        // Expose UPS song duration too (like Spotify) when the track carries it.
                        var upsDuration = song.HasDuration ? song.DurationText : string.Empty;
                        Publish(s, song.Title ?? string.Empty, song.Artist ?? string.Empty, artPath,
                            duration: upsDuration);
                        return;
                    }

                    // Nothing is the active music.
                    _artWriter?.Clear();
                    Publish(s, string.Empty, string.Empty, string.Empty);
                }
                catch (Exception ex)
                {
                    _fileLogger?.Debug($"[NowPlaying] Refresh failed: {ex.Message}");
                }
            }
        }

        // Resolve the current game's cover-art file via the injected resolver. Returns "" when no
        // resolver, no game/cover, or the file is missing. Fail-safe (never throws).
        private string TryGetGameCoverPath()
        {
            if (_getGameCoverArtPath == null) return string.Empty;
            try
            {
                var path = _getGameCoverArtPath();
                return (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path)) ? path : string.Empty;
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"[NowPlaying] game-cover fallback failed: {ex.Message}");
                return string.Empty;
            }
        }

        // album/genre are Spotify-only; duration is populated for both Spotify AND UPS game music.
        // The nothing-playing call site omits them (default ""), clearing any stale prior values.
        private void Publish(UniPlaySongSettings s, string title, string artist, string artPath,
            string album = "", string genre = "", string duration = "")
        {
            s.NowPlayingTitle = title;
            s.NowPlayingArtist = artist;
            s.NowPlayingAlbumArtPath = artPath;
            s.NowPlayingAlbum = album;
            s.NowPlayingGenre = genre;
            s.NowPlayingDuration = duration;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_metadata != null) _metadata.OnSongInfoChanged -= OnUpsSongInfoChanged;
            if (_spotify != null) _spotify.NowPlayingChanged -= Refresh;
        }
    }
}
