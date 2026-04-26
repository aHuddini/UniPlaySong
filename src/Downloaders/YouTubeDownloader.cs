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
        private string _ytDlpPath;
        private string _ffmpegPath;
        private CookieMode _cookieMode;
        private string _customCookiesFilePath;
        private readonly ErrorHandlerService _errorHandler;

        // FFmpeg version-check cache. Probing ffmpeg -version on every DownloadSong call
        // adds ~30-50ms per song; for a 50-track preview session that's 1.5-2.5s of
        // redundant work probing the same binary. Cache by (path, last-write-time) so
        // an in-place ffmpeg update busts the cache. Reset on UpdateSettings when the
        // user picks a different ffmpeg path.
        private string _ffmpegProbedPath = null;
        private long _ffmpegProbedMtimeTicks = 0;

        public YouTubeDownloader(HttpClient httpClient, string ytDlpPath, string ffmpegPath, CookieMode cookieMode, string customCookiesFilePath, ErrorHandlerService errorHandler)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ytDlpPath = ytDlpPath;
            _ffmpegPath = ffmpegPath;
            _cookieMode = cookieMode;
            _customCookiesFilePath = customCookiesFilePath ?? string.Empty;
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        }

        // Live-updates the settings-driven fields without recreating the downloader.
        // Called by DownloadManager.UpdateSettings when the user changes yt-dlp path /
        // ffmpeg path / cookie mode / custom cookies file in settings. Fixes a
        // stale-reference bug where dialog code paths (DownloadDialogService,
        // ControllerDialogHandler) held the old DownloadManager instance when settings
        // changed, so new YouTube download attempts went through the OLD config even after
        // the user fixed their configuration.
        public void UpdateSettings(string ytDlpPath, string ffmpegPath, CookieMode cookieMode, string customCookiesFilePath)
        {
            // Bust the ffmpeg version-check cache when the path changes so the new binary
            // gets probed on next download. mtime-based cache check inside the probe
            // handles the "user updated ffmpeg in place" case automatically.
            if (!string.Equals(_ffmpegPath, ffmpegPath, StringComparison.OrdinalIgnoreCase))
            {
                _ffmpegProbedPath = null;
                _ffmpegProbedMtimeTicks = 0;
            }

            _ytDlpPath = ytDlpPath;
            _ffmpegPath = ffmpegPath;
            _cookieMode = cookieMode;
            _customCookiesFilePath = customCookiesFilePath ?? string.Empty;
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
                // 20 results = ~1 page from YouTube; previously was 100 which paged 5-6 times
                // and added 3-5s of latency. Users rarely scroll past the top few playlists
                // anyway, and the plugin's BestAlbumPick scoring strongly favors top results.
                var results = client.Search(searchQuery, 20, cancellationToken);

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

            Logger.DebugIf(LogPrefix, $"Downloading {song?.Name} (id={song?.Id})");
            
            // Check for JavaScript runtime (required for yt-dlp 2025.11.12+)
            // yt-dlp will automatically detect Deno, Node.js, QuickJS, etc. if installed
            // We'll check yt-dlp output for hints about JS runtime usage
            // Reference: https://github.com/yt-dlp/yt-dlp/issues/15012
            
            // Validate FFmpeg is accessible. Cached by (path, mtime) so we only probe once
            // per binary per session — running ffmpeg -version on every DownloadSong call
            // was ~30-50ms of waste per song. mtime check catches in-place updates.
            long ffmpegMtimeTicks = 0;
            try { ffmpegMtimeTicks = File.GetLastWriteTimeUtc(_ffmpegPath).Ticks; } catch { }

            bool ffmpegAlreadyProbed =
                string.Equals(_ffmpegProbedPath, _ffmpegPath, StringComparison.OrdinalIgnoreCase)
                && _ffmpegProbedMtimeTicks == ffmpegMtimeTicks
                && ffmpegMtimeTicks != 0;

            if (!ffmpegAlreadyProbed)
            {
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

                                // Mark the cache only on success; failed probes should retry next time.
                                _ffmpegProbedPath = _ffmpegPath;
                                _ffmpegProbedMtimeTicks = ffmpegMtimeTicks;
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

                // Rate limiting:
                //   --sleep-requests 0.3: pauses 300ms between API calls within a single
                //     video extraction. Was 1.0s; reduced after observing zero 429s across
                //     multi-user testing. yt-dlp issue #7143 documents a user running
                //     --sleep-requests 0.5 for hours-long archive runs without 429s, so
                //     0.3s on user-paced downloads keeps comfortable margin while saving
                //     ~2.8s per full download (4 inter-call sleeps × 0.7s shaved each).
                //     yt-dlp does NOT inject automatic per-request sleeps when this flag
                //     is absent (verified in source: extractor/common.py _request_webpage
                //     uses `sleep_interval_requests or 0`).
                //   --sleep-interval / --max-sleep-interval: removed (was a random 1-3s
                //     pre-download delay; each yt-dlp process had its own sleep window so
                //     parallel processes didn't aggregate into a global rate-limit
                //     accumulator — pure overhead).
                //   PREVIEWS: no rate-limit flags. Each preview is 3 API calls; user-paced
                //     bursts are far under the documented guest-session ceiling.
                var rateLimitOptions = isPreview
                    ? string.Empty
                    : " --sleep-requests 0.3";

                // Concurrent fragments: YouTube serves audio in DASH fragments (~5s chunks).
                // -N 4 fetches up to 4 in parallel. Only meaningful for FULL downloads where
                // multi-minute audio is fragmented; previews are 40-second single-segment
                // HTTP range requests that don't fragment, so the flag is a no-op for them.
                // Bumped from 3 to 4 to test if the extra parallelism helps on full songs.
                // Combined with BatchDownloadService.MaxConcurrentDownloads=3, upper bound is
                // 3*4=12 simultaneous HTTPS connections — still well within reasonable.
                var concurrentFragments = isPreview ? string.Empty : " --concurrent-fragments 4";

                // Prefer free formats (opus/webm over m4a/aac at same quality). Affects
                // source-format selection BEFORE the --audio-format mp3 conversion; on
                // previews the source-format choice doesn't impact wall-clock meaningfully
                // (40-second clip), so dropped on the preview path. Kept on full downloads
                // where the slightly smaller container/decode does add up across an album.
                var preferFreeFormats = isPreview ? string.Empty : " --prefer-free-formats";

                // Anti-bot detection: player_client selection.
                //
                // COOKIE MODE: leave player_client unset entirely. yt-dlp auto-skips
                // android/ios when cookies are present (those clients don't carry cookies),
                // so forcing `android,ios,web` collapses to web-only and triggers the nsig JS
                // challenge — which fails outright for users without a JS runtime (Deno).
                // yt-dlp's own default rotation includes cookie-compatible clients
                // (web_safari etc.) and adapts as YouTube changes faster than we ship; trust it.
                //
                // NO-COOKIE MODE: explicitly select clients because yt-dlp's 2026 defaults
                // (android_vr,web,web_safari) were observed hitting 403/SABR walls in the
                // wild. The `android,ios,web` triple still produces working URLs without
                // cookies. Applied to both previews and full downloads — preview floor of
                // ~5-6s on this path is the architectural limit yt-dlp imposes (multi-client
                // negotiation + JS challenge solve), not something flag tweaks can lower.
                // Tracked: https://github.com/yt-dlp/yt-dlp/issues/12482
                string antiBotOptions = (_cookieMode != CookieMode.None)
                    ? string.Empty
                    : " --extractor-args \"youtube:player_client=android,ios,web\"";

                // Post-processor args ensure SDL_mixer / NAudio compatibility (-ar 48000 -ac 2).
                // Single-pass MP3 postprocessor: folds the SDL/NAudio compatibility resample
                // (-ar 48000 -ac 2) into yt-dlp's first encode pass. Saves ~1-2s per song
                // on full downloads versus the prior two-pass form (`ffmpeg:-ar 48000 -ac 2`,
                // which re-encoded the already-encoded mp3). Promoted to always-on after
                // shipping behind the experimental setting in this release with no bug
                // reports — the syntax is well-formed, ffmpeg accepts it, output plays.
                var postProcessorArgs = " --postprocessor-args \"ExtractAudio+ffmpeg_o:-ar 48000 -ac 2\"";

                // Always-on performance flags (no quality / compatibility impact):
                //   --no-progress: skip terminal progress rendering UPS doesn't read (~50-100ms).
                //   --no-mtime:    skip applying YouTube upload date as file mtime (~50ms).
                var performanceFlags = " --no-progress --no-mtime";

                if (_cookieMode != CookieMode.None)
                {
                    var quality = isPreview ? "5" : "0";
                    var cookiesSectionLimit = isPreview ? " --download-sections \"*0:00-0:40\"" : "";
                    string cookiesArg;
                    switch (_cookieMode)
                    {
                        case CookieMode.Firefox: cookiesArg = "--cookies-from-browser firefox"; break;
                        case CookieMode.Chrome:  cookiesArg = "--cookies-from-browser chrome"; break;
                        case CookieMode.Edge:    cookiesArg = "--cookies-from-browser edge"; break;
                        case CookieMode.Brave:   cookiesArg = "--cookies-from-browser brave"; break;
                        case CookieMode.Opera:   cookiesArg = "--cookies-from-browser opera"; break;
                        default:                 cookiesArg = $"--cookies \"{_customCookiesFilePath}\""; break;
                    }
                    arguments = $"{cookiesArg} -x --audio-format mp3 --audio-quality {quality}{antiBotOptions}{rateLimitOptions}{concurrentFragments}{preferFreeFormats}{postProcessorArgs}{performanceFlags}{cookiesSectionLimit} --no-playlist --ffmpeg-location=\"{_ffmpegPath}\" -o \"{pathWithoutExt}.%(ext)s\" {YouTubeBaseUrl}/watch?v={song.Id}";
                }
                else
                {
                    // Full command with all options when not using cookies
                    // yt-dlp arguments: extract audio, mp3 format, output path
                    // For previews: use lower quality (5 = ~128kbps), limit to 30 seconds, and optimize for speed
                    // For full downloads: use best quality (0 = best available)
                    var quality = isPreview ? "5" : "0"; // 5 = ~128kbps, 0 = best

                    // For previews: download only first 30 seconds instead of full track + trim
                    var sectionLimit = isPreview ? " --download-sections \"*0:00-0:40\"" : "";

                    // Keep stderr diagnostics visible even for previews — prior --no-warnings
                    // --quiet --no-progress combo silenced all output, so when YouTube bot
                    // detection / cookie / JS-runtime failures hit on preview-only flows
                    // (most common user interaction), there was nothing in the log to diagnose.
                    // --no-playlist prevents an entire channel being downloaded when the URL
                    // happens to be a playlist URL for a game soundtrack.
                    var previewFlags = " --no-playlist";

                    arguments = $"-x --audio-format mp3 --audio-quality {quality}{antiBotOptions}{rateLimitOptions}{concurrentFragments}{preferFreeFormats}{postProcessorArgs}{performanceFlags}{sectionLimit}{previewFlags} --ffmpeg-location=\"{_ffmpegPath}\" -o \"{pathWithoutExt}.%(ext)s\" {YouTubeBaseUrl}/watch?v={song.Id}";
                }

                // Ensure the target directory exists before yt-dlp tries to write to it.
                // The previous .writetest probe (write a sentinel, delete it, log result) was
                // dropped — yt-dlp itself surfaces a clear error if the directory is unwritable,
                // so the probe was duplicating disk I/O and AV-scanner work for no diagnostic
                // value the failed download wouldn't already provide.
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
                    }
                    catch (Exception dirEx)
                    {
                        Logger.Error($"Failed to create/access target directory {targetDir}: {dirEx.Message}");
                    }
                }

                var workingDir = Path.GetDirectoryName(_ytDlpPath) ?? Directory.GetCurrentDirectory();

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
                        // Non-error output dropped — verbose noise per song with no diagnostic value.
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
                            if (errorLower.Contains("failed to load python dll") || errorLower.Contains("pyi-") || errorLower.Contains("_internal"))
                            {
                                Logger.Error("Diagnosis: yt-dlp.exe appears corrupted or incomplete (Python DLL load failed).");
                                Logger.Error("");
                                Logger.Error("This usually means the wrong yt-dlp release was downloaded:");
                                Logger.Error("  - The folder/zip distribution (yt-dlp.zip, yt-dlp_min.zip) contains an _internal folder");
                                Logger.Error("    that can become corrupted, mismatched across updates, or quarantined by antivirus.");
                                Logger.Error("");
                                Logger.Error("Fix: Download the SINGLE-FILE yt-dlp.exe asset:");
                                Logger.Error("  1. Delete the current yt-dlp.exe AND any _internal folder next to it");
                                Logger.Error("  2. Go to: https://github.com/yt-dlp/yt-dlp/releases/latest");
                                Logger.Error("  3. Download the file named 'yt-dlp.exe' (~17 MB, NOT the .zip archives)");
                                Logger.Error("  4. Place it at the same path you had before");
                                Logger.Error("  5. Retry the download");
                                Logger.Error("");
                                Logger.Error("If the error persists, check Windows Defender for quarantined yt-dlp files,");
                                Logger.Error("and ensure the Visual C++ 2015-2022 Redistributable (x64) is installed.");
                            }
                            else if (errorLower.Contains("sign in to confirm") || errorLower.Contains("not a bot") || errorLower.Contains("bot"))
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
                    
                }

                // yt-dlp may add extension, check if file exists with various extensions.
                // .mp4 and .aac added for the fast-preview path (audio-format=m4a may yield
                // either depending on source format; .mp4 specifically when source is format
                // 18 and yt-dlp's audio-only extract didn't fully demux).
                if (!File.Exists(path))
                {
                    var pathWithoutExt2 = Path.ChangeExtension(path, null);
                    var possibleExtensions = new[] { ".mp3", ".m4a", ".mp4", ".aac", ".webm", ".opus" };

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

                    // Pattern-match fallback for unusual yt-dlp output filenames.
                    // ONLY runs if the explicit-extensions loop above didn't already move
                    // a file into place. Without this guard, the pattern match found the
                    // freshly-moved <hash>.mp3 from the loop above, deleted it as a "stale
                    // duplicate", then tried to move it from the (now-deleted) source path
                    // — surfacing as FileNotFoundException. Bug exposed by the v1.4.5
                    // preview-fast-path change which routinely produces .m4a output that
                    // exercises the rename fallback for the first time.
                    if (!File.Exists(path))
                    {
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
                }

                if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    // Parse yt-dlp's "Downloading 1 format(s): <N>" line so we know which
                    // format yt-dlp picked (18=mp4 video+audio, 140=m4a audio-only,
                    // 251=opus audio-only, etc). Useful when diagnosing PO Token issues
                    // or when YouTube changes default format selection.
                    string format = null;
                    if (!string.IsNullOrEmpty(output))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(output, @"Downloading\s+\d+\s+format\(s\):\s+(\S+)");
                        if (match.Success) format = match.Groups[1].Value;
                    }
                    var formatTag = string.IsNullOrEmpty(format) ? "" : $" [fmt {format}]";
                    Logger.DebugIf(LogPrefix, $"Downloaded {song.Name} ({fileInfo.Length} bytes){formatTag}");
                    return true;
                }
                
                Logger.Error($"Download failed: File not found at {path} after yt-dlp completed successfully");
                Logger.Error($"This may indicate yt-dlp created the file with a different name or extension");
                
                var debugDir = Path.GetDirectoryName(path);
                if (!Directory.Exists(debugDir))
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

