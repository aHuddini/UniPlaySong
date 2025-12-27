using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Downloaders;
using UniPlaySong.Models;
using UniPlaySong.Services;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace UniPlaySong.Menus
{
    /// <summary>
    /// Handles game context menu actions
    /// </summary>
    public class GameMenuHandler
    {
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;
        private readonly IDownloadManager _downloadManager;
        private readonly GameMusicFileService _fileService;
        private readonly IMusicPlaybackService _playbackService;
        private readonly DownloadDialogService _dialogService;
        private readonly ErrorHandlerService _errorHandler;
        private readonly List<FailedDownload> _failedDownloads;

        /// <summary>
        /// Logs a debug message only if debug logging is enabled in settings.
        /// </summary>
        private void LogDebug(string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                _logger.Debug(message);
            }
        }

        public GameMenuHandler(
            IPlayniteAPI playniteApi,
            ILogger logger,
            IDownloadManager downloadManager,
            GameMusicFileService fileService,
            IMusicPlaybackService playbackService,
            DownloadDialogService dialogService,
            ErrorHandlerService errorHandler = null)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _downloadManager = downloadManager;
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _playbackService = playbackService;
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _errorHandler = errorHandler;
            _failedDownloads = new List<FailedDownload>();
        }

        /// <summary>
        /// Gets the list of failed downloads for retry purposes
        /// </summary>
        public IReadOnlyList<FailedDownload> FailedDownloads => _failedDownloads.AsReadOnly();

        public void DownloadMusicForGame(Game game)
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () =>
                    {
                        _logger.Info($"DownloadMusicForGame called for game: {game?.Name ?? "null"}");
                        System.Diagnostics.Debug.WriteLine($"UniPlaySong: DownloadMusicForGame called for game: {game?.Name ?? "null"}");
                        
                        if (_downloadManager == null)
                        {
                            _playniteApi.Dialogs.ShowMessage("Download manager not initialized. Please check extension settings.");
                            return;
                        }

                        // Step 1: Select source (KHInsider or YouTube) - Use custom dialog for fullscreen compatibility
                        // Loop to allow going back from album selection
                        Source? sourceNullable = null;
                        while (true)
                        {
                            LogDebug("Showing source selection dialog...");
                            sourceNullable = _dialogService.ShowSourceSelectionDialog();
                            if (sourceNullable == null)
                            {
                                _logger.Info("User cancelled source selection");
                                return; // User cancelled
                            }

                            var source = sourceNullable.Value; // Convert nullable to non-nullable
                            _logger.Info($"User selected source: {source}");

                            // Step 2: Select album (with loop to allow going back from song selection)
                            Album album = null;
                            while (true)
                            {
                                LogDebug($"Showing album selection dialog for source: {source}");
                                album = _dialogService.ShowAlbumSelectionDialog(game, source);
                                if (album == null)
                                {
                                    _logger.Info("User cancelled album selection or pressed back - allowing source re-selection");
                                    break; // Break inner loop to re-select source
                                }
                            
                                _logger.Info($"User selected album: {album.Name} (ID: {album.Id})");

                                // Step 3: Select songs and download (handled inline in dialog)
                                var songs = _dialogService.ShowSongSelectionDialog(game, album);
                                // Downloads are now handled inline in the dialog
                                // Dialog returns empty list - downloads happen within the dialog
                                // User can close dialog after download completes
                                return; // Exit function after dialog closes
                            }
                            
                            // If we get here, user pressed back in album selection - break to re-select source
                            break;
                        }
                    },
                    context: $"downloading music for '{game?.Name}'",
                    showUserMessage: true
                );
            }
            else
            {
                // Fallback to original error handling
                try
                {
                    _logger.Info($"DownloadMusicForGame called for game: {game?.Name ?? "null"}");
                    System.Diagnostics.Debug.WriteLine($"UniPlaySong: DownloadMusicForGame called for game: {game?.Name ?? "null"}");
                    
                    if (_downloadManager == null)
                    {
                        _playniteApi.Dialogs.ShowMessage("Download manager not initialized. Please check extension settings.");
                        return;
                    }

                    // Step 1: Select source (KHInsider or YouTube) - Use custom dialog for fullscreen compatibility
                    // Loop to allow going back from album selection
                    Source? sourceNullable = null;
                    while (true)
                    {
                        LogDebug("Showing source selection dialog...");
                        sourceNullable = _dialogService.ShowSourceSelectionDialog();
                        if (sourceNullable == null)
                        {
                            _logger.Info("User cancelled source selection");
                            return; // User cancelled
                        }

                        var source = sourceNullable.Value; // Convert nullable to non-nullable
                        _logger.Info($"User selected source: {source}");

                        // Step 2: Select album (with loop to allow going back from song selection)
                        Album album = null;
                        while (true)
                        {
                            LogDebug($"Showing album selection dialog for source: {source}");
                            album = _dialogService.ShowAlbumSelectionDialog(game, source);
                            if (album == null)
                            {
                                _logger.Info("User cancelled album selection or pressed back - allowing source re-selection");
                                break; // Break inner loop to re-select source
                            }
                        
                            _logger.Info($"User selected album: {album.Name} (ID: {album.Id})");

                            // Step 3: Select songs and download (handled inline in dialog)
                            var songs = _dialogService.ShowSongSelectionDialog(game, album);
                            // Downloads are now handled inline in the dialog
                            // Dialog returns empty list - downloads happen within the dialog
                            // User can close dialog after download completes
                            return; // Exit function after dialog closes
                        }
                        
                        // If we get here, user pressed back in album selection - break to re-select source
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error in DownloadMusicForGame: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
                }
            }
        }

        private void DownloadSongs(Game game, List<Song> songs)
        {
            var musicDir = _fileService.GetGameMusicDirectory(game);
            Directory.CreateDirectory(musicDir);

            var progressTitle = $"Downloading Music for {game.Name}";
            var progressOptions = new GlobalProgressOptions(progressTitle, true)
            {
                IsIndeterminate = false
            };

            _playniteApi.Dialogs.ActivateGlobalProgress((args) =>
            {
                try
                {
                    int downloaded = 0;
                    int total = songs.Count;
                    var cancellationToken = args.CancelToken;

                    foreach (var song in songs)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        args.CurrentProgressValue = downloaded;
                        args.ProgressMaxValue = total;
                        args.Text = $"{progressTitle}\n\nDownloading: {song.Name} ({downloaded + 1}/{total})";

                        // Sanitize filename BEFORE combining with path
                        var sanitizedName = song.Name;
                        foreach (var invalidChar in Path.GetInvalidFileNameChars())
                        {
                            sanitizedName = sanitizedName.Replace(invalidChar, '_');
                        }
                        
                        // Also remove any other problematic characters
                        sanitizedName = sanitizedName.Replace("..", "_");
                        sanitizedName = sanitizedName.Trim();

                        var fileName = $"{sanitizedName}.mp3";
                        var filePath = Path.Combine(musicDir, fileName);

                        LogDebug($"Attempting to download '{song.Name}' to '{filePath}'");

                        var success = _downloadManager.DownloadSong(song, filePath, cancellationToken);
                        if (success)
                        {
                            // Verify file actually exists
                            if (File.Exists(filePath))
                            {
                                downloaded++;
                                var fileInfo = new FileInfo(filePath);
                                _logger.Info($"Successfully downloaded: {song.Name} to {filePath} ({fileInfo.Length} bytes)");
                            }
                            else
                            {
                                _logger.Warn($"Download reported success but file not found: {filePath}");
                            }
                        }
                        else
                        {
                            _logger.Warn($"Failed to download: {song.Name} to {filePath}");
                        }
                    }

                    args.Text = $"Downloaded {downloaded}/{total} songs";
                    
                    // Show summary message
                    if (downloaded < total)
                    {
                        _playniteApi.Dialogs.ShowMessage(
                            $"Downloaded {downloaded} out of {total} songs.\n\n" +
                            $"Music folder: {musicDir}\n\n" +
                            $"Check the logs for details on any failures.",
                            "Download Complete");
                    }
                    else if (downloaded > 0)
                    {
                        _playniteApi.Dialogs.ShowMessage(
                            $"Successfully downloaded all {downloaded} songs!\n\n" +
                            $"Music folder: {musicDir}",
                            "Download Complete");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error downloading songs: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"Error downloading songs: {ex.Message}\n\n" +
                        $"Music folder: {musicDir}\n\n" +
                        $"Check the logs for more details.",
                        "UniPlaySong");
                }
            }, progressOptions);
        }

        public void OpenMusicFolder(Game game)
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () =>
                    {
                        _logger.Info($"OpenMusicFolder called for game: {game?.Name ?? "null"}");
                        var musicDir = _fileService?.GetGameMusicDirectory(game);
                        if (!string.IsNullOrEmpty(musicDir) && Directory.Exists(musicDir))
                        {
                            Process.Start("explorer.exe", musicDir);
                        }
                        else
                        {
                            _playniteApi.Dialogs.ShowMessage(
                                $"No music folder found for {game.Name}.\n\nMusic will be stored in:\n{_fileService?.GetGameMusicDirectory(game) ?? "Unknown"}",
                                "UniPlaySong");
                        }
                    },
                    context: $"opening music folder for '{game?.Name}'",
                    showUserMessage: true
                );
            }
            else
            {
                // Fallback to original error handling
                try
                {
                    _logger.Info($"OpenMusicFolder called for game: {game?.Name ?? "null"}");
                    var musicDir = _fileService?.GetGameMusicDirectory(game);
                    if (!string.IsNullOrEmpty(musicDir) && Directory.Exists(musicDir))
                    {
                        Process.Start("explorer.exe", musicDir);
                    }
                    else
                    {
                        _playniteApi.Dialogs.ShowMessage(
                            $"No music folder found for {game.Name}.\n\nMusic will be stored in:\n{_fileService?.GetGameMusicDirectory(game) ?? "Unknown"}",
                            "UniPlaySong");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error opening music folder: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
                }
            }
        }

        public void SetPrimarySong(Game game)
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () =>
                    {
                        _logger.Info($"SetPrimarySong called for game: {game?.Name ?? "null"}");
                        
                        var songs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                        if (songs.Count == 0)
                        {
                            _playniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                            return;
                        }

                        // Show file selection dialog
                        var selectedFile = _playniteApi.Dialogs.SelectFile("Audio files|*.mp3;*.wav;*.ogg;*.flac");
                        if (!string.IsNullOrEmpty(selectedFile))
                        {
                            var musicDir = _fileService.GetGameMusicDirectory(game);
                            if (selectedFile.StartsWith(musicDir))
                            {
                                PrimarySongManager.SetPrimarySong(musicDir, selectedFile, _errorHandler);
                                _playniteApi.Dialogs.ShowMessage(
                                    $"Primary song set to:\n{Path.GetFileName(selectedFile)}",
                                    "UniPlaySong");
                            }
                            else
                            {
                                _playniteApi.Dialogs.ShowMessage(
                                    "Selected file must be in the game's music folder.",
                                    "UniPlaySong");
                            }
                        }
                    },
                    context: $"setting primary song for '{game?.Name}'",
                    showUserMessage: true
                );
            }
            else
            {
                // Fallback to original error handling
                try
                {
                    _logger.Info($"SetPrimarySong called for game: {game?.Name ?? "null"}");
                    
                    var songs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                    if (songs.Count == 0)
                    {
                        _playniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                        return;
                    }

                    // Show file selection dialog
                    var selectedFile = _playniteApi.Dialogs.SelectFile("Audio files|*.mp3;*.wav;*.ogg;*.flac");
                    if (!string.IsNullOrEmpty(selectedFile))
                    {
                        var musicDir = _fileService.GetGameMusicDirectory(game);
                        if (selectedFile.StartsWith(musicDir))
                        {
                            PrimarySongManager.SetPrimarySong(musicDir, selectedFile);
                            _playniteApi.Dialogs.ShowMessage(
                                $"Primary song set to:\n{Path.GetFileName(selectedFile)}",
                                "UniPlaySong");
                        }
                        else
                        {
                            _playniteApi.Dialogs.ShowMessage(
                                "Selected file must be in the game's music folder.",
                                "UniPlaySong");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error setting primary song: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
                }
            }
        }

        public void ClearPrimarySong(Game game)
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () =>
                    {
                        _logger.Info($"ClearPrimarySong called for game: {game?.Name ?? "null"}");
                        
                        var musicDir = _fileService?.GetGameMusicDirectory(game);
                        if (!string.IsNullOrEmpty(musicDir))
                        {
                            PrimarySongManager.ClearPrimarySong(musicDir, _errorHandler);
                            _playniteApi.Dialogs.ShowMessage("Primary song cleared.", "UniPlaySong");
                        }
                    },
                    context: $"clearing primary song for '{game?.Name}'",
                    showUserMessage: true
                );
            }
            else
            {
                // Fallback to original error handling
                try
                {
                    _logger.Info($"ClearPrimarySong called for game: {game?.Name ?? "null"}");
                    
                    var musicDir = _fileService?.GetGameMusicDirectory(game);
                    if (!string.IsNullOrEmpty(musicDir))
                    {
                        PrimarySongManager.ClearPrimarySong(musicDir);
                        _playniteApi.Dialogs.ShowMessage("Primary song cleared.", "UniPlaySong");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error clearing primary song: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
                }
            }
        }

        /// <summary>
        /// Opens a dialog to download audio from a specific YouTube URL
        /// </summary>
        /// <param name="game">The game to download music for</param>
        public void DownloadFromUrl(Game game)
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () => ExecuteDownloadFromUrl(game),
                    context: $"downloading from URL for '{game?.Name}'",
                    showUserMessage: true
                );
            }
            else
            {
                try
                {
                    ExecuteDownloadFromUrl(game);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error in DownloadFromUrl: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
                }
            }
        }

        private void ExecuteDownloadFromUrl(Game game)
        {
            _logger.Info($"DownloadFromUrl called for game: {game?.Name ?? "null"}");

            if (game == null)
            {
                _playniteApi.Dialogs.ShowMessage("No game selected.", "UniPlaySong");
                return;
            }

            if (_downloadManager == null)
            {
                _playniteApi.Dialogs.ShowMessage("Download manager not initialized. Please check extension settings.", "UniPlaySong");
                return;
            }

            // Show the Download From URL dialog
            _dialogService.ShowDownloadFromUrlDialog(game);
        }

        #region Batch Download (Download All)

        /// <summary>
        /// Downloads music for multiple selected games with batch options
        /// </summary>
        /// <param name="games">List of games to download music for</param>
        /// <param name="source">Download source (KHInsider, YouTube, or All)</param>
        public void DownloadMusicForGames(List<Game> games, Source source = Source.KHInsider)
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () => ExecuteDownloadMusicForGames(games, source),
                    context: $"downloading music for {games.Count} games",
                    showUserMessage: true
                );
            }
            else
            {
                try
                {
                    ExecuteDownloadMusicForGames(games, source);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error in DownloadMusicForGames: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
                }
            }
        }

        private void ExecuteDownloadMusicForGames(List<Game> games, Source source)
        {
            _logger.Info($"DownloadMusicForGames called for {games.Count} game(s), source: {source}");

            if (_downloadManager == null)
            {
                _playniteApi.Dialogs.ShowMessage("Download manager not initialized. Please check extension settings.", "UniPlaySong");
                return;
            }

            if (games == null || games.Count == 0)
            {
                _playniteApi.Dialogs.ShowMessage("No games selected.", "UniPlaySong");
                return;
            }

            // For single game, use existing interactive flow
            if (games.Count == 1)
            {
                DownloadMusicForGame(games[0]);
                return;
            }

            // For multiple games, prompt for batch options (matching PlayniteSound behavior)
            var albumSelect = _playniteApi.Dialogs.ShowMessage(
                "Do you want to manually select albums for each game?\n\n" +
                "• Yes: You'll choose the album for each game\n" +
                "• No: Best matching album will be auto-selected",
                "Album Selection",
                System.Windows.MessageBoxButton.YesNo) == System.Windows.MessageBoxResult.Yes;

            var songSelect = _playniteApi.Dialogs.ShowMessage(
                "Do you want to manually select songs for each album?\n\n" +
                "• Yes: You'll choose which songs to download\n" +
                "• No: Best matching song (theme/title) will be auto-selected",
                "Song Selection",
                System.Windows.MessageBoxButton.YesNo) == System.Windows.MessageBoxResult.Yes;

            var overwrite = _playniteApi.Dialogs.ShowMessage(
                "Do you want to overwrite existing music files?\n\n" +
                "• Yes: Re-download and replace existing files\n" +
                "• No: Skip games that already have music",
                "Overwrite Files",
                System.Windows.MessageBoxButton.YesNo) == System.Windows.MessageBoxResult.Yes;

            _logger.Info($"Batch options: albumSelect={albumSelect}, songSelect={songSelect}, overwrite={overwrite}");

            // Show progress dialog
            var progressTitle = $"UniPlaySong - Downloading Music";
            var progressOptions = new GlobalProgressOptions(progressTitle, true)
            {
                IsIndeterminate = false
            };

            _playniteApi.Dialogs.ActivateGlobalProgress((args) =>
            {
                StartBatchDownload(args, games, source, albumSelect, songSelect, overwrite, progressTitle);
            }, progressOptions);

            // Cleanup temp files after all downloads
            _downloadManager.Cleanup();

            _logger.Info("Batch download complete");
        }

        private void StartBatchDownload(
            GlobalProgressActionArgs args,
            List<Game> games,
            Source source,
            bool albumSelect,
            bool songSelect,
            bool overwrite,
            string progressTitle)
        {
            // Clear failed downloads from previous batch runs to prevent accumulation
            // This ensures only failures from THIS batch run are tracked
            _failedDownloads.Clear();
            LogDebug("Cleared previous failed downloads before starting new batch");

            int gameIdx = 0;
            int successCount = 0;
            int skipCount = 0;
            int failCount = 0;
            var errors = new List<string>();

            foreach (var game in games)
            {
                if (args.CancelToken.IsCancellationRequested)
                {
                    _logger.Info("Batch download cancelled by user");
                    break;
                }

                args.ProgressMaxValue = games.Count;
                args.CurrentProgressValue = ++gameIdx;
                args.Text = $"{progressTitle} ({gameIdx}/{games.Count})\n\n{game.Name}";

                _logger.Info($"Processing game {gameIdx}/{games.Count}: {game.Name}");

                // Check if game already has music (skip if not overwriting)
                var musicDir = _fileService.GetGameMusicDirectory(game);
                if (!overwrite && Directory.Exists(musicDir) && Directory.GetFiles(musicDir, "*.mp3").Length > 0)
                {
                    _logger.Info($"Skipping '{game.Name}' - already has music files");
                    skipCount++;
                    continue;
                }

                try
                {
                    var result = DownloadMusicForSingleGame(
                        args, game, source, albumSelect, songSelect, overwrite, progressTitle);

                    if (result)
                    {
                        successCount++;
                        // Remove from failed downloads if it was previously failed
                        _failedDownloads.RemoveAll(fd => fd.Game?.Id == game.Id);
                    }
                    else
                    {
                        failCount++;
                        // Track failed download
                        TrackFailedDownload(game, "Download failed - no suitable music found");
                    }

                    // Rate limiting: Add a small delay between batch downloads to avoid overwhelming servers
                    // This helps prevent 429 (Too Many Requests) errors from KHInsider and YouTube
                    // Only delay if there are more games to process
                    if (gameIdx < games.Count && !args.CancelToken.IsCancellationRequested)
                    {
                        System.Threading.Thread.Sleep(1000); // 1 second delay between games
                    }
                }
                catch (BatchDownloadSkipException)
                {
                    // User chose to skip this game
                    _logger.Info($"User skipped game: {game.Name}");
                    skipCount++;
                }
                catch (BatchDownloadCancelException)
                {
                    // User cancelled entire operation
                    _logger.Info("User cancelled batch download");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error downloading music for '{game.Name}': {ex.Message}");
                    errors.Add($"{game.Name}: {ex.Message}");
                    failCount++;
                    // Track failed download
                    TrackFailedDownload(game, ex.Message);
                }
            }

            // Show summary after completion
            args.Text = $"Download complete: {successCount} successful, {skipCount} skipped, {failCount} failed";

            // Show detailed summary on UI thread
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                var message = $"Batch download complete!\n\n" +
                    $"✓ Successful: {successCount}\n" +
                    $"⊘ Skipped: {skipCount}\n" +
                    $"✗ Failed: {failCount}";

                if (errors.Count > 0 && errors.Count <= 5)
                {
                    message += $"\n\nErrors:\n" + string.Join("\n", errors);
                }
                else if (errors.Count > 5)
                {
                    message += $"\n\nFirst 5 errors:\n" + string.Join("\n", errors.Take(5));
                    message += $"\n... and {errors.Count - 5} more";
                }

                _playniteApi.Dialogs.ShowMessage(message, "UniPlaySong - Batch Download");

                // If there are failed downloads, offer to retry them immediately
                if (failCount > 0)
                {
                    var retryPrompt = $"Would you like to retry the {failCount} failed download(s) now?\n\n" +
                                     "You'll be able to manually search and select music for each failed game.";

                    var retryResult = _playniteApi.Dialogs.ShowMessage(
                        retryPrompt,
                        "UniPlaySong - Retry Failed Downloads?",
                        MessageBoxButton.YesNo);

                    if (retryResult == MessageBoxResult.Yes)
                    {
                        RetryFailedDownloads();
                    }
                }
            });
        }

        private bool DownloadMusicForSingleGame(
            GlobalProgressActionArgs args,
            Game game,
            Source source,
            bool albumSelect,
            bool songSelect,
            bool overwrite,
            string progressTitle)
        {
            // Prepare game name for searching (strips suffixes, normalizes)
            var searchGameName = StringHelper.PrepareForSearch(game.Name);
            _logger.Info($"Searching for '{game.Name}' using search term: '{searchGameName}'");

            // Step 1: Get/Select album
            Album album = null;

            if (albumSelect)
            {
                // Show album selection dialog (dispatched to UI thread)
                album = System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    _dialogService.ShowAlbumSelectionDialog(game, source));
            }
            else
            {
                // Auto-select best album with fallback logic
                args.Text = $"{progressTitle}\n\n{game.Name}\nSearching for albums...";
                
                // Use Source.All for automatic fallback (KHInsider -> YouTube)
                var effectiveSource = source == Source.KHInsider ? Source.All : source;
                
                _logger.Info($"Getting albums for '{game.Name}' with source: {effectiveSource}");
                var albums = _downloadManager.GetAlbumsForGame(searchGameName, effectiveSource, args.CancelToken, auto: true)?.ToList();

                if (albums == null || albums.Count == 0)
                {
                    _logger.Warn($"No albums found for game: {game.Name} (searched: '{searchGameName}')");
                    args.Text = $"{progressTitle}\n\n{game.Name}\n⚠ No albums found";
                    return false;
                }

                _logger.Info($"Found {albums.Count} album(s) for '{game.Name}'");
                album = _downloadManager.BestAlbumPick(albums, game);
                
                if (album != null)
                {
                    // Show which source we're using
                    var sourceIcon = album.Source == Source.KHInsider ? "🎮" : 
                                    album.Source == Source.YouTube ? "📺" : "🎵";
                    _logger.Info($"Auto-selected album from {album.Source}: '{album.Name}' for game: {game.Name}");
                    args.Text = $"{progressTitle}\n\n{game.Name}\n{sourceIcon} {album.Source}: {album.Name}";
                }
            }

            if (album == null)
            {
                _logger.Warn($"No suitable album found for game: {game.Name} after filtering");
                args.Text = $"{progressTitle}\n\n{game.Name}\n⚠ No suitable albums";
                return false;
            }

            // Step 2: Get/Select songs
            List<Song> songs = null;

            if (songSelect)
            {
                // Show song selection dialog (dispatched to UI thread)
                songs = System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    _dialogService.ShowSongSelectionDialogWithReturn(game, album));
            }
            else
            {
                // Auto-select best song(s)
                var sourceIcon = album.Source == Source.KHInsider ? "🎮" : 
                                album.Source == Source.YouTube ? "📺" : "🎵";
                args.Text = $"{progressTitle}\n\n{game.Name}\n{sourceIcon} Getting songs...";
                
                var allSongs = _downloadManager.GetSongsFromAlbum(album, args.CancelToken)?.ToList();

                if (allSongs == null || allSongs.Count == 0)
                {
                    _logger.Warn($"No songs found in album: {album.Name}");
                    args.Text = $"{progressTitle}\n\n{game.Name}\n⚠ No songs in album";
                    return false;
                }

                _logger.Info($"Found {allSongs.Count} song(s) in album '{album.Name}'");
                
                // Use prepared search name for better song matching
                songs = _downloadManager.BestSongPick(allSongs, searchGameName, maxSongs: 1);
                if (songs.Count > 0)
                {
                    _logger.Info($"Auto-selected song: '{songs[0].Name}' for game: {game.Name}");
                    args.Text = $"{progressTitle}\n\n{game.Name}\n{sourceIcon} Downloading: {songs[0].Name}";
                }
            }

            if (songs == null || songs.Count == 0)
            {
                _logger.Warn($"No songs selected for game: {game.Name}");
                return false;
            }

            // Step 3: Download songs
            return DownloadSongsForGame(game, songs, overwrite, args, progressTitle);
        }

        private bool DownloadSongsForGame(
            Game game,
            List<Song> songs,
            bool overwrite,
            GlobalProgressActionArgs args,
            string progressTitle)
        {
            var musicDir = _fileService.GetGameMusicDirectory(game);
            Directory.CreateDirectory(musicDir);

            int downloaded = 0;

            foreach (var song in songs)
            {
                if (args.CancelToken.IsCancellationRequested)
                    break;

                // Sanitize filename
                var sanitizedName = song.Name;
                foreach (var invalidChar in Path.GetInvalidFileNameChars())
                {
                    sanitizedName = sanitizedName.Replace(invalidChar, '_');
                }
                sanitizedName = sanitizedName.Replace("..", "_").Trim();

                var fileName = $"{sanitizedName}.mp3";
                var filePath = Path.Combine(musicDir, fileName);

                // Skip if file exists and not overwriting
                if (!overwrite && File.Exists(filePath))
                {
                    _logger.Info($"Skipping existing file: {filePath}");
                    continue;
                }

                args.Text = $"{progressTitle}\n\n{game.Name}: Downloading '{song.Name}'...";

                var success = _downloadManager.DownloadSong(song, filePath, args.CancelToken);
                if (success && File.Exists(filePath))
                {
                    downloaded++;
                    var fileInfo = new FileInfo(filePath);
                    _logger.Info($"Downloaded: {song.Name} to {filePath} ({fileInfo.Length} bytes)");
                }
                else
                {
                    _logger.Warn($"Failed to download: {song.Name}");
                }
            }

            return downloaded > 0;
        }

        #endregion

        #region Failed Download Tracking and Retry

        /// <summary>
        /// Tracks a failed download for later retry
        /// </summary>
        private void TrackFailedDownload(Game game, string reason)
        {
            if (game == null)
                return;

            // Remove any existing failed download for this game
            _failedDownloads.RemoveAll(fd => fd.Game?.Id == game.Id);

            // Add new failed download
            _failedDownloads.Add(new FailedDownload
            {
                Game = game,
                FailureReason = reason,
                FailedAt = DateTime.Now,
                Resolved = false
            });

            _logger.Info($"Tracked failed download for '{game.Name}': {reason}");
        }

        /// <summary>
        /// Retry failed downloads with manual source selection
        /// Allows users to manually search and select music from KHInsider or YouTube
        /// </summary>
        public void RetryFailedDownloads()
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () => ExecuteRetryFailedDownloads(),
                    context: "retrying failed downloads",
                    showUserMessage: true
                );
            }
            else
            {
                try
                {
                    ExecuteRetryFailedDownloads();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error in RetryFailedDownloads: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
                }
            }
        }

        private void ExecuteRetryFailedDownloads()
        {
            var unresolved = _failedDownloads.Where(fd => !fd.Resolved).ToList();

            if (unresolved.Count == 0)
            {
                _playniteApi.Dialogs.ShowMessage(
                    "No failed downloads to retry.\n\n" +
                    "Failed downloads are tracked when batch download operations fail.",
                    "UniPlaySong - Retry Failed Downloads");
                return;
            }

            var message = $"Found {unresolved.Count} failed download(s):\n\n";
            foreach (var fd in unresolved.Take(10))
            {
                message += $"• {fd.Game.Name}\n";
            }
            if (unresolved.Count > 10)
            {
                message += $"\n... and {unresolved.Count - 10} more";
            }
            message += $"\n\nWould you like to retry these downloads with manual source selection?";

            var result = _playniteApi.Dialogs.ShowMessage(
                message,
                "UniPlaySong - Retry Failed Downloads",
                MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes)
                return;

            int retried = 0;
            int succeeded = 0;

            foreach (var failedDownload in unresolved)
            {
                _logger.Info($"Retrying failed download for: {failedDownload.Game.Name}");

                // Use unified search (both KHInsider and YouTube)
                try
                {
                    // Show unified album selection dialog (searches both sources)
                    var album = _dialogService.ShowUnifiedAlbumSelectionDialog(failedDownload.Game);
                    if (album == null)
                    {
                        _logger.Info($"User cancelled album selection for: {failedDownload.Game.Name}");
                        continue; // Skip to next game
                    }

                    // Show song selection dialog (with inline download)
                    _dialogService.ShowSongSelectionDialog(failedDownload.Game, album);

                    // Mark as resolved
                    failedDownload.Resolved = true;
                    retried++;
                    succeeded++;

                    _logger.Info($"Successfully retried download for: {failedDownload.Game.Name}");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error retrying download for '{failedDownload.Game.Name}': {ex.Message}");
                    retried++;
                    // Don't mark as resolved if it failed again
                }
            }

            // Clean up resolved downloads
            _failedDownloads.RemoveAll(fd => fd.Resolved);

            // Show summary
            var summaryMessage = $"Retry Summary:\n\n" +
                                $"Attempted: {retried}\n" +
                                $"Succeeded: {succeeded}\n" +
                                $"Remaining failures: {_failedDownloads.Count(fd => !fd.Resolved)}";

            _playniteApi.Dialogs.ShowMessage(summaryMessage, "UniPlaySong - Retry Complete");
        }

        /// <summary>
        /// Clear all tracked failed downloads
        /// </summary>
        public void ClearFailedDownloads()
        {
            _failedDownloads.Clear();
            _logger.Info("Cleared all tracked failed downloads");
        }

        #endregion

        #region Batch Download Exceptions

        /// <summary>
        /// Exception thrown when user wants to skip current game in batch download
        /// </summary>
        private class BatchDownloadSkipException : Exception { }

        /// <summary>
        /// Exception thrown when user wants to cancel entire batch download
        /// </summary>
        private class BatchDownloadCancelException : Exception { }

        #endregion
    }
}

