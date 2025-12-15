using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Downloaders
{
    /// <summary>
    /// Downloader implementation for YouTube
    /// Note: Requires yt-dlp and ffmpeg to be installed and configured
    /// </summary>
    public class YouTubeDownloader : IDownloader
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string YouTubeBaseUrl = "https://www.youtube.com";

        private readonly HttpClient _httpClient;
        private readonly string _ytDlpPath;
        private readonly string _ffmpegPath;
        private readonly ErrorHandlerService _errorHandler;

        public YouTubeDownloader(HttpClient httpClient, string ytDlpPath, string ffmpegPath, ErrorHandlerService errorHandler)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ytDlpPath = ytDlpPath;
            _ffmpegPath = ffmpegPath;
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        }

        public string BaseUrl() => YouTubeBaseUrl;

        public Source DownloadSource() => Source.YouTube;

        public IEnumerable<Album> GetAlbumsForGame(string gameName, CancellationToken cancellationToken, bool auto = false)
        {
            var albums = new List<Album>();

            try
            {
                // Use gameName as-is if it already contains OST/soundtrack keywords, otherwise append OST
                var searchQuery = gameName;
                if (!gameName.ContainsIgnoreCase("OST") && 
                    !gameName.ContainsIgnoreCase("soundtrack") &&
                    !gameName.ContainsIgnoreCase("original soundtrack"))
                {
                    searchQuery = $"{gameName} OST";
                }
                
                Logger.Debug($"Searching YouTube for: {searchQuery}");

                var client = new YouTubeClient(_httpClient, _errorHandler);
                var results = client.Search(searchQuery, 100, cancellationToken);

                if (results == null)
                {
                    Logger.Warn($"YouTube search returned null for '{gameName}'");
                    return albums;
                }

                albums = results.Select(item => new Album
                {
                    Name = item.Title,
                    Id = item.Id,
                    Source = Source.YouTube,
                    IconUrl = item.ThumbnailUrl?.ToString(),
                    Count = item.Count,
                    ChannelId = item.ChannelId,
                    ChannelName = item.ChannelName
                }).ToList();

                Logger.Info($"Found {albums.Count} playlists for '{gameName}' on YouTube");
                
                if (albums.Count == 0)
                {
                    Logger.Warn($"No playlists found for '{gameName}' on YouTube. Search query was: '{searchQuery}'");
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"YouTube search cancelled for: {gameName}");
                throw; // Re-throw cancellation
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(
                    ex,
                    context: $"searching YouTube for '{gameName}'",
                    showUserMessage: false // Don't show error dialog here - return empty list so dialog can show "no results" message
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

                Logger.Debug($"Loading YouTube playlist: {album.Name}");

                var client = new YouTubeClient(_httpClient, _errorHandler);
                var playlistItems = client.GetPlaylist(album.Id, cancellationToken);

                songs = playlistItems.Select(item => new Song
                {
                    Name = item.Title,
                    Id = item.Id,
                    Length = item.Duration,
                    Source = Source.YouTube,
                    IconUrl = item.ThumbnailUrl?.ToString()
                }).ToList();

                Logger.Info($"Found {songs.Count} songs in playlist '{album.Name}'");
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"Playlist loading cancelled for: {album?.Name}");
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(
                    ex,
                    context: $"loading songs from playlist '{album?.Name}'",
                    showUserMessage: false
                );
            }

            return songs;
        }

        public bool DownloadSong(Song song, string path, CancellationToken cancellationToken, bool isPreview = false)
        {
            if (string.IsNullOrWhiteSpace(_ytDlpPath) || string.IsNullOrWhiteSpace(_ffmpegPath))
            {
                Logger.Error("yt-dlp or ffmpeg path not configured. Cannot download from YouTube.");
                return false;
            }

            if (!File.Exists(_ytDlpPath))
            {
                Logger.Error($"yt-dlp not found at: {_ytDlpPath}");
                return false;
            }

            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // yt-dlp arguments: extract audio, mp3 format, output path
                // For previews: use lower quality (5 = ~128kbps), limit to 30 seconds, and optimize for speed
                // For full downloads: use best quality (0 = best available)
                var pathWithoutExt = Path.ChangeExtension(path, null);
                var quality = isPreview ? "5" : "0"; // 5 = ~128kbps, 0 = best
                
                // For previews, add optimization flags for faster, more consistent downloads
                var previewOptimizations = isPreview 
                    ? " --no-playlist --no-warnings --quiet --no-progress --postprocessor-args \"ffmpeg:-t 30\""
                    : "";
                
                // For full downloads, use standard flags
                var standardFlags = isPreview ? "" : " --no-playlist";
                
                var arguments = $"-x --audio-format mp3 --audio-quality {quality}{previewOptimizations}{standardFlags} --ffmpeg-location=\"{_ffmpegPath}\" -o \"{pathWithoutExt}.%(ext)s\" {YouTubeBaseUrl}/watch?v={song.Id}";

                Logger.Debug($"Downloading song with yt-dlp: {song.Name} to {path}");
                Logger.Debug($"yt-dlp command: {_ytDlpPath} {arguments}");

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(_ytDlpPath) ?? Directory.GetCurrentDirectory(),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        Logger.Error("Failed to start yt-dlp process");
                        return false;
                    }

                    // Read output for progress (optional - can be enhanced later)
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    while (!process.WaitForExit(100))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch { }
                            Logger.Info($"Download cancelled for: {song.Name}");
                            return false;
                        }
                    }

                    if (process.ExitCode != 0)
                    {
                        var error = process.StandardError.ReadToEnd();
                        Logger.Error($"yt-dlp failed with exit code {process.ExitCode}: {error}");
                        return false;
                    }
                }

                // yt-dlp may add extension, check if file exists with various extensions
                if (!File.Exists(path))
                {
                    var pathWithoutExt2 = Path.ChangeExtension(path, null);
                    var possibleExtensions = new[] { ".mp3", ".m4a", ".webm", ".opus" };
                    
                    foreach (var ext in possibleExtensions)
                    {
                        var testPath = pathWithoutExt2 + ext;
                        if (File.Exists(testPath))
                        {
                            Logger.Debug($"Found file with extension {ext}, moving to {path}");
                            File.Move(testPath, path);
                            break;
                        }
                    }
                    
                    // Also check if yt-dlp created a file with the video ID in the name
                    var downloadDirectory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(downloadDirectory))
                    {
                        var files = Directory.GetFiles(downloadDirectory, $"{Path.GetFileNameWithoutExtension(pathWithoutExt2)}*");
                        if (files.Length > 0)
                        {
                            var foundFile = files[0];
                            Logger.Debug($"Found file with pattern match: {foundFile}, moving to {path}");
                            if (File.Exists(path))
                            {
                                File.Delete(path);
                            }
                            File.Move(foundFile, path);
                        }
                    }
                }

                if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    Logger.Info($"Successfully downloaded: {song.Name} to {path} ({fileInfo.Length} bytes)");
                    return true;
                }
                
                Logger.Error($"Download failed: File not found at {path}");
                // List files in directory for debugging
                var debugDir = Path.GetDirectoryName(path);
                if (Directory.Exists(debugDir))
                {
                    var files = Directory.GetFiles(debugDir);
                    Logger.Debug($"Files in directory: {string.Join(", ", files.Select(f => Path.GetFileName(f)))}");
                }
                return false;
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"Download cancelled for: {song?.Name}");
                return false;
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(
                    ex,
                    context: $"downloading song '{song?.Name}'",
                    showUserMessage: false
                );
                return false;
            }
        }
    }
}

