using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Downloaders;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    /// <summary>Single game download result</summary>
    public class GameDownloadResult
    {
        public Game Game { get; set; }
        public bool Success { get; set; }
        public string AlbumName { get; set; }
        public string AlbumId { get; set; }
        public string SourceName { get; set; }
        public string SongPath { get; set; }
        public string ErrorMessage { get; set; }
        public bool WasSkipped { get; set; }
        public string SkipReason { get; set; }
    }

    /// <summary>Batch download result with successes and failures</summary>
    public class BatchDownloadResult
    {
        public List<string> DownloadedFiles { get; set; } = new List<string>();
        public List<GameDownloadResult> FailedGames { get; set; } = new List<GameDownloadResult>();
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
    }

    /// <summary>Progress callback for batch downloads</summary>
    public delegate void GameDownloadProgressCallback(Game game, string gameName, BatchDownloadStatus status, string message, string albumName = null, string sourceName = null);

    /// <summary>Batch music download service with parallel support</summary>
    public class BatchDownloadService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly IDownloadManager _downloadManager;
        private readonly GameMusicFileService _fileService;
        private readonly ErrorHandlerService _errorHandler;

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

        /// <summary>Downloads music for multiple games in parallel</summary>
        public async Task<List<GameDownloadResult>> DownloadMusicParallelAsync(
            List<Game> games,
            Source source,
            bool overwrite,
            CancellationToken cancellationToken,
            GameDownloadProgressCallback progressCallback = null)
        {
            if (games == null || games.Count == 0)
                return new List<GameDownloadResult>();

            var results = new List<GameDownloadResult>();
            var resultsLock = new object();

            // Use SemaphoreSlim for concurrency control
            using (var semaphore = new SemaphoreSlim(MaxConcurrentDownloads))
            {
                var tasks = games.Select(async game =>
                {
                    bool acquiredSemaphore = false;
                    try
                    {
                        // Wait for semaphore slot - must be inside try to handle cancellation
                        await semaphore.WaitAsync(cancellationToken);
                        acquiredSemaphore = true;

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
                            progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Cancelled, "Cancelled", null, null);
                            return;
                        }

                        // Check if game already has music
                        var musicDir = _fileService.GetGameMusicDirectory(game);
                        if (!overwrite && Directory.Exists(musicDir) && Directory.GetFiles(musicDir, "*.mp3").Length > 0)
                        {
                            var skipResult = new GameDownloadResult
                            {
                                Game = game,
                                Success = false,
                                WasSkipped = true,
                                SkipReason = "Already has music"
                            };
                            lock (resultsLock) { results.Add(skipResult); }
                            progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Skipped, "Already has music", null, null);
                            return;
                        }

                        // Report downloading status
                        progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Downloading, "Searching for music...", null, null);

                        // Perform the download - don't pass cancellation token to Task.Run to avoid
                        // OperationCanceledException before the task body runs
                        var result = await Task.Run(() => DownloadMusicForGameInternal(game, source, cancellationToken));

                        lock (resultsLock) { results.Add(result); }

                        if (result.Success)
                        {
                            var sourceInfo = !string.IsNullOrEmpty(result.SourceName) ? $" ({result.SourceName})" : "";
                            progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Completed, $"Downloaded{sourceInfo}", result.AlbumName, result.SourceName);
                        }
                        else if (result.WasSkipped)
                        {
                            progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Skipped, result.SkipReason, null, null);
                        }
                        else
                        {
                            progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Failed, result.ErrorMessage ?? "No music found", null, null);
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
                        progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Cancelled, "Cancelled", null, null);
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
                        progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Failed, ex.Message, null, null);
                    }
                    finally
                    {
                        // Only release if we actually acquired the semaphore
                        if (acquiredSemaphore)
                        {
                            semaphore.Release();
                        }
                    }
                }).ToList();

                // Wait for all tasks to complete
                await Task.WhenAll(tasks);
            }

            return results;
        }

        private GameDownloadResult DownloadMusicForGameInternal(Game game, Source source, CancellationToken cancellationToken)
        {
            var result = new GameDownloadResult { Game = game };

            try
            {
                // Get albums (auto mode)
                var albums = _downloadManager.GetAlbumsForGame(game.Name, source, cancellationToken, auto: true);
                var albumsList = albums?.ToList() ?? new List<Album>();

                if (albumsList.Count == 0)
                {
                    result.ErrorMessage = "No albums found";
                    return result;
                }

                // Pick best album (auto mode)
                var bestAlbum = _downloadManager.BestAlbumPick(albumsList, game);
                if (bestAlbum == null)
                {
                    result.ErrorMessage = "No suitable album match";
                    return result;
                }

                result.AlbumName = bestAlbum.Name;
                result.AlbumId = bestAlbum.Id;
                result.SourceName = GetSourceDisplayName(bestAlbum.Source);

                // Get songs from album
                var songs = _downloadManager.GetSongsFromAlbum(bestAlbum, cancellationToken);
                var songsList = songs?.ToList() ?? new List<Song>();

                if (songsList.Count == 0)
                {
                    result.ErrorMessage = "Album has no songs";
                    return result;
                }

                // Pick best song (auto mode) - use detailed method if available for better error messages
                List<Song> bestSongs;
                string rejectionReason = null;

                if (_downloadManager is DownloadManager dm)
                {
                    var pickResult = dm.BestSongPickWithReason(songsList, game.Name, maxSongs: 1);
                    bestSongs = pickResult.songs;
                    rejectionReason = pickResult.rejectionReason;
                }
                else
                {
                    bestSongs = _downloadManager.BestSongPick(songsList, game.Name, maxSongs: 1);
                }

                if (bestSongs == null || bestSongs.Count == 0)
                {
                    var errorMsg = rejectionReason ?? "No suitable song found";
                    result.ErrorMessage = errorMsg;
                    return result;
                }

                var songToDownload = bestSongs.First();

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
                    extension = Constants.DefaultAudioExtension;
                }
                var downloadPath = Path.Combine(musicDir, safeFileName + extension);

                // Download the song
                var downloadSuccess = _downloadManager.DownloadSong(songToDownload, downloadPath, cancellationToken);

                if (downloadSuccess && File.Exists(downloadPath))
                {
                    result.Success = true;
                    result.SongPath = downloadPath;

                    // Invalidate song cache since we added a new file
                    _fileService.InvalidateCacheForGame(game);
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

        /// <summary>Downloads music for multiple games sequentially</summary>
        public async Task<List<GameDownloadResult>> DownloadMusicSequentialAsync(
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
                    progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Cancelled, "Cancelled", null, null);
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
                    progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Skipped, "Already has music", null, null);
                    continue;
                }

                progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Downloading, "Downloading...", null, null);

                var result = DownloadMusicForGameInternal(game, source, cancellationToken);
                results.Add(result);

                if (result.Success)
                {
                    var sourceInfo = !string.IsNullOrEmpty(result.SourceName) ? $" ({result.SourceName})" : "";
                    progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Completed, $"Downloaded{sourceInfo}", result.AlbumName, result.SourceName);
                }
                else if (result.WasSkipped)
                {
                    progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Skipped, result.SkipReason, null, null);
                }
                else
                {
                    progressCallback?.Invoke(game, game.Name, BatchDownloadStatus.Failed, result.ErrorMessage ?? "No music found", null, null);
                }

                // Rate limiting delay
                if (delayBetweenGamesMs > 0 && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(delayBetweenGamesMs, cancellationToken);
                }
            }

            return results;
        }

        private static string GetSourceDisplayName(Source source)
        {
            switch (source)
            {
                case Source.KHInsider: return "KHInsider";
                case Source.Zophar: return "Zophar";
                case Source.YouTube: return "YouTube";
                case Source.SoundCloud: return "SoundCloud";
                default: return source.ToString();
            }
        }
    }
}
