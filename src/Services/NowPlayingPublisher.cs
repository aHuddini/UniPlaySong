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
                        var np = _spotify.GetNowPlaying();
                        var title = np.IsEmpty ? string.Empty : (np.Title ?? string.Empty);
                        var artist = np.IsEmpty ? string.Empty : (np.Artist ?? string.Empty);
                        var album = np.IsEmpty ? string.Empty : (np.Album ?? string.Empty);
                        var genre = np.IsEmpty ? string.Empty : (np.Genre ?? string.Empty);
                        // Preformat duration to "m:ss" here so themes/the card bind a ready string; "" when unknown.
                        var duration = (np.IsEmpty || np.Duration <= TimeSpan.Zero)
                            ? string.Empty
                            : DeskMediaControl.SongTitleCleaner.FormatDuration(np.Duration);
                        var artPath = _artWriter?.WriteBytes(_spotifyClient?.TryGetAlbumArtBytes()) ?? string.Empty;
                        Publish(s, title, artist, artPath, album, genre, duration);
                        return;
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
                        Publish(s, song.Title ?? string.Empty, song.Artist ?? string.Empty, artPath);
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

        // album/genre/duration are Spotify-only; the game-music and nothing-playing call sites omit
        // them (default ""), which clears any stale values from a prior Spotify track.
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
