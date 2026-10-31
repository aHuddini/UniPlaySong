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

        // auto marks an automatic (unattended) search.
        IEnumerable<Album> GetAlbumsForGame(string gameName, CancellationToken cancellationToken, bool auto = false);

        IEnumerable<Song> GetSongsFromAlbum(Album album, CancellationToken cancellationToken = default);

        // isPreview trades quality and length for a faster download. True on success.
        bool DownloadSong(Song song, string path, CancellationToken cancellationToken, bool isPreview = false);
    }
}

