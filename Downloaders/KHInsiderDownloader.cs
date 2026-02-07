using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Downloaders
{
    /// <summary>
    /// Downloader implementation for KHInsider (downloads.khinsider.com)
    /// </summary>
    public class KHInsiderDownloader : IDownloader
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "KHInsider";
        private const string KhInsiderBaseUrl = "https://downloads.khinsider.com/";

        private readonly HttpClient _httpClient;
        private readonly HtmlWeb _htmlWeb;
        private readonly ErrorHandlerService _errorHandler;

        public KHInsiderDownloader(HttpClient httpClient, HtmlWeb htmlWeb, ErrorHandlerService errorHandler = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _htmlWeb = htmlWeb ?? throw new ArgumentNullException(nameof(htmlWeb));
            _errorHandler = errorHandler;
            
            // Set user agent to avoid blocking
            if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; UniPlaySong/1.0)");
            }
        }

        public string BaseUrl() => KhInsiderBaseUrl;

        public Source DownloadSource() => Source.KHInsider;

        public IEnumerable<Album> GetAlbumsForGame(string gameName, CancellationToken cancellationToken, bool auto = false)
        {
            var albums = new List<Album>();

            try
            {
                // URL-encode the game name - KHInsider uses + for spaces
                var encodedName = Uri.EscapeDataString(gameName).Replace("%20", "+");
                var searchUrl = $"{KhInsiderBaseUrl}search?search={encodedName}";

                // Use async properly - but we need to block for IEnumerable return
                // In a real async scenario, this would return Task<IEnumerable<Album>>
                var htmlDoc = _htmlWeb.LoadFromWebAsync(searchUrl, cancellationToken).GetAwaiter().GetResult();

                if (htmlDoc == null)
                {
                    Logger.Warn($"[KHInsider] No response for: '{gameName}'");
                    return albums;
                }

                var tableRows = htmlDoc.DocumentNode.Descendants("tr").Skip(1).ToList();

                foreach (var row in tableRows)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var album = ParseAlbumRow(row, gameName);
                    if (album != null)
                    {
                        albums.Add(album);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Search cancelled
            }
            catch (Exception ex)
            {
                Logger.Error($"[KHInsider] Error searching '{gameName}': {ex.Message}");
                _errorHandler?.HandleError(
                    ex,
                    context: $"searching KHInsider for '{gameName}'",
                    showUserMessage: false
                );
            }

            return albums;
        }

        public IEnumerable<Song> GetSongsFromAlbum(Album album, CancellationToken cancellationToken = default)
        {
            var songs = new List<Song>();

            try
            {
                if (string.IsNullOrWhiteSpace(album?.Id))
                {
                    Logger.Warn("Album ID is null or empty");
                    return songs;
                }

                var albumUrl = $"{KhInsiderBaseUrl}{album.Id}";

                var htmlDoc = _htmlWeb.LoadFromWebAsync(albumUrl, cancellationToken).GetAwaiter().GetResult();

                if (htmlDoc == null)
                {
                    Logger.Warn($"Failed to load album: {album.Name}");
                    return songs;
                }

                // Check if album has MP3 files
                var headerRow = htmlDoc.GetElementbyId("songlist_header");
                if (headerRow != null)
                {
                    var headers = headerRow.Descendants("th").Select(n => n.InnerHtml);
                    if (!headers.Any(h => h.Contains("MP3")))
                    {
                        // Album has no MP3 files
                        return songs;
                    }
                }

                var songTable = htmlDoc.GetElementbyId("songlist");
                if (songTable == null)
                {
                    Logger.Warn($"No song list found for album: {album.Name}");
                    return songs;
                }

                var tableRows = songTable.Descendants("tr").Skip(1).ToList();
                if (tableRows.Count < 2)
                {
                    return songs;
                }

                // Remove footer row
                tableRows.RemoveAt(tableRows.Count - 1);

                foreach (var row in tableRows)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var song = ParseSongRow(row);
                    if (song != null)
                    {
                        songs.Add(song);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Album loading cancelled
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: $"loading songs from album '{album?.Name}'",
                    showUserMessage: false
                );
            }

            return songs;
        }

        public bool DownloadSong(Song song, string path, CancellationToken cancellationToken, bool isPreview = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(song?.Id))
                {
                    Logger.Warn("Song ID is null or empty");
                    return false;
                }

                // Get the actual download URL from the song page
                var songUrl = $"{KhInsiderBaseUrl}{song.Id}";

                var htmlDoc = _htmlWeb.LoadFromWebAsync(songUrl, cancellationToken).GetAwaiter().GetResult();
                if (htmlDoc == null)
                {
                    Logger.Warn($"Failed to load song page: {song.Name}");
                    return false;
                }

                var audioElement = htmlDoc.GetElementbyId("audio");
                if (audioElement == null)
                {
                    Logger.Warn($"No audio element found for song: {song.Name}");
                    return false;
                }

                var fileUrl = audioElement.GetAttributeValue("src", null);
                if (string.IsNullOrWhiteSpace(fileUrl))
                {
                    Logger.Warn($"No download URL found for song: {song.Name}");
                    return false;
                }

                // Make URL absolute if it's relative
                if (!Uri.IsWellFormedUriString(fileUrl, UriKind.Absolute))
                {
                    if (fileUrl.StartsWith("//"))
                    {
                        fileUrl = "https:" + fileUrl;
                    }
                    else if (fileUrl.StartsWith("/"))
                    {
                        fileUrl = KhInsiderBaseUrl.TrimEnd('/') + fileUrl;
                    }
                    else
                    {
                        // Relative to current page
                        var baseUri = new Uri(songUrl);
                        fileUrl = new Uri(baseUri, fileUrl).ToString();
                    }
                }

                // Ensure directory exists
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Download the file
                // Use SendAsync with HttpRequestMessage to avoid overload ambiguity in .NET Framework 4.6.2
                var request = new HttpRequestMessage(HttpMethod.Get, fileUrl);
                var response = _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                using (var fileStream = File.Create(path))
                {
                    // CopyToAsync in .NET Framework 4.6.2 doesn't have CancellationToken overload
                    // Use ReadAsStreamAsync and copy manually, or just use CopyToAsync without cancellation
                    response.Content.CopyToAsync(fileStream).GetAwaiter().GetResult();
                }

                // Verify file was created and has content
                if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    if (fileInfo.Length > 0)
                    {
                        return true;
                    }
                    else
                    {
                        Logger.Warn($"Downloaded file is empty: {path}");
                        File.Delete(path);
                        return false;
                    }
                }
                else
                {
                    Logger.Error($"Download failed: File not created at {path}");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (HttpRequestException ex)
            {
                Logger.Error($"[KHInsider] Download HTTP error for '{song?.Name}': {ex.Message}");
                _errorHandler?.HandleError(
                    ex,
                    context: $"downloading song '{song?.Name}'",
                    showUserMessage: false
                );
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"[KHInsider] Download failed for '{song?.Name}': {ex.GetType().Name} - {ex.Message}");
                _errorHandler?.HandleError(
                    ex,
                    context: $"downloading song '{song?.Name}'",
                    showUserMessage: false
                );
                return false;
            }
        }

        private Album ParseAlbumRow(HtmlNode row, string gameName)
        {
            try
            {
                var columns = row.Descendants("td").ToList();
                if (columns.Count < 2)
                    return null;

                // Get icon URL
                var iconUrl = string.Empty;
                var iconField = columns.FirstOrDefault();
                if (iconField != null)
                {
                    var img = iconField.Descendants("img").FirstOrDefault();
                    if (img != null)
                    {
                        iconUrl = img.GetAttributeValue("src", string.Empty);
                    }
                }

                // Get title and link
                var titleField = columns.ElementAtOrDefault(1);
                if (titleField == null)
                    return null;

                var link = titleField.Descendants("a").FirstOrDefault();
                if (link == null)
                    return null;

                var albumName = StringHelper.StripHtml(link.InnerHtml);
                var albumPartialLink = link.GetAttributeValue("href", null);
                if (string.IsNullOrWhiteSpace(albumPartialLink))
                    return null;

                var album = new Album
                {
                    Name = albumName,
                    Id = albumPartialLink.TrimStart('/'),
                    Source = Source.KHInsider,
                    IconUrl = iconUrl
                };

                // Get platforms
                var platformField = columns.ElementAtOrDefault(2);
                if (platformField != null)
                {
                    var platforms = platformField.Descendants("a")
                        .Select(a => StringHelper.StripHtml(a.InnerHtml))
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .ToList();
                    if (platforms.Any())
                    {
                        album.Platforms = platforms;
                    }
                }

                // Get type
                var typeField = columns.ElementAtOrDefault(3);
                if (typeField != null && !string.IsNullOrWhiteSpace(typeField.InnerText))
                {
                    album.Type = typeField.InnerText.Trim();
                }

                // Get year
                var yearField = columns.ElementAtOrDefault(4);
                if (yearField != null && !string.IsNullOrWhiteSpace(yearField.InnerText))
                {
                    album.Year = yearField.InnerText.Trim();
                }

                return album;
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "parsing album row",
                    showUserMessage: false
                );
                return null;
            }
        }

        private Song ParseSongRow(HtmlNode row)
        {
            try
            {
                var links = row.Descendants("a").ToList();
                if (links.Count == 0)
                    return null;

                var songLink = links.FirstOrDefault();
                if (songLink == null)
                    return null;

                var partialUrl = songLink.GetAttributeValue("href", null);
                if (string.IsNullOrWhiteSpace(partialUrl))
                    return null;

                var song = new Song
                {
                    Name = StringHelper.StripHtml(songLink.InnerHtml),
                    Id = partialUrl.TrimStart('/'),
                    Source = Source.KHInsider
                };

                // Get length
                var lengthLink = links.ElementAtOrDefault(1);
                if (lengthLink != null && !string.IsNullOrWhiteSpace(lengthLink.InnerHtml))
                {
                    song.Length = StringHelper.ParseTimeSpan(lengthLink.InnerHtml);
                }

                // Get size
                var sizeLink = links.ElementAtOrDefault(2);
                if (sizeLink != null && !string.IsNullOrWhiteSpace(sizeLink.InnerHtml))
                {
                    song.SizeInMb = StringHelper.StripHtml(sizeLink.InnerHtml);
                }

                return song;
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "parsing song row",
                    showUserMessage: false
                );
                return null;
            }
        }
    }
}

