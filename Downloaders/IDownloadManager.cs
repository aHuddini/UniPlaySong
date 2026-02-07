using System.Collections.Generic;
using System.Threading;
using Playnite.SDK.Models;
using UniPlaySong.Models;

namespace UniPlaySong.Downloaders
{
    // Interface for managing music downloads from various sources
    public interface IDownloadManager
    {
        /// <summary>
        /// Gets albums for a game from a specific source
        /// </summary>
        /// <param name="gameName">Name of the game to search for</param>
        /// <param name="source">Source to search (KHInsider, YouTube, or All)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="auto">If true, applies stricter filtering for auto-download</param>
        /// <param name="skipCache">If true, bypasses the search cache for fresh results</param>
        IEnumerable<Album> GetAlbumsForGame(string gameName, Source source, CancellationToken cancellationToken, bool auto = false, bool skipCache = false);

        Album BestAlbumPick(IEnumerable<Album> albums, Game game);
        Album BestAlbumPickBroader(IEnumerable<Album> albums, Game game);

        /// <summary>
        /// Picks the best song(s) from a list based on preferences (theme, title, menu songs preferred)
        /// </summary>
        /// <param name="songs">List of songs to choose from</param>
        /// <param name="gameName">Game name for matching</param>
        /// <param name="maxSongs">Maximum number of songs to return (default 1)</param>
        /// <returns>List of best matching songs</returns>
        List<Song> BestSongPick(IEnumerable<Song> songs, string gameName, int maxSongs = 1);

        IEnumerable<Song> GetSongsFromAlbum(Album album, CancellationToken cancellationToken);

        /// <summary>
        /// Downloads a song to the specified path
        /// </summary>
        /// <param name="isPreview">If true, optimize for faster preview download (lower quality, shorter duration)</param>
        bool DownloadSong(Song song, string path, CancellationToken cancellationToken, bool isPreview = false);

        string GetTempPath(Song song);
        void Cleanup();

        /// <summary>
        /// Gets hint-based albums for a game (from search_hints.json).
        /// Returns albums created from YouTubePlaylistId and KHInsiderAlbum hints.
        /// </summary>
        /// <param name="gameName">Name of the game to check for hints</param>
        /// <returns>List of albums from hints, or empty list if no hints exist</returns>
        List<Album> GetHintAlbums(string gameName);
    }
}

