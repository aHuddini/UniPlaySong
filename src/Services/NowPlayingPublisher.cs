using System;
using System.Windows;
using System.Windows.Threading;
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
        private readonly Func<string, string> _getGameCoverArtPath;
        private readonly FileLogger _fileLogger;
        private readonly object _publishLock = new object();
        private bool _disposed;

        // "Track just changed" pulse: remember the last published identity so a re-publish of the
        // same track (Spotify re-publishes every couple seconds) does NOT re-fire, and a one-shot
        // UI-thread timer that flips IsMusicChanged back to false shortly after each real change.
        private string _lastPublishedKey;
        private DispatcherTimer _musicChangedResetTimer;
        // Short by design: only the true→false edge matters (a theme runs its own visible-duration
        // timer). Long enough that the true state is observed, short enough to re-arm quickly.
        private static readonly TimeSpan MusicChangedPulse = TimeSpan.FromMilliseconds(750);

        // getGameCoverArtPath: takes the playing track's file path (null for Spotify) and returns a
        // game cover-art file path (or null/""), used as a fallback when the track has no embedded
        // art or Spotify exposes no album art. The resolver prefers the game that OWNS the track
        // (parsed from its ...\Games\{GameId}\ path — so pool/radio songs get THEIR game's cover),
        // then the selected game. Resolved at the composition root (where the Playnite API lives) so
        // the publisher stays decoupled. Optional — null disables the fallback (♪ placeholder shows).
        public NowPlayingPublisher(
            SongMetadataService metadata,
            SpotifyControlService spotify,
            ISpotifyClient spotifyClient,
            NowPlayingArtWriter artWriter,
            Func<UniPlaySongSettings> getSettings,
            FileLogger fileLogger,
            Func<string, string> getGameCoverArtPath = null)
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
                            // Dedupe BEFORE fetching album art: Spotify's SMTC re-fires on every
                            // position tick (many/sec), and the thumbnail fetch is a COM stream
                            // open+copy each time. Same track as last publish -> skip it entirely.
                            // (Read-only check here; _lastPublishedKey is only WRITTEN after a real
                            // publish below, so a failed/raced fetch can't wedge the dedup.)
                            {
                                var t = np.IsEmpty ? string.Empty : (np.Title ?? string.Empty);
                                var a = np.IsEmpty ? string.Empty : (np.Artist ?? string.Empty);
                                var al = np.IsEmpty ? string.Empty : (np.Album ?? string.Empty);
                                if ("spotify\n" + t + "\n" + a + "\n" + al == _lastPublishedKey) return;
                            }
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

                                    // Dedupe by track identity. Spotify's SMTC re-fires NowPlayingChanged
                                    // on every position/state tick (many times per second), and without
                                    // this each tick wrote a fresh album-art PNG to disk and republished a
                                    // new path — hammering disk I/O and forcing the theme's now-playing
                                    // <Image> to decode/reload constantly, which visibly slowed the theme.
                                    // Same track since last publish => nothing to do.
                                    var key = "spotify\n" + title + "\n" + artist + "\n" + album;
                                    if (key == _lastPublishedKey) return;
                                    _lastPublishedKey = key;

                                    var artPath = _artWriter?.WriteBytes(artBytes) ?? string.Empty;
                                    if (string.IsNullOrEmpty(artPath))
                                    {
                                        // No Spotify album art — fall back to the selected game's cover
                                        // so the now-playing slot isn't empty (same fallback as game music).
                                        // Null path: a Spotify track has no owning-game folder.
                                        var cover = TryGetGameCoverPath(null);
                                        if (!string.IsNullOrEmpty(cover))
                                        {
                                            _artWriter?.Clear(); // point at the cover directly
                                            artPath = cover;
                                        }
                                    }
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
                            // No embedded track art — fall back to a game cover: the track's OWNING
                            // game (parsed from its Games\{GameId}\ path — right cover for pool/radio
                            // songs, and works when CurrentGame is null), else the selected game.
                            var cover = TryGetGameCoverPath(song.FilePath);
                            _fileLogger?.Debug($"[NowPlaying] UPS art: embedded=none, file='{song.FilePath}', coverFallback='{cover}'");
                            if (!string.IsNullOrEmpty(cover))
                            {
                                _artWriter?.Clear(); // drop any stale written art; we point at the cover directly
                                artPath = cover;
                            }
                        }
                        else
                        {
                            _fileLogger?.Debug($"[NowPlaying] UPS art: embedded='{artPath}'");
                        }
                        if (string.IsNullOrEmpty(artPath))
                            _fileLogger?.Debug("[NowPlaying] UPS art: FINAL empty (no embedded art AND no game cover resolved)");
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

        // Resolve a game cover for the given track path (null for Spotify) via the injected resolver.
        // Returns "" when no resolver, no game/cover, or the file is missing. Fail-safe (never throws).
        private string TryGetGameCoverPath(string songFilePath)
        {
            if (_getGameCoverArtPath == null) return string.Empty;
            try
            {
                var path = _getGameCoverArtPath(songFilePath);
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
            _fileLogger?.Debug($"[NowPlaying] Publish: title='{title}', artPath='{artPath}'");
            s.NowPlayingTitle = title;
            s.NowPlayingArtist = artist;
            s.NowPlayingAlbumArtPath = artPath;
            s.NowPlayingAlbum = album;
            s.NowPlayingGenre = genre;
            s.NowPlayingDuration = duration;

            // Pulse IsMusicChanged only when the track identity actually changed (not on a re-publish
            // of the same song). Identity = title + U+0001 + artist (a control char that cannot occur in
            // real metadata, so "A"+"" stays distinct from ""+"A". Start and stop both count.
            var key = (title ?? string.Empty) + "" + (artist ?? string.Empty);
            bool bothEmpty = string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist);
            bool firstEmptyAtStartup = _lastPublishedKey == null && bothEmpty;
            bool changed = key != _lastPublishedKey && !firstEmptyAtStartup;
            _lastPublishedKey = key;
            if (changed) PulseMusicChanged(s);
        }

        // Flip IsMusicChanged true now, then back to false after a short delay. The reset runs on a
        // UI-thread DispatcherTimer so the property change is raised on the UI thread (this method may
        // be called from an off-thread Spotify callback); both the initial set and the timer arming are
        // marshalled via the dispatcher. Re-arming restarts the window so rapid changes still end false.
        private void PulseMusicChanged(UniPlaySongSettings s)
        {
            if (s == null) return;
            OnUi(() =>
            {
                if (_disposed) return;
                s.IsMusicChanged = true;

                if (_musicChangedResetTimer == null)
                {
                    _musicChangedResetTimer = new DispatcherTimer { Interval = MusicChangedPulse };
                    _musicChangedResetTimer.Tick += (sender, e) =>
                    {
                        _musicChangedResetTimer.Stop();
                        var cur = _getSettings?.Invoke();
                        if (cur != null) cur.IsMusicChanged = false;
                    };
                }
                _musicChangedResetTimer.Stop();   // restart the window on each change
                _musicChangedResetTimer.Start();
            });
        }

        // Marshal to the UI thread (BeginInvoke, never sync Invoke — the established deadlock-fix rule).
        private static void OnUi(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d != null && !d.CheckAccess()) d.BeginInvoke(a);
            else a();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_metadata != null) _metadata.OnSongInfoChanged -= OnUpsSongInfoChanged;
            if (_spotify != null) _spotify.NowPlayingChanged -= Refresh;
            try { _musicChangedResetTimer?.Stop(); } catch { }
            _musicChangedResetTimer = null;
        }
    }
}
