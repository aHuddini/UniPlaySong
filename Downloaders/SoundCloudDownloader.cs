using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Downloaders
{
    /// <summary>
    /// Downloader implementation for SoundCloud (hints-only, no search).
    /// Uses yt-dlp for both metadata extraction and downloads.
    /// </summary>
    public class SoundCloudDownloader : IDownloader
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "SoundCloudDownloader";
        private const string SoundCloudBaseUrl = "https://soundcloud.com/";

        private readonly string _ytDlpPath;
        private readonly string _ffmpegPath;
        private readonly ErrorHandlerService _errorHandler;

        public SoundCloudDownloader(string ytDlpPath, string ffmpegPath, ErrorHandlerService errorHandler)
        {
            _ytDlpPath = ytDlpPath;
            _ffmpegPath = ffmpegPath;
            _errorHandler = errorHandler;
        }

        public string BaseUrl() => SoundCloudBaseUrl;

        public Source DownloadSource() => Source.SoundCloud;

        /// <summary>
        /// SoundCloud is hints-only, no search functionality
        /// </summary>
        public IEnumerable<Album> GetAlbumsForGame(string gameName, CancellationToken cancellationToken, bool auto = false)
        {
            // SoundCloud doesn't support search - only direct URLs from hints
            return Enumerable.Empty<Album>();
        }

        /// <summary>
        /// Gets songs from a SoundCloud track or playlist.
        /// For single tracks, returns one song. For playlists, fetches all tracks.
        /// </summary>
        public IEnumerable<Song> GetSongsFromAlbum(Album album, CancellationToken cancellationToken = default)
        {
            if (album == null || string.IsNullOrEmpty(album.Id))
                return Enumerable.Empty<Song>();

            var songs = new List<Song>();
            var fullUrl = SoundCloudBaseUrl + album.Id;

            try
            {
                // Check if it's a playlist/set (contains "/sets/")
                if (album.Id.Contains("/sets/"))
                {
                    songs = GetPlaylistSongs(fullUrl, album.Name, cancellationToken).ToList();
                }
                else
                {
                    // Single track - use yt-dlp to get metadata
                    var trackInfo = GetTrackMetadata(fullUrl, cancellationToken);
                    if (trackInfo != null)
                    {
                        songs.Add(trackInfo);
                    }
                    else
                    {
                        // Fallback: create song with URL as ID
                        songs.Add(new Song
                        {
                            Id = album.Id,
                            Name = album.Name.Replace(" (UPS Hint - SoundCloud)", ""),
                            Source = Source.SoundCloud
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"{LogPrefix}: Error getting songs from SoundCloud: {ex.Message}");
                _errorHandler?.HandleError(ex, $"fetching SoundCloud tracks from '{album.Name}'", showUserMessage: false);
            }

            return songs;
        }

        /// <summary>
        /// Gets metadata for a single SoundCloud track using yt-dlp
        /// </summary>
        private Song GetTrackMetadata(string trackUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_ytDlpPath) || !File.Exists(_ytDlpPath))
            {
                Logger.Warn($"{LogPrefix}: yt-dlp not configured or not found");
                return null;
            }

            try
            {
                // Use yt-dlp --dump-json to get track metadata without downloading
                var arguments = $"--dump-json --no-playlist \"{trackUrl}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        Logger.Error($"{LogPrefix}: Failed to start yt-dlp process");
                        return null;
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();

                    process.WaitForExit(30000); // 30 second timeout

                    if (process.ExitCode != 0)
                    {
                        Logger.Warn($"{LogPrefix}: yt-dlp metadata fetch failed with exit code {process.ExitCode}: {error}");
                        return null;
                    }

                    if (string.IsNullOrEmpty(output))
                    {
                        Logger.Warn($"{LogPrefix}: yt-dlp returned empty output");
                        return null;
                    }

                    var json = JObject.Parse(output);
                    var urlPath = ExtractUrlPath(trackUrl);

                    // Parse duration if available (yt-dlp returns duration in seconds)
                    TimeSpan? length = null;
                    if (double.TryParse(json["duration"]?.ToString(), out var durationSeconds))
                    {
                        length = TimeSpan.FromSeconds(durationSeconds);
                    }

                    return new Song
                    {
                        Id = urlPath,
                        Name = json["title"]?.ToString() ?? json["fulltitle"]?.ToString() ?? "Unknown Track",
                        Length = length,
                        Source = Source.SoundCloud
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"{LogPrefix}: Error getting track metadata: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets all tracks from a SoundCloud playlist/set using yt-dlp
        /// </summary>
        private IEnumerable<Song> GetPlaylistSongs(string playlistUrl, string albumName, CancellationToken cancellationToken)
        {
            var songs = new List<Song>();

            if (string.IsNullOrWhiteSpace(_ytDlpPath) || !File.Exists(_ytDlpPath))
            {
                Logger.Warn($"{LogPrefix}: yt-dlp not configured or not found");
                return songs;
            }

            try
            {
                // Use yt-dlp --flat-playlist -J to get playlist items without downloading
                var arguments = $"--flat-playlist -J \"{playlistUrl}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        Logger.Error($"{LogPrefix}: Failed to start yt-dlp process for playlist");
                        return songs;
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();

                    process.WaitForExit(60000); // 60 second timeout for playlists

                    if (process.ExitCode != 0)
                    {
                        Logger.Warn($"{LogPrefix}: yt-dlp playlist fetch failed with exit code {process.ExitCode}: {error}");
                        return songs;
                    }

                    if (string.IsNullOrEmpty(output))
                    {
                        Logger.Warn($"{LogPrefix}: yt-dlp returned empty output for playlist");
                        return songs;
                    }

                    var json = JObject.Parse(output);
                    var entries = json["entries"] as JArray;

                    if (entries == null || entries.Count == 0)
                    {
                        Logger.Warn($"{LogPrefix}: No entries found in playlist");
                        return songs;
                    }

                    foreach (var entry in entries)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var url = entry["url"]?.ToString();
                        var title = entry["title"]?.ToString() ?? "Unknown Track";

                        // Parse duration if available (yt-dlp returns duration in seconds)
                        TimeSpan? length = null;
                        if (double.TryParse(entry["duration"]?.ToString(), out var durationSeconds))
                        {
                            length = TimeSpan.FromSeconds(durationSeconds);
                        }

                        // For SoundCloud, the URL in flat-playlist output is the full URL
                        // We need to extract just the path part
                        var urlPath = ExtractUrlPath(url);
                        if (string.IsNullOrEmpty(urlPath))
                        {
                            // Fallback: use the ID field if URL extraction fails
                            urlPath = entry["id"]?.ToString();
                        }

                        if (!string.IsNullOrEmpty(urlPath))
                        {
                            songs.Add(new Song
                            {
                                Id = urlPath,
                                Name = title,
                                Length = length,
                                Source = Source.SoundCloud
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"{LogPrefix}: Error getting playlist songs: {ex.Message}");
            }

            return songs;
        }

        /// <summary>
        /// Downloads a SoundCloud track to the specified path using yt-dlp
        /// </summary>
        public bool DownloadSong(Song song, string path, CancellationToken cancellationToken, bool isPreview = false)
        {
            if (string.IsNullOrWhiteSpace(_ytDlpPath) || string.IsNullOrWhiteSpace(_ffmpegPath))
            {
                Logger.Error($"{LogPrefix}: yt-dlp or ffmpeg path not configured. Cannot download from SoundCloud.");
                return false;
            }

            if (!File.Exists(_ytDlpPath))
            {
                Logger.Error($"{LogPrefix}: yt-dlp not found at: {_ytDlpPath}");
                return false;
            }

            if (!File.Exists(_ffmpegPath))
            {
                Logger.Error($"{LogPrefix}: FFmpeg not found at: {_ffmpegPath}");
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

                // Build full SoundCloud URL from the path stored in song.Id
                var fullUrl = SoundCloudBaseUrl + song.Id;
                var pathWithoutExt = Path.ChangeExtension(path, null);

                // Build yt-dlp arguments (similar to YouTube but without YouTube-specific options)
                var quality = isPreview ? "5" : "0";

                // Post-processor args to ensure SDL_mixer compatibility
                var postProcessorArgs = isPreview
                    ? " --postprocessor-args \"ffmpeg:-ar 48000 -ac 2 -t 30\""
                    : " --postprocessor-args \"ffmpeg:-ar 48000 -ac 2\"";

                var previewFlags = isPreview
                    ? " --no-playlist --no-warnings --quiet --no-progress"
                    : " --no-playlist";

                // Rate limiting to be polite to SoundCloud
                var rateLimitOptions = " --sleep-requests 1 --sleep-interval 1 --max-sleep-interval 3";

                var arguments = $"-x --audio-format mp3 --audio-quality {quality}{rateLimitOptions}{postProcessorArgs}{previewFlags} --ffmpeg-location=\"{_ffmpegPath}\" -o \"{pathWithoutExt}.%(ext)s\" \"{fullUrl}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        Logger.Error($"{LogPrefix}: Failed to start yt-dlp process");
                        return false;
                    }

                    // Read output streams
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();

                    // Wait with timeout
                    var timeout = isPreview ? 60000 : 300000; // 1 min for preview, 5 min for full
                    process.WaitForExit(timeout);

                    if (process.ExitCode != 0)
                    {
                        Logger.Error($"{LogPrefix}: yt-dlp failed with exit code {process.ExitCode}");
                        if (!string.IsNullOrEmpty(error))
                        {
                            Logger.Error($"{LogPrefix}: Error output: {error}");
                        }
                        return false;
                    }

                    // Check if file was created
                    var expectedPath = pathWithoutExt + ".mp3";
                    if (File.Exists(expectedPath))
                    {
                        // Rename to target path if different
                        if (!expectedPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                        {
                            if (File.Exists(path))
                                File.Delete(path);
                            File.Move(expectedPath, path);
                        }
                        return true;
                    }

                    // Try to find the downloaded file with any extension
                    var downloadedFile = FindDownloadedFile(pathWithoutExt);
                    if (!string.IsNullOrEmpty(downloadedFile))
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                        File.Move(downloadedFile, path);
                        return true;
                    }

                    Logger.Error($"{LogPrefix}: Download completed but file not found at expected location");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"{LogPrefix}: Error downloading song: {ex.Message}");
                _errorHandler?.HandleError(ex, $"downloading '{song.Name}' from SoundCloud", showUserMessage: false);
                return false;
            }
        }

        /// <summary>
        /// Extracts the URL path from a full SoundCloud URL
        /// </summary>
        private static string ExtractUrlPath(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            // Handle full URLs like "https://soundcloud.com/artist/track-name"
            if (url.StartsWith(SoundCloudBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                return url.Substring(SoundCloudBaseUrl.Length).TrimStart('/');
            }

            // Handle protocol-relative URLs
            if (url.StartsWith("//soundcloud.com/", StringComparison.OrdinalIgnoreCase))
            {
                return url.Substring("//soundcloud.com/".Length).TrimStart('/');
            }

            // Already a path
            if (!url.Contains("://"))
            {
                return url.TrimStart('/');
            }

            return null;
        }

        /// <summary>
        /// Searches for a downloaded file with various audio extensions
        /// </summary>
        private static string FindDownloadedFile(string pathWithoutExt)
        {
            var extensions = new[] { ".mp3", ".m4a", ".opus", ".webm", ".ogg", ".wav" };
            foreach (var ext in extensions)
            {
                var candidate = pathWithoutExt + ext;
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }
    }
}
