using System.Collections.Generic;
using System.Threading;
using Playnite.SDK.Models;
using UniPlaySong.Models;

namespace UniPlaySong.Downloaders
{
    // Interface for managing music downloads from various sources
    public interface IDownloadManager
    {
        // auto applies the stricter auto-download filtering; skipCache bypasses the search cache.
        IEnumerable<Album> GetAlbumsForGame(string gameName, Source source, CancellationToken cancellationToken, bool auto = false, bool skipCache = false);

        Album BestAlbumPick(IEnumerable<Album> albums, Game game);
        Album BestAlbumPickBroader(IEnumerable<Album> albums, Game game);

        // Prefers theme, title and menu songs.
        List<Song> BestSongPick(IEnumerable<Song> songs, string gameName, int maxSongs = 1);

        IEnumerable<Song> GetSongsFromAlbum(Album album, CancellationToken cancellationToken);

        // isPreview trades quality and length for a faster download.
        bool DownloadSong(Song song, string path, CancellationToken cancellationToken, bool isPreview = false);

        string GetTempPath(Song song);
        void Cleanup();

        // Albums built from the game's YouTubePlaylistId / KHInsiderAlbum entries in search_hints.json; empty if none.
        List<Album> GetHintAlbums(string gameName);

        // Live-updates the YouTube + SoundCloud downloader configs without replacing this IDownloadManager instance.
        // Avoids stale-reference bugs in downstream services (DownloadDialogService, ControllerDialogHandler) that
        // capture _downloadManager at construction time.
        void UpdateSettings(string ytDlpPath, string ffmpegPath, CookieMode cookieMode, string customCookiesFilePath);
    }
}

