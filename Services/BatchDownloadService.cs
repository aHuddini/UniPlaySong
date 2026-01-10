using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Downloaders;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Result of a single game download operation
    /// </summary>
    public class GameDownloadResult
    {
        public Game Game { get; set; }
        public bool Success { get; set; }
        public string AlbumName { get; set; }
        public string SourceName { get; set; }
        public string SongPath { get; set; }
        public string ErrorMessage { get; set; }
        public bool WasSkipped { get; set; }
        public string SkipReason { get; set; }
    }

    /// <summary>
    /// Result of a batch download operation containing both successes and failures
    /// </summary>
    public class BatchDownloadResult
    {
        public List<string> DownloadedFiles { get; set; } = new List<string>();
        public List<GameDownloadResult> FailedGames { get; set; } = new List<GameDownloadResult>();
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
    }

    /// <summary>
    /// Callback for reporting individual game download progress
    /// </summary>
    /// <param name="gameName">Name of the game</param>
    /// <param name="status">Current download status</param>
    /// <param name="message">Status message</param>
    /// <param name="albumName">Album name if found</param>
    /// <param name="sourceName">Source name (KHInsider or YouTube)</param>
    public delegate void GameDownloadProgressCallback(string gameName, BatchDownloadStatus status, string message, string albumName = null, string sourceName = null);

    /// <summary>
    /// Service for batch downloading music for multiple games with parallel support
    /// </summary>
    public class BatchDownloadService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly IDownloadManager _downloadManager;
        private readonly GameMusicFileService _fileService;
        private readonly ErrorHandlerService _errorHandler;

        /// <summary>
        /// Maximum number of concurrent downloads (default: 3)
        /// </summary>
        public int MaxConcurrentDownloads { get; set; } = 3;

        public BatchDownloadService(
            IDownloadManager downloadManager,
            GameMusicFileService fileService,
            ErrorHandlerService errorHandler = null)
        {
            _downloadManager = downloadManager ?? throw new ArgumentNullException(nameof(downloadManager));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _errorHandler = errorHandler;
        }

        /// <summary>
        /// Downloads music for multiple games in parallel
        /// </summary>
        /// <param name="games">Games to download music for</param>
        /// <param name="source">Download source (KHInsider, YouTube, or All)</param>
        /// <param name="overwrite">Whether to overwrite existing music</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="progressCallback">Callback for reporting per-game progress</param>
        /// <returns>List of download results for each game</returns>
        public async Task<List<GameDownloadResult>> DownloadMusicParallelAsync(
            List<Game> games,
            Source source,
            bool overwrite,
            CancellationToken cancellationToken,
            GameDownloadProgressCallback progressCallback = null)
        {
            if (games == null || games.Count == 0)
                return new List<GameDownloadResult>();

            Logger.Info($"[BatchDownload] Starting parallel download for {games.Count} games (max concurrent: {MaxConcurrentDownloads})");

            var results = new List<GameDownloadResult>();
            var resultsLock = new object();

            // Use SemaphoreSlim for concurrency control
            using (var semaphore = new SemaphoreSlim(MaxConcurrentDownloads))
            {
                var tasks = games.Select(async game =>
                {
                    // Wait for semaphore slot
                    await semaphore.WaitAsync(cancellationToken);

                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            var cancelResult = new GameDownloadResult
                            {
                                Game = game,
                                Success = false,
                                WasSkipped = true,
                                SkipReason = "Cancelled"
                            };
                            lock (resultsLock) { results.Add(cancelResult); }
                            progressCallback?.Invoke(game.Name, BatchDownloadStatus.Cancelled, "Cancelled", null, null);
                            return;
                        }

                        // Check if game already has music
                        var musicDir = _fileService.GetGameMusicDirectory(game);
                        if (!overwrite && Directory.Exists(musicDir) && Directory.GetFiles(musicDir, "*.mp3").Length > 0)
                        {
                            Logger.Info($"[BatchDownload] Skipping '{game.Name}' - already has music");
                            var skipResult = new GameDownloadResult
                            {
                                Game = game,
                                Success = false,
                                WasSkipped = true,
                                SkipReason = "Already has music"
                            };
                            lock (resultsLock) { results.Add(skipResult); }
                            progressCallback?.Invoke(game.Name, BatchDownloadStatus.Skipped, "Already has music", null, null);
                            return;
                        }

                        // Report downloading status
                        progressCallback?.Invoke(game.Name, BatchDownloadStatus.Downloading, "Searching for music...", null, null);

                        // Perform the download
                        var result = await Task.Run(() => DownloadMusicForGameInternal(game, source, cancellationToken), cancellationToken);

                        lock (resultsLock) { results.Add(result); }

                        if (result.Success)
                        {
                            var sourceInfo = !string.IsNullOrEmpty(result.SourceName) ? $" ({result.SourceName})" : "";
                            progressCallback?.Invoke(game.Name, BatchDownloadStatus.Completed, $"Downloaded{sourceInfo}", result.AlbumName, result.SourceName);
                        }
                        else if (result.WasSkipped)
                        {
                            progressCallback?.Invoke(game.Name, BatchDownloadStatus.Skipped, result.SkipReason, null, null);
                        }
                        else
                        {
                            progressCallback?.Invoke(game.Name, BatchDownloadStatus.Failed, result.ErrorMessage ?? "No music found", null, null);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        var cancelResult = new GameDownloadResult
                        {
                            Game = game,
                            Success = false,
                            WasSkipped = true,
                            SkipReason = "Cancelled"
                        };
                        lock (resultsLock) { results.Add(cancelResult); }
                        progressCallback?.Invoke(game.Name, BatchDownloadStatus.Cancelled, "Cancelled", null, null);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"[BatchDownload] Error downloading for '{game.Name}': {ex.Message}");
                        var errorResult = new GameDownloadResult
                        {
                            Game = game,
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                        lock (resultsLock) { results.Add(errorResult); }
                        progressCallback?.Invoke(game.Name, BatchDownloadStatus.Failed, ex.Message, null, null);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                // Wait for all tasks to complete
                await Task.WhenAll(tasks);
            }

            // Log summary
            var successCount = results.Count(r => r.Success);
            var skipCount = results.Count(r => r.WasSkipped);
            var failCount = results.Count(r => !r.Success && !r.WasSkipped);
            Logger.Info($"[BatchDownload] Complete: {successCount} succeeded, {skipCount} skipped, {failCount} failed");

            return results;
        }

        /// <summary>
        /// Internal method to download music for a single game (auto-mode)
        /// </summary>
        private GameDownloadResult DownloadMusicForGameInternal(Game game, Source source, CancellationToken cancellationToken)
        {
            var result = new GameDownloadResult { Game = game };

            try
            {
                Logger.Info($"[BatchDownload] Processing '{game.Name}'");

                // Get albums (auto mode)
                var albums = _downloadManager.GetAlbumsForGame(game.Name, source, cancellationToken, auto: true);
                var albumsList = albums?.ToList() ?? new List<Album>();

                if (albumsList.Count == 0)
                {
                    Logger.Info($"[BatchDownload] No albums found for '{game.Name}'");
                    result.ErrorMessage = "No albums found";
                    return result;
                }

                // Pick best album (auto mode)
                var bestAlbum = _downloadManager.BestAlbumPick(albumsList, game);
                if (bestAlbum == null)
                {
                    Logger.Info($"[BatchDownload] No suitable album for '{game.Name}'");
                    result.ErrorMessage = "No suitable album match";
                    return result;
                }

                result.AlbumName = bestAlbum.Name;
                result.SourceName = GetSourceDisplayName(bestAlbum.Source);
                Logger.Info($"[BatchDownload] Selected album '{bestAlbum.Name}' from {result.SourceName} for '{game.Name}'");

                // Get songs from album
                var songs = _downloadManager.GetSongsFromAlbum(bestAlbum, cancellationToken);
                var songsList = songs?.ToList() ?? new List<Song>();

                if (songsList.Count == 0)
                {
                    Logger.Info($"[BatchDownload] No songs in album '{bestAlbum.Name}'");
                    result.ErrorMessage = "Album has no songs";
                    return result;
                }

                // Pick best song (auto mode)
                var bestSongs = _downloadManager.BestSongPick(songsList, game.Name, maxSongs: 1);
                if (bestSongs == null || bestSongs.Count == 0)
                {
                    Logger.Info($"[BatchDownload] No suitable song for '{game.Name}'");
                    result.ErrorMessage = "No suitable song found";
                    return result;
                }

                var songToDownload = bestSongs.First();
                Logger.Info($"[BatchDownload] Selected song '{songToDownload.Name}' for '{game.Name}'");

                // Ensure music directory exists
                var musicDir = _fileService.GetGameMusicDirectory(game);
                if (!Directory.Exists(musicDir))
                {
                    Directory.CreateDirectory(musicDir);
                }

                // Generate download path
                var safeFileName = Common.StringHelper.CleanForPath(songToDownload.Name);
                var extension = Path.GetExtension(songToDownload.Id);
                if (string.IsNullOrEmpty(extension) || extension == songToDownload.Id)
                {
                    extension = ".mp3";
                }
                var downloadPath = Path.Combine(musicDir, safeFileName + extension);

                // Download the song
                var downloadSuccess = _downloadManager.DownloadSong(songToDownload, downloadPath, cancellationToken);

                if (downloadSuccess && File.Exists(downloadPath))
                {
                    result.Success = true;
                    result.SongPath = downloadPath;
                    Logger.Info($"[BatchDownload] Successfully downloaded '{songToDownload.Name}' for '{game.Name}'");
                }
                else
                {
                    result.ErrorMessage = "Download failed";
                    Logger.Warn($"[BatchDownload] Download failed for '{game.Name}'");
                }
            }
            catch (OperationCanceledException)
            {
                result.WasSkipped = true;
                result.SkipReason = "Cancelled";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[BatchDownload] Error processing '{game.Name}': {ex.Message}");
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Downloads music for multiple games sequentially (for backward compatibility)
        /// </summary>
        public List<GameDownloadResult> DownloadMusicSequential(
            List<Game> games,
            Source source,
            bool overwrite,
            CancellationToken cancellationToken,
            GameDownloadProgressCallback progressCallback = null,
            int delayBetweenGamesMs = 1000)
        {
            var results = new List<GameDownloadResult>();

            foreach (var game in games)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    results.Add(new GameDownloadResult
                    {
                        Game = game,
                        Success = false,
                        WasSkipped = true,
                        SkipReason = "Cancelled"
                    });
                    progressCallback?.Invoke(game.Name, BatchDownloadStatus.Cancelled, "Cancelled", null, null);
                    continue;
                }

                // Check if game already has music
                var musicDir = _fileService.GetGameMusicDirectory(game);
                if (!overwrite && Directory.Exists(musicDir) && Directory.GetFiles(musicDir, "*.mp3").Length > 0)
                {
                    results.Add(new GameDownloadResult
                    {
                        Game = game,
                        Success = false,
                        WasSkipped = true,
                        SkipReason = "Already has music"
                    });
                    progressCallback?.Invoke(game.Name, BatchDownloadStatus.Skipped, "Already has music", null, null);
                    continue;
                }

                progressCallback?.Invoke(game.Name, BatchDownloadStatus.Downloading, "Downloading...", null, null);

                var result = DownloadMusicForGameInternal(game, source, cancellationToken);
                results.Add(result);

                if (result.Success)
                {
                    var sourceInfo = !string.IsNullOrEmpty(result.SourceName) ? $" ({result.SourceName})" : "";
                    progressCallback?.Invoke(game.Name, BatchDownloadStatus.Completed, $"Downloaded{sourceInfo}", result.AlbumName, result.SourceName);
                }
                else if (result.WasSkipped)
                {
                    progressCallback?.Invoke(game.Name, BatchDownloadStatus.Skipped, result.SkipReason, null, null);
                }
                else
                {
                    progressCallback?.Invoke(game.Name, BatchDownloadStatus.Failed, result.ErrorMessage ?? "No music found", null, null);
                }

                // Rate limiting delay
                if (delayBetweenGamesMs > 0 && !cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(delayBetweenGamesMs);
                }
            }

            return results;
        }

        /// <summary>
        /// Gets display name for a source
        /// </summary>
        private static string GetSourceDisplayName(Source source)
        {
            switch (source)
            {
                case Source.KHInsider:
                    return "KHInsider";
                case Source.Zophar:
                    return "Zophar";
                case Source.YouTube:
                    return "YouTube";
                default:
                    return source.ToString();
            }
        }
    }
}
