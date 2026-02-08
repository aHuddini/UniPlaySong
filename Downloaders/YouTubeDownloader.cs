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
    // YouTube downloader (requires yt-dlp and ffmpeg)
    public class YouTubeDownloader : IDownloader
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "YouTubeDownloader";
        private const string YouTubeBaseUrl = "https://www.youtube.com";

        private readonly HttpClient _httpClient;
        private readonly string _ytDlpPath;
        private readonly string _ffmpegPath;
        private readonly bool _useFirefoxCookies;
        private readonly ErrorHandlerService _errorHandler;

        public YouTubeDownloader(HttpClient httpClient, string ytDlpPath, string ffmpegPath, bool useFirefoxCookies, ErrorHandlerService errorHandler)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ytDlpPath = ytDlpPath;
            _ffmpegPath = ffmpegPath;
            _useFirefoxCookies = useFirefoxCookies;
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
                
                Logger.DebugIf(LogPrefix,$"Searching YouTube for: {searchQuery}");

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

                Logger.DebugIf(LogPrefix,$"Found {albums.Count} playlists for '{gameName}' on YouTube");
                
                if (albums.Count == 0)
                {
                    Logger.Warn($"No playlists found for '{gameName}' on YouTube. Search query was: '{searchQuery}'");
                }
            }
            catch (OperationCanceledException)
            {
                Logger.DebugIf(LogPrefix,$"YouTube search cancelled for: {gameName}");
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

                Logger.DebugIf(LogPrefix,$"Loading YouTube playlist: {album.Name}");

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

                Logger.DebugIf(LogPrefix,$"Found {songs.Count} songs in playlist '{album.Name}'");
            }
            catch (OperationCanceledException)
            {
                Logger.DebugIf(LogPrefix,$"Playlist loading cancelled for: {album?.Name}");
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

            if (!File.Exists(_ffmpegPath))
            {
                Logger.Error($"FFmpeg not found at: {_ffmpegPath}");
                return false;
            }

            // Log diagnostic information
            Logger.DebugIf(LogPrefix,$"Download diagnostic - Song: {song?.Name}, Video ID: {song?.Id}, Target path: {path}");
            Logger.DebugIf(LogPrefix,$"Download diagnostic - yt-dlp path: {_ytDlpPath}, FFmpeg path: {_ffmpegPath}");
            
            // Check for JavaScript runtime (required for yt-dlp 2025.11.12+)
            // yt-dlp will automatically detect Deno, Node.js, QuickJS, etc. if installed
            // We'll check yt-dlp output for hints about JS runtime usage
            // Reference: https://github.com/yt-dlp/yt-dlp/issues/15012
            
            // Validate FFmpeg is accessible
            try
            {
                var ffmpegInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var ffmpegCheck = System.Diagnostics.Process.Start(ffmpegInfo))
                {
                    if (ffmpegCheck != null)
                    {
                        ffmpegCheck.WaitForExit(3000);
                        if (ffmpegCheck.ExitCode == 0)
                        {
                            var versionOutput = ffmpegCheck.StandardOutput.ReadToEnd();
                            var firstLine = versionOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(firstLine))
                            {
                                Logger.DebugIf(LogPrefix,$"FFmpeg version check: {firstLine.Trim()}");
                            }
                        }
                        else
                        {
                            Logger.Warn($"FFmpeg version check failed with exit code {ffmpegCheck.ExitCode}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not verify FFmpeg version: {ex.Message}");
            }

            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var pathWithoutExt = Path.ChangeExtension(path, null);
                string arguments;
                
                // Rate limiting options to avoid YouTube's aggressive bot detection
                // --sleep-requests: seconds to sleep between requests to YouTube API (prevents 429 errors)
                // --sleep-interval/--max-sleep-interval: random delay before each download
                // These help prevent rate limiting during batch downloads
                var rateLimitOptions = " --sleep-requests 1 --sleep-interval 2 --max-sleep-interval 5";

                if (_useFirefoxCookies)
                {
                    // Simplified command when using Firefox cookies - extract audio to MP3
                    // Minimal options: cookies, extract audio, MP3 format, FFmpeg location, output path, and URL
                    // Post-processor args ensure SDL_mixer compatibility (48kHz stereo, matches normalization)
                    var cookiesPostArgs = isPreview
                        ? " --postprocessor-args \"ffmpeg:-ar 48000 -ac 2 -t 30\""
                        : " --postprocessor-args \"ffmpeg:-ar 48000 -ac 2\"";
                    arguments = $"--cookies-from-browser firefox -x --audio-format mp3{rateLimitOptions}{cookiesPostArgs} --ffmpeg-location=\"{_ffmpegPath}\" -o \"{pathWithoutExt}.%(ext)s\" {YouTubeBaseUrl}/watch?v={song.Id}";
                    Logger.DebugIf(LogPrefix,"Using simplified yt-dlp command with Firefox cookies");
                }
                else
                {
                    // Full command with all options when not using cookies
                    // yt-dlp arguments: extract audio, mp3 format, output path
                    // For previews: use lower quality (5 = ~128kbps), limit to 30 seconds, and optimize for speed
                    // For full downloads: use best quality (0 = best available)
                    var quality = isPreview ? "5" : "0"; // 5 = ~128kbps, 0 = best

                    // Anti-bot detection options to help bypass YouTube's bot checks
                    // Try multiple clients in order: android (best), ios (good), web (fallback)
                    // This helps bypass YouTube's aggressive bot detection, especially in regions like Germany
                    var antiBotOptions = " --extractor-args \"youtube:player_client=android,ios,web\"";

                    // Post-processor args to ensure SDL_mixer compatibility
                    // -ar 48000: Resample to 48kHz (matches normalization settings for consistency)
                    // -ac 2: Convert to stereo (2 channels)
                    // This fixes issues where unusual sample rates or channel configs cause SDL "Out of memory" errors
                    // For previews: also limit to 30 seconds with -t 30
                    var postProcessorArgs = isPreview
                        ? " --postprocessor-args \"ffmpeg:-ar 48000 -ac 2 -t 30\""
                        : " --postprocessor-args \"ffmpeg:-ar 48000 -ac 2\"";

                    // For previews, add optimization flags for faster downloads
                    var previewFlags = isPreview
                        ? " --no-playlist --no-warnings --quiet --no-progress"
                        : " --no-playlist";

                    arguments = $"-x --audio-format mp3 --audio-quality {quality}{antiBotOptions}{rateLimitOptions}{postProcessorArgs}{previewFlags} --ffmpeg-location=\"{_ffmpegPath}\" -o \"{pathWithoutExt}.%(ext)s\" {YouTubeBaseUrl}/watch?v={song.Id}";
                }

                // Check directory permissions before starting download
                var targetDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    try
                    {
                        if (!Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                            Logger.DebugIf(LogPrefix,$"Created target directory: {targetDir}");
                        }
                        
                        // Test write permissions
                        var testFile = Path.Combine(targetDir, ".writetest");
                        try
                        {
                            File.WriteAllText(testFile, "test");
                            File.Delete(testFile);
                            Logger.DebugIf(LogPrefix,$"Directory write permissions verified: {targetDir}");
                        }
                        catch (Exception permEx)
                        {
                            Logger.Error($"Directory write permission check failed for {targetDir}: {permEx.Message}");
                        }
                    }
                    catch (Exception dirEx)
                    {
                        Logger.Error($"Failed to create/access target directory {targetDir}: {dirEx.Message}");
                    }
                }

                var workingDir = Path.GetDirectoryName(_ytDlpPath) ?? Directory.GetCurrentDirectory();
                Logger.DebugIf(LogPrefix,$"Downloading song with yt-dlp: {song.Name} to {path}");
                Logger.DebugIf(LogPrefix,$"yt-dlp command: {_ytDlpPath} {arguments}");
                Logger.DebugIf(LogPrefix,$"Working directory: {workingDir}");

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                // Declare output/error outside using block so they're accessible after process completes
                string output = null;
                string error = null;

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        Logger.Error("Failed to start yt-dlp process");
                        return false;
                    }

                    // Read streams synchronously to avoid mixing async/sync operations
                    // Use Task.Run to prevent blocking the UI thread while reading streams
                    
                    var readOutputTask = Task.Run(() => process.StandardOutput.ReadToEnd());
                    var readErrorTask = Task.Run(() => process.StandardError.ReadToEnd());

                    while (!process.WaitForExit(100))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch { }
                            
                            // Wait for stream reading to complete after killing process
                            try
                            {
                                readOutputTask.Wait(1000);
                                readErrorTask.Wait(1000);
                            }
                            catch { }
                            
                            Logger.DebugIf(LogPrefix,$"Download cancelled for: {song.Name}");
                            return false;
                        }
                    }

                    // Wait for stream reading to complete
                    readOutputTask.Wait();
                    readErrorTask.Wait();
                    output = readOutputTask.Result;
                    error = readErrorTask.Result;

                    // Check for JavaScript runtime usage in output (yt-dlp logs this when using Deno/Node/etc)
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        var outputLower = output.ToLowerInvariant();
                        
                        // Check for explicit JS runtime usage indicators
                        if (outputLower.Contains("[jsc:deno]") || outputLower.Contains("solving js challenges using deno"))
                        {
                            Logger.DebugIf(LogPrefix,"JavaScript runtime detected: Deno is working! (solving JS challenges)");
                        }
                        else if (outputLower.Contains("[jsc:node]") || (outputLower.Contains("using") && outputLower.Contains("node")))
                        {
                            Logger.DebugIf(LogPrefix,"JavaScript runtime detected: Node.js is working!");
                        }
                        else if (outputLower.Contains("[jsc:quickjs]") || (outputLower.Contains("using") && outputLower.Contains("quickjs")))
                        {
                            Logger.DebugIf(LogPrefix,"JavaScript runtime detected: QuickJS is working!");
                        }
                        else if (outputLower.Contains("[jsc:bun]") || (outputLower.Contains("using") && outputLower.Contains("bun")))
                        {
                            Logger.DebugIf(LogPrefix,"JavaScript runtime detected: Bun is working!");
                        }
                        else if (outputLower.Contains("deprecated") || outputLower.Contains("no js runtime") || outputLower.Contains("without js"))
                        {
                            Logger.Warn("⚠ yt-dlp appears to be running without JavaScript runtime - downloads may be limited or fail");
                            Logger.Warn("Install Deno for best results: https://deno.com/");
                        }
                        else
                        {
                            // Log a sample of output to help debug
                            var firstLines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Take(3);
                            Logger.DebugIf(LogPrefix,$"yt-dlp output preview: {string.Join(" | ", firstLines)}");
                        }
                    }
                    else
                    {
                        Logger.DebugIf(LogPrefix,"yt-dlp produced no standard output (this is normal for some operations)");
                    }

                    if (process.ExitCode != 0)
                    {
                        Logger.Error($"yt-dlp failed with exit code {process.ExitCode} for song '{song?.Name}' (Video ID: {song?.Id})");
                        
                        // Log full error output
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            Logger.Error($"yt-dlp error output:\n{error}");
                            
                            // Parse common error patterns and provide helpful diagnostics
                            var errorLower = error.ToLowerInvariant();
                            if (errorLower.Contains("sign in to confirm") || errorLower.Contains("not a bot") || errorLower.Contains("bot"))
                            {
                                Logger.Error("Diagnosis: YouTube bot detection - YouTube is blocking the download");
                                
                                Logger.Error("");
                                Logger.Error("CRITICAL: yt-dlp 2025.11.12+ now REQUIRES a JavaScript runtime for YouTube downloads!");
                                Logger.Error("Without a JS runtime, YouTube downloads are deprecated and will fail with bot detection.");
                                Logger.Error("");
                                Logger.Error("Solution 1 (MOST IMPORTANT): Install Deno (recommended JavaScript runtime):");
                                Logger.Error("  - Download from: https://deno.com/ or https://github.com/denoland/deno/releases");
                                Logger.Error("  - Install Deno (get 'deno' NOT 'denort' from GitHub releases)");
                                Logger.Error("  - Minimum version: Deno 2.0.0 (latest version recommended)");
                                Logger.Error("  - After installation, yt-dlp will automatically detect and use Deno");
                                Logger.Error("");
                                Logger.Error("Alternative JS runtimes (if Deno doesn't work):");
                                Logger.Error("  - Node.js: https://nodejs.org/ (minimum v20.0.0, v25+ recommended)");
                                Logger.Error("  - QuickJS: https://bellard.org/quickjs/ (minimum 2023-12-9, 2025-4-26+ recommended)");
                                Logger.Error("");
                                Logger.Error("Solution 2: Update yt-dlp to the latest version:");
                                Logger.Error("  - Open Command Prompt/PowerShell");
                                Logger.Error("  - Navigate to your yt-dlp directory");
                                Logger.Error("  - Run: yt-dlp.exe -U");
                                Logger.Error("");
                                Logger.Error("Solution 3: Wait 10-15 minutes and try again (YouTube rate limiting)");
                                Logger.Error("");
                                Logger.Error("Reference: https://github.com/yt-dlp/yt-dlp/issues/15012");
                            }
                            else if (errorLower.Contains("unable to download") || errorLower.Contains("http error") || errorLower.Contains("network"))
                            {
                                Logger.Error("Diagnosis: Network/HTTP error - check internet connection or YouTube availability");
                            }
                            else if (errorLower.Contains("ffmpeg") || errorLower.Contains("postprocessor"))
                            {
                                Logger.Error($"Diagnosis: FFmpeg-related error - verify FFmpeg is working at: {_ffmpegPath}");
                            }
                            else if (errorLower.Contains("private video") || errorLower.Contains("unavailable"))
                            {
                                Logger.Error("Diagnosis: Video is private or unavailable");
                            }
                            else if (errorLower.Contains("permission") || errorLower.Contains("access denied"))
                            {
                                Logger.Error($"Diagnosis: File system permission error - check write access to: {Path.GetDirectoryName(path)}");
                            }
                            else if (errorLower.Contains("disk") || errorLower.Contains("space"))
                            {
                                Logger.Error("Diagnosis: Disk space issue - check available disk space");
                            }
                        }
                        else
                        {
                            Logger.Warn("yt-dlp error output was empty - this may indicate a process startup issue");
                        }
                        
                        // Log standard output if available (sometimes errors go to stdout)
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            Logger.DebugIf(LogPrefix,$"yt-dlp standard output:\n{output}");
                        }
                        
                        // Log the exact command that failed for troubleshooting
                        Logger.Error($"Failed command: {_ytDlpPath} {arguments}");
                        Logger.Error($"Working directory: {processInfo.WorkingDirectory}");
                        Logger.Error($"Target path: {path}");
                        
                        return false;
                    }
                    
                    // Log successful completion details
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Logger.DebugIf(LogPrefix,$"yt-dlp output: {output}");
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
                            Logger.DebugIf(LogPrefix,$"Found file with extension {ext}, moving to {path}");
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
                            Logger.DebugIf(LogPrefix,$"Found file with pattern match: {foundFile}, moving to {path}");
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
                    var successMessage = $"Successfully downloaded: {song.Name} to {path} ({fileInfo.Length} bytes)";
                    
                    // Add context about what helped the download succeed
                    var successDetails = new List<string>();
                    if (!string.IsNullOrWhiteSpace(output) && output.ToLowerInvariant().Contains("using") && 
                        (output.ToLowerInvariant().Contains("deno") || output.ToLowerInvariant().Contains("node") || 
                         output.ToLowerInvariant().Contains("quickjs") || output.ToLowerInvariant().Contains("bun")))
                    {
                        successDetails.Add("JavaScript runtime active");
                    }
                    
                    if (successDetails.Any())
                    {
                        successMessage += $" ({string.Join(", ", successDetails)})";
                    }

                    Logger.DebugIf(LogPrefix,successMessage);
                    return true;
                }
                
                Logger.Error($"Download failed: File not found at {path} after yt-dlp completed successfully");
                Logger.Error($"This may indicate yt-dlp created the file with a different name or extension");
                
                // List files in directory for debugging
                var debugDir = Path.GetDirectoryName(path);
                if (Directory.Exists(debugDir))
                {
                    var files = Directory.GetFiles(debugDir);
                    Logger.DebugIf(LogPrefix,$"Files in directory ({files.Length} total): {string.Join(", ", files.Select(f => Path.GetFileName(f)))}");
                    
                    // Check for files created around the same time (within last 2 minutes)
                    var recentFiles = files.Where(f => 
                    {
                        try
                        {
                            var fileTime = File.GetLastWriteTime(f);
                            return (DateTime.Now - fileTime).TotalMinutes < 2;
                        }
                        catch { return false; }
                    }).ToList();
                    
                    if (recentFiles.Any())
                    {
                        Logger.DebugIf(LogPrefix,$"Recently created files (last 2 minutes): {string.Join(", ", recentFiles.Select(f => Path.GetFileName(f)))}");
                    }
                }
                else
                {
                    Logger.Error($"Target directory does not exist: {debugDir}");
                }
                
                return false;
            }
            catch (OperationCanceledException)
            {
                Logger.DebugIf(LogPrefix,$"Download cancelled for: {song?.Name}");
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

