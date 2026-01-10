using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using HtmlAgilityPack;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Downloaders
{
    /// <summary>
    /// Downloader implementation for Zophar.net (zophar.net/music)
    /// Video game music archive with emulated formats and MP3s
    /// </summary>
    public class ZopharDownloader : IDownloader
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "Zophar";
        private const string ZopharBaseUrl = "https://www.zophar.net/";
        private const string ZopharFileBaseUrl = "https://fi.zophar.net/";

        private readonly HttpClient _httpClient;
        private readonly HtmlWeb _htmlWeb;
        private readonly ErrorHandlerService _errorHandler;

        public ZopharDownloader(HttpClient httpClient, HtmlWeb htmlWeb, ErrorHandlerService errorHandler = null)
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

        public string BaseUrl() => ZopharBaseUrl;

        public Source DownloadSource() => Source.Zophar;

        public IEnumerable<Album> GetAlbumsForGame(string gameName, CancellationToken cancellationToken, bool auto = false)
        {
            var albums = new List<Album>();

            try
            {
                // Zophar uses + for spaces in search
                var encodedName = Uri.EscapeDataString(gameName).Replace("%20", "+");
                var searchUrl = $"{ZopharBaseUrl}music/search?search={encodedName}";

                Logger.Info($"[Zophar] Search: '{gameName}' → {searchUrl}");

                var htmlDoc = _htmlWeb.LoadFromWebAsync(searchUrl, cancellationToken).GetAwaiter().GetResult();

                if (htmlDoc == null)
                {
                    Logger.Warn($"[Zophar] No response for: '{gameName}'");
                    return albums;
                }

                // Find all result rows in the table
                // Results are in table rows with links to /music/{console}/{game-slug}
                var resultLinks = htmlDoc.DocumentNode.SelectNodes("//a[contains(@href, '/music/') and not(contains(@href, '/search'))]");

                if (resultLinks == null || resultLinks.Count == 0)
                {
                    Logger.Info($"[Zophar] No results for: '{gameName}'");
                    return albums;
                }

                // Track seen URLs to avoid duplicates (image and text links point to same album)
                var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var link in resultLinks)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var href = link.GetAttributeValue("href", "");
                    if (string.IsNullOrEmpty(href) || !href.StartsWith("/music/"))
                        continue;

                    // Skip if we've already seen this URL
                    if (seenUrls.Contains(href))
                        continue;
                    seenUrls.Add(href);

                    // Extract album name from link text or parent row
                    var albumName = HtmlEntity.DeEntitize(link.InnerText?.Trim() ?? "");

                    // Skip if it's just an image or empty text
                    if (string.IsNullOrWhiteSpace(albumName) || albumName.Length < 2)
                    {
                        // Try to find text in sibling or parent
                        var parentRow = link.Ancestors("tr").FirstOrDefault();
                        if (parentRow != null)
                        {
                            var textLink = parentRow.SelectSingleNode(".//a[string-length(normalize-space(text())) > 2]");
                            if (textLink != null)
                            {
                                albumName = HtmlEntity.DeEntitize(textLink.InnerText?.Trim() ?? "");
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(albumName))
                        continue;

                    // Extract console from URL path: /music/{console}/{game-slug}
                    var urlParts = href.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    var console = urlParts.Length >= 2 ? urlParts[1] : "unknown";

                    var album = new Album
                    {
                        Id = href.TrimStart('/'), // Store relative path: music/{console}/{game-slug}
                        Name = albumName,
                        Source = Source.Zophar,
                        Artist = console.ToUpperInvariant() // Use console as artist for display
                    };

                    albums.Add(album);
                    Logger.DebugIf(LogPrefix, $"[Zophar] Parsed album: '{album.Name}' ({console})");
                }

                // Remove duplicates by ID
                albums = albums.GroupBy(a => a.Id).Select(g => g.First()).ToList();

                // Filter out generic console entries (e.g., just "NES" or "SNES" without game name)
                // These are platform pages, not actual game soundtracks
                albums = albums.Where(a => !IsGenericConsolePage(a.Name, gameName)).ToList();

                Logger.Info($"[Zophar] Result: '{gameName}' → {albums.Count} album(s)");
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"[Zophar] Cancelled: '{gameName}'");
            }
            catch (Exception ex)
            {
                Logger.Error($"[Zophar] Error searching '{gameName}': {ex.Message}");
                _errorHandler?.HandleError(
                    ex,
                    context: $"searching Zophar for '{gameName}'",
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
                    Logger.Warn("[Zophar] Album ID is null or empty");
                    return songs;
                }

                var albumUrl = $"{ZopharBaseUrl}{album.Id}";
                Logger.Info($"[Zophar] Loading album: {albumUrl}");

                var htmlDoc = _htmlWeb.LoadFromWebAsync(albumUrl, cancellationToken).GetAwaiter().GetResult();

                if (htmlDoc == null)
                {
                    Logger.Warn($"[Zophar] Failed to load album: {album.Name}");
                    return songs;
                }

                // Extract console and game slug from album ID for building download URLs
                // album.Id format: music/{console}/{game-slug}
                var idParts = album.Id.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (idParts.Length < 3)
                {
                    Logger.Warn($"[Zophar] Invalid album ID format: {album.Id}");
                    return songs;
                }

                var console = idParts[1];
                var gameSlug = idParts[2];

                // Try to find tracklist - Zophar uses a table with id="songlist" or similar
                // Also look for JavaScript track data
                var tracklistTable = htmlDoc.GetElementbyId("songlist") ??
                                     htmlDoc.DocumentNode.SelectSingleNode("//table[contains(@class, 'track')]") ??
                                     htmlDoc.DocumentNode.SelectSingleNode("//table[.//tr[contains(@class, 'pl')]]");

                if (tracklistTable != null)
                {
                    var rows = tracklistTable.SelectNodes(".//tr[td]");
                    if (rows != null)
                    {
                        foreach (var row in rows)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            var song = ParseTrackRow(row, console, gameSlug);
                            if (song != null)
                            {
                                songs.Add(song);
                            }
                        }
                    }
                }

                // If no songs found via table, try parsing JavaScript track data
                if (songs.Count == 0)
                {
                    songs = ParseJavaScriptTracks(htmlDoc, console, gameSlug, cancellationToken);
                }

                // If still no songs, try finding direct MP3 links
                if (songs.Count == 0)
                {
                    var mp3Links = htmlDoc.DocumentNode.SelectNodes("//a[contains(@href, '.mp3')]");
                    if (mp3Links != null)
                    {
                        int trackNum = 1;
                        foreach (var link in mp3Links)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            var href = link.GetAttributeValue("href", "");
                            if (string.IsNullOrEmpty(href))
                                continue;

                            var songName = HtmlEntity.DeEntitize(link.InnerText?.Trim() ?? "");
                            if (string.IsNullOrWhiteSpace(songName))
                            {
                                songName = Path.GetFileNameWithoutExtension(WebUtility.UrlDecode(href));
                            }

                            // Make URL absolute
                            if (!href.StartsWith("http"))
                            {
                                href = href.StartsWith("//") ? "https:" + href :
                                       href.StartsWith("/") ? ZopharFileBaseUrl.TrimEnd('/') + href :
                                       $"{ZopharFileBaseUrl}soundfiles/{console}/{gameSlug}/{href}";
                            }

                            songs.Add(new Song
                            {
                                Id = href, // Store full download URL
                                Name = songName,
                                Source = Source.Zophar
                            });
                            trackNum++;
                        }
                    }
                }

                Logger.Info($"[Zophar] Found {songs.Count} songs in album '{album.Name}'");
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"[Zophar] Album loading cancelled for: {album?.Name}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[Zophar] Error loading album '{album?.Name}': {ex.Message}");
                _errorHandler?.HandleError(
                    ex,
                    context: $"loading songs from Zophar album '{album?.Name}'",
                    showUserMessage: false
                );
            }

            return songs;
        }

        private Song ParseTrackRow(HtmlNode row, string console, string gameSlug)
        {
            try
            {
                var cells = row.SelectNodes("td");
                if (cells == null || cells.Count == 0)
                    return null;

                // Try to find track name - usually in first or second cell
                string trackName = null;
                string duration = null;
                string downloadUrl = null;

                foreach (var cell in cells)
                {
                    var text = HtmlEntity.DeEntitize(cell.InnerText?.Trim() ?? "");

                    // Check for duration pattern (e.g., "3:06", "12:34")
                    if (Regex.IsMatch(text, @"^\d{1,2}:\d{2}$"))
                    {
                        duration = text;
                        continue;
                    }

                    // Check for download link
                    var link = cell.SelectSingleNode(".//a[contains(@href, '.mp3')]");
                    if (link != null)
                    {
                        downloadUrl = link.GetAttributeValue("href", "");
                    }

                    // Track name is usually the longest non-duration text
                    if (text.Length > 2 && !Regex.IsMatch(text, @"^\d+$") && (trackName == null || text.Length > trackName.Length))
                    {
                        trackName = text;
                    }
                }

                if (string.IsNullOrWhiteSpace(trackName))
                    return null;

                // Build download URL if not found directly
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    // URL encode the track name
                    var encodedName = Uri.EscapeDataString(trackName);
                    downloadUrl = $"{ZopharFileBaseUrl}soundfiles/{console}/{gameSlug}/{encodedName}.mp3";
                }
                else if (!downloadUrl.StartsWith("http"))
                {
                    downloadUrl = downloadUrl.StartsWith("//") ? "https:" + downloadUrl :
                                  downloadUrl.StartsWith("/") ? ZopharFileBaseUrl.TrimEnd('/') + downloadUrl :
                                  $"{ZopharFileBaseUrl}soundfiles/{console}/{gameSlug}/{downloadUrl}";
                }

                // Try to extract track number from name
                int trackNum = 0;
                var trackMatch = Regex.Match(trackName, @"^(\d+)\s*[-–.]\s*");
                if (trackMatch.Success)
                {
                    int.TryParse(trackMatch.Groups[1].Value, out trackNum);
                }

                // Parse duration if available
                TimeSpan? length = null;
                if (!string.IsNullOrEmpty(duration))
                {
                    var parts = duration.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int mins) && int.TryParse(parts[1], out int secs))
                    {
                        length = TimeSpan.FromSeconds(mins * 60 + secs);
                    }
                }

                return new Song
                {
                    Id = downloadUrl,
                    Name = trackName,
                    Length = length,
                    Source = Source.Zophar
                };
            }
            catch (Exception ex)
            {
                Logger.DebugIf(LogPrefix, $"[Zophar] Error parsing track row: {ex.Message}");
                return null;
            }
        }

        private List<Song> ParseJavaScriptTracks(HtmlDocument htmlDoc, string console, string gameSlug, CancellationToken cancellationToken)
        {
            var songs = new List<Song>();

            try
            {
                // Look for JavaScript that defines track data
                // Zophar often has: var tracks = [{name: "...", file: "...", length: "..."}, ...]
                var scripts = htmlDoc.DocumentNode.SelectNodes("//script[not(@src)]");
                if (scripts == null)
                    return songs;

                foreach (var script in scripts)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var content = script.InnerHtml;
                    if (string.IsNullOrEmpty(content))
                        continue;

                    // Look for track array pattern
                    var trackMatches = Regex.Matches(content, @"\{[^{}]*['""](?:name|title)['""]\s*:\s*['""]([^'""]+)['""][^{}]*['""]file['""]\s*:\s*['""]([^'""]+)['""][^{}]*\}",
                        RegexOptions.IgnoreCase);

                    int trackNum = 1;
                    foreach (Match match in trackMatches)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var name = match.Groups[1].Value;
                        var file = match.Groups[2].Value;

                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(file))
                            continue;

                        // Build full URL
                        var downloadUrl = file;
                        if (!downloadUrl.StartsWith("http"))
                        {
                            downloadUrl = $"{ZopharFileBaseUrl}soundfiles/{console}/{gameSlug}/{file}";
                        }

                        songs.Add(new Song
                        {
                            Id = downloadUrl,
                            Name = name,
                            Source = Source.Zophar
                        });
                        trackNum++;
                    }

                    if (songs.Count > 0)
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.DebugIf(LogPrefix, $"[Zophar] Error parsing JavaScript tracks: {ex.Message}");
            }

            return songs;
        }

        // Common console/platform names that appear as standalone results
        private static readonly HashSet<string> ConsoleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NES", "SNES", "N64", "GameCube", "Wii", "Wii U", "Switch",
            "Game Boy", "Game Boy Color", "Game Boy Advance", "GBA", "DS", "3DS",
            "PlayStation", "PS1", "PS2", "PS3", "PS4", "PS5", "PSP", "PS Vita",
            "Xbox", "Xbox 360", "Xbox One", "Xbox Series",
            "Genesis", "Mega Drive", "Saturn", "Dreamcast",
            "Master System", "Game Gear",
            "TurboGrafx-16", "PC Engine", "Neo Geo", "Arcade",
            "Atari", "Atari 2600", "Atari 7800", "Atari Lynx", "Jaguar",
            "DOS", "PC", "Windows", "Amiga", "C64", "Commodore 64"
        };

        private bool IsGenericConsolePage(string albumName, string searchedGameName)
        {
            if (string.IsNullOrWhiteSpace(albumName)) return true;

            var trimmed = albumName.Trim();

            // Check if album name is just a console name
            if (ConsoleNames.Contains(trimmed)) return true;

            // Check if it's a very short name that doesn't contain any words from the search
            if (trimmed.Length <= 5)
            {
                var searchWords = searchedGameName.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var albumLower = trimmed.ToLowerInvariant();
                if (!searchWords.Any(w => albumLower.Contains(w) || w.Contains(albumLower)))
                    return true;
            }

            return false;
        }

        public bool DownloadSong(Song song, string path, CancellationToken cancellationToken, bool isPreview = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(song?.Id))
                {
                    Logger.Warn("[Zophar] Song ID (download URL) is null or empty");
                    return false;
                }

                var downloadUrl = song.Id;

                // Ensure URL is absolute
                if (!downloadUrl.StartsWith("http"))
                {
                    downloadUrl = downloadUrl.StartsWith("//") ? "https:" + downloadUrl :
                                  ZopharFileBaseUrl + downloadUrl.TrimStart('/');
                }

                Logger.Info($"[Zophar] Downloading: {downloadUrl}");

                // Ensure directory exists
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Download the file
                var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                var response = _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                using (var fileStream = File.Create(path))
                {
                    response.Content.CopyToAsync(fileStream).GetAwaiter().GetResult();
                }

                // Verify file was created and has content
                if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    if (fileInfo.Length > 0)
                    {
                        Logger.Info($"[Zophar] Downloaded: {song.Name} ({fileInfo.Length} bytes)");
                        return true;
                    }
                    else
                    {
                        Logger.Warn($"[Zophar] Downloaded file is empty: {path}");
                        File.Delete(path);
                        return false;
                    }
                }
                else
                {
                    Logger.Error($"[Zophar] Download failed: File not created at {path}");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"[Zophar] Download cancelled: {song?.Name}");
                return false;
            }
            catch (HttpRequestException ex)
            {
                Logger.Error($"[Zophar] Download HTTP error for '{song?.Name}': {ex.Message}");
                _errorHandler?.HandleError(
                    ex,
                    context: $"downloading '{song?.Name}' from Zophar",
                    showUserMessage: false
                );
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"[Zophar] Download error for '{song?.Name}': {ex.Message}");
                _errorHandler?.HandleError(
                    ex,
                    context: $"downloading '{song?.Name}' from Zophar",
                    showUserMessage: false
                );
                return false;
            }
        }
    }
}
