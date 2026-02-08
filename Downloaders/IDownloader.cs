using System.Collections.Generic;
using System.Threading;
using UniPlaySong.Models;

namespace UniPlaySong.Downloaders
{
    // Interface for music download sources (KHInsider, YouTube, etc.)
    public interface IDownloader
    {
        string BaseUrl();
        Source DownloadSource();

        /// <summary>
        /// Searches for albums/soundtracks for a given game name
        /// </summary>
        /// <param name="gameName">Name of the game to search for</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="auto">Whether this is an automatic search</param>
        /// <returns>Collection of albums found</returns>
        IEnumerable<Album> GetAlbumsForGame(string gameName, CancellationToken cancellationToken, bool auto = false);

        /// <summary>
        /// Gets all songs from an album
        /// </summary>
        /// <param name="album">Album to get songs from</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Collection of songs in the album</returns>
        IEnumerable<Song> GetSongsFromAlbum(Album album, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a song to the specified path
        /// </summary>
        /// <param name="song">Song to download</param>
        /// <param name="path">Destination path</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="isPreview">If true, optimize for faster preview download (lower quality, shorter duration)</param>
        /// <returns>True if download succeeded</returns>
        bool DownloadSong(Song song, string path, CancellationToken cancellationToken, bool isPreview = false);
    }
}

