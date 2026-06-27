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
        private readonly FileLogger _fileLogger;
        private readonly object _publishLock = new object();
        private bool _disposed;

        public NowPlayingPublisher(
            SongMetadataService metadata,
            SpotifyControlService spotify,
            ISpotifyClient spotifyClient,
            NowPlayingArtWriter artWriter,
            Func<UniPlaySongSettings> getSettings,
            FileLogger fileLogger)
        {
            _metadata = metadata;
            _spotify = spotify;
            _spotifyClient = spotifyClient;
            _artWriter = artWriter;
            _getSettings = getSettings;
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
                        var artPath = _artWriter?.WriteBytes(_spotifyClient?.TryGetAlbumArtBytes()) ?? string.Empty;
                        Publish(s, title, artist, artPath);
                        return;
                    }

                    var song = _metadata?.CurrentSongInfo;
                    if (song != null && !song.IsEmpty)
                    {
                        var artPath = _artWriter?.WriteFromAudioFile(song.FilePath) ?? string.Empty;
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

        private void Publish(UniPlaySongSettings s, string title, string artist, string artPath)
        {
            s.NowPlayingTitle = title;
            s.NowPlayingArtist = artist;
            // Force the {PluginSettings} binding to re-read even when the path text is unchanged
            // across tracks (same file): clear first, then set. WPF reloads the image on the change.
            if (s.NowPlayingAlbumArtPath == artPath && artPath.Length > 0)
            {
                s.NowPlayingAlbumArtPath = string.Empty;
            }
            s.NowPlayingAlbumArtPath = artPath;
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
