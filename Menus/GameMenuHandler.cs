using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly AudioRepairService _repairService;
        private readonly Func<UniPlaySongSettings> _getSettings;

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
            ErrorHandlerService errorHandler = null,
            AudioRepairService repairService = null,
            Func<UniPlaySongSettings> getSettings = null)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _downloadManager = downloadManager;
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _playbackService = playbackService;
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _errorHandler = errorHandler;
            _failedDownloads = new List<FailedDownload>();
            _repairService = repairService;
            _getSettings = getSettings;
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
                        _logger.Debug($"DownloadMusicForGame called for game: {game?.Name ?? "null"}");

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
                                _logger.Debug("User cancelled source selection");
                                return; // User cancelled
                            }

                            var source = sourceNullable.Value; // Convert nullable to non-nullable
                            _logger.Debug($"User selected source: {source}");

                            // Step 2: Select album (with loop to allow going back from song selection)
                            Album album = null;
                            bool backToSourceFromAlbum = false;
                            while (true)
                            {
                                LogDebug($"Showing album selection dialog for source: {source}");
                                album = _dialogService.ShowAlbumSelectionDialog(game, source);

                                // Check if user pressed Back button (return to source selection)
                                if (Album.IsBackSignal(album))
                                {
                                    _logger.Debug("User pressed back in album selection - returning to source selection");
                                    backToSourceFromAlbum = true;
                                    break; // Break inner loop to re-select source
                                }

                                // Check if user cancelled (exit entirely)
                                if (album == null)
                                {
                                    _logger.Debug("User cancelled album selection");
                                    return; // Exit function entirely
                                }

                                _logger.Debug($"User selected album: {album.Name} (ID: {album.Id})");

                                // Step 3: Select songs and download (handled inline in dialog)
                                var songs = _dialogService.ShowSongSelectionDialog(game, album);

                                // If songs is null, user pressed Back - loop to re-select album
                                if (songs == null)
                                {
                                    _logger.Debug("User pressed back in song selection - returning to album selection");
                                    continue; // Continue inner loop to re-select album
                                }

                                // Downloads are handled inline in the dialog
                                // Empty list means user completed download or cancelled
                                return; // Exit function after dialog closes
                            }

                            // If backToSourceFromAlbum is true, continue outer loop to re-select source
                            if (!backToSourceFromAlbum)
                            {
                                return; // User cancelled, exit entirely
                            }
                            // Otherwise continue to next iteration of outer while(true) loop
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
                    _logger.Debug($"DownloadMusicForGame called for game: {game?.Name ?? "null"}");

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
                            _logger.Debug("User cancelled source selection");
                            return; // User cancelled
                        }

                        var source = sourceNullable.Value; // Convert nullable to non-nullable
                        _logger.Debug($"User selected source: {source}");

                        // Step 2: Select album (with loop to allow going back from song selection)
                        Album album = null;
                        bool backToSourceFromAlbum = false;
                        while (true)
                        {
                            LogDebug($"Showing album selection dialog for source: {source}");
                            album = _dialogService.ShowAlbumSelectionDialog(game, source);

                            // Check if user pressed Back button (return to source selection)
                            if (Album.IsBackSignal(album))
                            {
                                _logger.Debug("User pressed back in album selection - returning to source selection");
                                backToSourceFromAlbum = true;
                                break; // Break inner loop to re-select source
                            }

                            // Check if user cancelled (exit entirely)
                            if (album == null)
                            {
                                _logger.Debug("User cancelled album selection");
                                return; // Exit function entirely
                            }

                            _logger.Debug($"User selected album: {album.Name} (ID: {album.Id})");

                            // Step 3: Select songs and download (handled inline in dialog)
                            var songs = _dialogService.ShowSongSelectionDialog(game, album);

                            // If songs is null, user pressed Back - loop to re-select album
                            if (songs == null)
                            {
                                _logger.Debug("User pressed back in song selection - returning to album selection");
                                continue; // Continue inner loop to re-select album
                            }

                            // Downloads are handled inline in the dialog
                            // Empty list means user completed download or cancelled
                            return; // Exit function after dialog closes
                        }

                        // If backToSourceFromAlbum is true, continue outer loop to re-select source
                        if (!backToSourceFromAlbum)
                        {
                            return; // User cancelled, exit entirely
                        }
                        // Otherwise continue to next iteration of outer while(true) loop
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

            var downloadedFilePaths = new List<string>();

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
                        var sanitizedName = StringHelper.CleanForPath(song.Name);

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
                                downloadedFilePaths.Add(filePath);
                                var fileInfo = new FileInfo(filePath);
                                _logger.Debug($"Successfully downloaded: {song.Name} to {filePath} ({fileInfo.Length} bytes)");
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

            // Trigger auto-normalize after download dialog closes (if any files were downloaded)
            if (downloadedFilePaths.Count > 0)
            {
                try
                {
                    _dialogService?.AutoNormalizeDownloadedFiles(downloadedFilePaths);
                }
                catch (Exception ex)
                {
                    LogDebug($"Error during auto-normalize after bulk download: {ex.Message}");
                }
            }
        }

        public void OpenMusicFolder(Game game)
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () =>
                    {
                        _logger.Debug($"OpenMusicFolder called for game: {game?.Name ?? "null"}");
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
                    _logger.Debug($"OpenMusicFolder called for game: {game?.Name ?? "null"}");
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
                        _logger.Debug($"SetPrimarySong called for game: {game?.Name ?? "null"}");

                        // Get the game's music directory first
                        var musicDir = _fileService.GetGameMusicDirectory(game);

                        var songs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                        if (songs.Count == 0)
                        {
                            _playniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                            return;
                        }

                        // Show file selection dialog starting in the game's music directory
                        var dialog = new Microsoft.Win32.OpenFileDialog
                        {
                            Filter = "Audio files|*.mp3;*.wav;*.ogg;*.flac",
                            InitialDirectory = musicDir,
                            Title = "Select Primary Song"
                        };

                        var selectedFile = dialog.ShowDialog() == true ? dialog.FileName : null;
                        if (!string.IsNullOrEmpty(selectedFile))
                        {
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
                    _logger.Debug($"SetPrimarySong called for game: {game?.Name ?? "null"}");

                    // Get the game's music directory first
                    var musicDir = _fileService.GetGameMusicDirectory(game);

                    var songs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                    if (songs.Count == 0)
                    {
                        _playniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                        return;
                    }

                    // Show file selection dialog starting in the game's music directory
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = "Audio files|*.mp3;*.wav;*.ogg;*.flac",
                        InitialDirectory = musicDir,
                        Title = "Select Primary Song"
                    };

                    var selectedFile = dialog.ShowDialog() == true ? dialog.FileName : null;
                    if (!string.IsNullOrEmpty(selectedFile))
                    {
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
                        _logger.Debug($"ClearPrimarySong called for game: {game?.Name ?? "null"}");
                        
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
                    _logger.Debug($"ClearPrimarySong called for game: {game?.Name ?? "null"}");
                    
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
        /// Shows a file picker dialog for normalizing an individual song
        /// </summary>
        public void ShowNormalizeIndividualSong(Game game)
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () => ExecuteShowNormalizeIndividualSong(game),
                    context: $"showing normalize individual song dialog for '{game?.Name}'",
                    showUserMessage: true
                );
            }
            else
            {
                try
                {
                    ExecuteShowNormalizeIndividualSong(game);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error in ShowNormalizeIndividualSong: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
                }
            }
        }

        private void ExecuteShowNormalizeIndividualSong(Game game)
        {
            _logger.Debug($"ShowNormalizeIndividualSong called for game: {game?.Name ?? "null"}");

            var songs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
            if (songs.Count == 0)
            {
                _playniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                return;
            }

            var musicDir = _fileService.GetGameMusicDirectory(game);

            // Show file selection dialog
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio files|*.mp3;*.wav;*.ogg;*.flac",
                InitialDirectory = musicDir,
                Title = "Select Song to Normalize"
            };

            var selectedFile = dialog.ShowDialog() == true ? dialog.FileName : null;
            if (!string.IsNullOrEmpty(selectedFile))
            {
                if (selectedFile.StartsWith(musicDir))
                {
                    // Trigger normalization for this single file via the plugin
                    _dialogService.ShowNormalizeIndividualSongProgress(game, selectedFile);
                }
                else
                {
                    _playniteApi.Dialogs.ShowMessage(
                        "Selected file must be in the game's music folder.",
                        "UniPlaySong");
                }
            }
        }

        /// <summary>
        /// Shows a file picker dialog for trimming an individual song
        /// </summary>
        public void ShowTrimIndividualSong(Game game)
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () => ExecuteShowTrimIndividualSong(game),
                    context: $"showing trim individual song dialog for '{game?.Name}'",
                    showUserMessage: true
                );
            }
            else
            {
                try
                {
                    ExecuteShowTrimIndividualSong(game);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error in ShowTrimIndividualSong: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
                }
            }
        }

        private void ExecuteShowTrimIndividualSong(Game game)
        {
            _logger.Debug($"ShowTrimIndividualSong called for game: {game?.Name ?? "null"}");

            var songs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
            if (songs.Count == 0)
            {
                _playniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                return;
            }

            var musicDir = _fileService.GetGameMusicDirectory(game);

            // Show file selection dialog
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio files|*.mp3;*.wav;*.ogg;*.flac",
                InitialDirectory = musicDir,
                Title = "Silence Trim - Select Song"
            };

            var selectedFile = dialog.ShowDialog() == true ? dialog.FileName : null;
            if (!string.IsNullOrEmpty(selectedFile))
            {
                if (selectedFile.StartsWith(musicDir))
                {
                    // Trigger trim for this single file via the dialog service
                    _dialogService.ShowTrimIndividualSongProgress(game, selectedFile);
                }
                else
                {
                    _playniteApi.Dialogs.ShowMessage(
                        "Selected file must be in the game's music folder.",
                        "UniPlaySong");
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
            _logger.Debug($"DownloadFromUrl called for game: {game?.Name ?? "null"}");

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
            _logger.Debug($"DownloadMusicForGames called for {games.Count} game(s), source: {source}");

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

            _logger.Debug($"Batch options: albumSelect={albumSelect}, songSelect={songSelect}, overwrite={overwrite}");

            // Stop playback before downloading if we're overwriting files
            // This prevents file lock errors when the currently playing song is being replaced
            if (overwrite)
            {
                _logger.Debug("Stopping playback before overwrite download to prevent file locks");
                _playbackService?.Stop();
            }

            var batchDownloadedFilePaths = new List<string>();

            // If full auto mode (no manual selection), use parallel batch download with progress dialog
            if (!albumSelect && !songSelect)
            {
                _logger.Debug("Using parallel batch download (full auto mode)");

                // Get concurrent downloads setting
                var settings = _getSettings?.Invoke();
                var maxConcurrent = settings?.MaxConcurrentDownloads ?? 3;

                // Get currently selected game for playback resume after batch download
                var currentGame = _playniteApi.MainView.SelectedGames?.FirstOrDefault();

                // Use the batch download dialog with results for retry support
                var batchResult = _dialogService?.ShowBatchDownloadDialogWithResults(games, source, overwrite, maxConcurrent, currentGame);
                if (batchResult != null)
                {
                    batchDownloadedFilePaths.AddRange(batchResult.DownloadedFiles);

                    // Prompt for retry if there were failures
                    if (batchResult.FailedGames.Count > 0)
                    {
                        _logger.Debug($"Batch download: {batchResult.FailedGames.Count} games failed, prompting for retry");
                        var retryDownloads = _dialogService?.PromptAndRetryFailedDownloads(batchResult.FailedGames);
                        if (retryDownloads != null && retryDownloads.Count > 0)
                        {
                            batchDownloadedFilePaths.AddRange(retryDownloads);
                        }
                    }
                }
            }
            else
            {
                // Manual selection mode - use sequential download with global progress
                _logger.Debug("Using sequential batch download (manual selection mode)");

                var progressTitle = $"UniPlaySong - Downloading Music";
                var progressOptions = new GlobalProgressOptions(progressTitle, true)
                {
                    IsIndeterminate = false
                };

                _playniteApi.Dialogs.ActivateGlobalProgress((args) =>
                {
                    StartBatchDownloadAsync(args, games, source, albumSelect, songSelect, overwrite, progressTitle, batchDownloadedFilePaths).Wait();
                }, progressOptions);
            }

            // Cleanup temp files after all downloads
            _downloadManager.Cleanup();

            // Trigger auto-normalize after batch download completes (if any files were downloaded)
            if (batchDownloadedFilePaths.Count > 0)
            {
                try
                {
                    _dialogService?.AutoNormalizeDownloadedFiles(batchDownloadedFilePaths);
                }
                catch (Exception ex)
                {
                    LogDebug($"Error during auto-normalize after batch download: {ex.Message}");
                }
            }

            _logger.Debug("Batch download complete");
        }

        private async Task StartBatchDownloadAsync(
            GlobalProgressActionArgs args,
            List<Game> games,
            Source source,
            bool albumSelect,
            bool songSelect,
            bool overwrite,
            string progressTitle,
            List<string> downloadedFilePaths = null)
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
                    _logger.Debug("Batch download cancelled by user");
                    break;
                }

                args.ProgressMaxValue = games.Count;
                args.CurrentProgressValue = ++gameIdx;
                args.Text = $"{progressTitle} ({gameIdx}/{games.Count})\n\n{game.Name}";

                _logger.Debug($"Processing game {gameIdx}/{games.Count}: {game.Name}");

                // Check if game already has music (skip if not overwriting)
                var musicDir = _fileService.GetGameMusicDirectory(game);
                if (!overwrite && Directory.Exists(musicDir) && Directory.GetFiles(musicDir, "*.mp3").Length > 0)
                {
                    _logger.Debug($"Skipping '{game.Name}' - already has music files");
                    skipCount++;
                    continue;
                }

                try
                {
                    var result = DownloadMusicForSingleGame(
                        args, game, source, albumSelect, songSelect, overwrite, progressTitle, downloadedFilePaths);

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
                        await Task.Delay(1000, args.CancelToken); // 1 second delay between games
                    }
                }
                catch (BatchDownloadSkipException)
                {
                    // User chose to skip this game
                    _logger.Debug($"User skipped game: {game.Name}");
                    skipCount++;
                }
                catch (BatchDownloadCancelException)
                {
                    // User cancelled entire operation
                    _logger.Debug("User cancelled batch download");
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
            string progressTitle,
            List<string> downloadedFilePaths = null)
        {
            // Prepare game name for searching (strips suffixes, normalizes)
            var searchGameName = StringHelper.PrepareForSearch(game.Name);
            _logger.Debug($"Searching for '{game.Name}' using search term: '{searchGameName}'");

            // Step 1: Get/Select album
            Album album = null;
            var currentSource = source;

            if (albumSelect)
            {
                // Loop to allow user to go back to source selection
                while (true)
                {
                    // Show album selection dialog (dispatched to UI thread)
                    album = System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        _dialogService.ShowAlbumSelectionDialog(game, currentSource));

                    // Check if user pressed Back button (return to source selection)
                    if (Album.IsBackSignal(album))
                    {
                        _logger.Debug($"User pressed back in album selection for '{game.Name}' - showing source selection");

                        // Show source selection dialog on UI thread with custom button labels
                        var newSource = System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            var sourceOptions = new List<Playnite.SDK.MessageBoxOption>
                            {
                                new Playnite.SDK.MessageBoxOption("KHInsider", true, false),  // isDefault = true
                                new Playnite.SDK.MessageBoxOption("YouTube", false, false),
                                new Playnite.SDK.MessageBoxOption("Skip Game", false, true)   // isCancel = true
                            };

                            var selected = _playniteApi.Dialogs.ShowMessage(
                                $"Select music source for '{game.Name}':\n\n" +
                                "• KHInsider: Video game soundtracks database\n" +
                                "• YouTube: Search YouTube for game music\n" +
                                "• Skip Game: Skip this game and continue",
                                "Select Source",
                                System.Windows.MessageBoxImage.Question,
                                sourceOptions);

                            if (selected == null || selected.Title == "Skip Game")
                                return (Source?)null;

                            if (selected.Title == "KHInsider")
                                return Source.KHInsider;
                            else if (selected.Title == "YouTube")
                                return Source.YouTube;
                            else
                                return (Source?)null;
                        });

                        if (newSource == null)
                        {
                            // User cancelled or chose to skip - skip this game
                            _logger.Debug($"User cancelled/skipped source selection for '{game.Name}'");
                            return false;
                        }

                        currentSource = newSource.Value;
                        _logger.Debug($"User selected new source: {currentSource} for '{game.Name}'");
                        continue; // Loop back to show album selection with new source
                    }

                    // User either selected an album or cancelled
                    break;
                }
            }
            else
            {
                // Auto-select best album with fallback logic
                args.Text = $"{progressTitle}\n\n{game.Name}\nSearching for albums...";

                // Use Source.All for automatic fallback (KHInsider -> YouTube)
                var effectiveSource = currentSource == Source.KHInsider ? Source.All : currentSource;

                _logger.Debug($"Getting albums for '{game.Name}' with source: {effectiveSource}");
                var albums = _downloadManager.GetAlbumsForGame(searchGameName, effectiveSource, args.CancelToken, auto: true)?.ToList();

                if (albums == null || albums.Count == 0)
                {
                    _logger.Warn($"No albums found for game: {game.Name} (searched: '{searchGameName}')");
                    args.Text = $"{progressTitle}\n\n{game.Name}\n⚠ No albums found";
                    return false;
                }

                _logger.Debug($"Found {albums.Count} album(s) for '{game.Name}'");
                album = _downloadManager.BestAlbumPick(albums, game);

                if (album != null)
                {
                    // Show which source we're using
                    var sourceIcon = album.Source == Source.KHInsider ? "🎮" :
                                    album.Source == Source.YouTube ? "📺" : "🎵";
                    _logger.Debug($"Auto-selected album from {album.Source}: '{album.Name}' for game: {game.Name}");
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

                _logger.Debug($"Found {allSongs.Count} song(s) in album '{album.Name}'");
                
                // Use prepared search name for better song matching
                songs = _downloadManager.BestSongPick(allSongs, searchGameName, maxSongs: 1);
                if (songs.Count > 0)
                {
                    _logger.Debug($"Auto-selected song: '{songs[0].Name}' for game: {game.Name}");
                    args.Text = $"{progressTitle}\n\n{game.Name}\n{sourceIcon} Downloading: {songs[0].Name}";
                }
            }

            if (songs == null || songs.Count == 0)
            {
                _logger.Warn($"No songs selected for game: {game.Name}");
                return false;
            }

            // Step 3: Download songs
            return DownloadSongsForGame(game, songs, overwrite, args, progressTitle, downloadedFilePaths);
        }

        private bool DownloadSongsForGame(
            Game game,
            List<Song> songs,
            bool overwrite,
            GlobalProgressActionArgs args,
            string progressTitle,
            List<string> downloadedFilePaths = null)
        {
            var musicDir = _fileService.GetGameMusicDirectory(game);
            Directory.CreateDirectory(musicDir);

            int downloaded = 0;

            foreach (var song in songs)
            {
                if (args.CancelToken.IsCancellationRequested)
                    break;

                // Sanitize filename
                var sanitizedName = StringHelper.CleanForPath(song.Name);

                var fileName = $"{sanitizedName}.mp3";
                var filePath = Path.Combine(musicDir, fileName);

                // Skip if file exists and not overwriting
                if (!overwrite && File.Exists(filePath))
                {
                    _logger.Debug($"Skipping existing file: {filePath}");
                    continue;
                }

                args.Text = $"{progressTitle}\n\n{game.Name}: Downloading '{song.Name}'...";

                var success = _downloadManager.DownloadSong(song, filePath, args.CancelToken);
                if (success && File.Exists(filePath))
                {
                    downloaded++;
                    downloadedFilePaths?.Add(filePath);
                    var fileInfo = new FileInfo(filePath);
                    _logger.Debug($"Downloaded: {song.Name} to {filePath} ({fileInfo.Length} bytes)");
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

            _logger.Debug($"Tracked failed download for '{game.Name}': {reason}");
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
                _logger.Debug($"Retrying failed download for: {failedDownload.Game.Name}");

                // Use unified search (both KHInsider and YouTube)
                try
                {
                    // Show unified album selection dialog (searches both sources)
                    var album = _dialogService.ShowUnifiedAlbumSelectionDialog(failedDownload.Game);
                    if (album == null)
                    {
                        _logger.Debug($"User cancelled album selection for: {failedDownload.Game.Name}");
                        continue; // Skip to next game
                    }

                    // Show song selection dialog (with inline download)
                    _dialogService.ShowSongSelectionDialog(failedDownload.Game, album);

                    // Mark as resolved
                    failedDownload.Resolved = true;
                    retried++;
                    succeeded++;

                    _logger.Debug($"Successfully retried download for: {failedDownload.Game.Name}");
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
            _logger.Debug("Cleared all tracked failed downloads");
        }

        #endregion

        #region Audio Repair

        /// <summary>
        /// Shows a file picker dialog for repairing a problematic audio file.
        /// Uses FFmpeg to re-encode the file to fix encoding issues that cause SDL_mixer failures.
        /// </summary>
        public void ShowRepairAudioFile(Game game)
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () => ExecuteShowRepairAudioFile(game),
                    context: $"showing repair audio file dialog for '{game?.Name}'",
                    showUserMessage: true
                );
            }
            else
            {
                try
                {
                    ExecuteShowRepairAudioFile(game);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error in ShowRepairAudioFile: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
                }
            }
        }

        private void ExecuteShowRepairAudioFile(Game game)
        {
            _logger.Debug($"ShowRepairAudioFile called for game: {game?.Name ?? "null"}");

            if (_repairService == null)
            {
                _playniteApi.Dialogs.ShowMessage(
                    "Audio repair service not available.",
                    "UniPlaySong");
                return;
            }

            var settings = _getSettings?.Invoke();
            if (settings == null || string.IsNullOrWhiteSpace(settings.FFmpegPath))
            {
                _playniteApi.Dialogs.ShowMessage(
                    "FFmpeg is required for audio repair. Please configure FFmpeg path in Settings → Audio Normalization.",
                    "UniPlaySong");
                return;
            }

            if (!_repairService.ValidateFFmpegAvailable(settings.FFmpegPath))
            {
                _playniteApi.Dialogs.ShowMessage(
                    $"FFmpeg not found or not working at: {settings.FFmpegPath}\n\nPlease verify the path in Settings → Audio Normalization.",
                    "UniPlaySong");
                return;
            }

            var songs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
            if (songs.Count == 0)
            {
                _playniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                return;
            }

            var musicDir = _fileService.GetGameMusicDirectory(game);

            // Show file selection dialog
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio files|*.mp3;*.wav;*.ogg;*.flac;*.m4a;*.aac",
                InitialDirectory = musicDir,
                Title = "Repair Audio File - Select Song"
            };

            var selectedFile = dialog.ShowDialog() == true ? dialog.FileName : null;
            if (!string.IsNullOrEmpty(selectedFile))
            {
                if (selectedFile.StartsWith(musicDir))
                {
                    RepairAudioFileWithProgress(game, selectedFile, settings.FFmpegPath);
                }
                else
                {
                    _playniteApi.Dialogs.ShowMessage(
                        "Selected file must be in the game's music folder.",
                        "UniPlaySong");
                }
            }
        }

        private void RepairAudioFileWithProgress(Game game, string filePath, string ffmpegPath)
        {
            var fileName = Path.GetFileName(filePath);
            _logger.Debug($"Starting audio repair for: {fileName}");

            // First, probe the file to show what issues were detected
            var progressTitle = "UniPlaySong - Repairing Audio";
            var progressOptions = new GlobalProgressOptions(progressTitle, false)
            {
                IsIndeterminate = true
            };

            AudioProbeResult probeResult = null;
            bool repairSuccess = false;

            _playniteApi.Dialogs.ActivateGlobalProgress((args) =>
            {
                args.Text = $"Analyzing: {fileName}";

                // Probe the file
                var probeTask = _repairService.ProbeFileAsync(filePath, ffmpegPath);
                probeTask.Wait();
                probeResult = probeTask.Result;

                if (probeResult.Success)
                {
                    args.Text = $"Repairing: {fileName}";

                    // Repair the file
                    var repairTask = _repairService.RepairFileAsync(filePath, ffmpegPath);
                    repairTask.Wait();
                    repairSuccess = repairTask.Result;
                }
            }, progressOptions);

            // Show result
            if (probeResult == null || !probeResult.Success)
            {
                _playniteApi.Dialogs.ShowMessage(
                    $"Failed to analyze audio file: {fileName}\n\n" +
                    $"Error: {probeResult?.ErrorMessage ?? "Unknown error"}",
                    "UniPlaySong - Repair Failed");
                return;
            }

            if (repairSuccess)
            {
                // Invalidate cache since we modified/replaced the file
                _fileService?.InvalidateCacheForGame(game);

                var issuesSummary = probeResult.HasIssues
                    ? $"Issues detected: {probeResult.Issues}\n\n"
                    : "No obvious issues detected, but file was re-encoded.\n\n";

                _playniteApi.Dialogs.ShowMessage(
                    $"Successfully repaired: {fileName}\n\n" +
                    issuesSummary +
                    "The original file has been backed up to PreservedOriginals folder.\n" +
                    "The repaired file should now play correctly.",
                    "UniPlaySong - Repair Complete");

                _logger.Debug($"Audio repair completed successfully for: {fileName}");
            }
            else
            {
                _playniteApi.Dialogs.ShowMessage(
                    $"Failed to repair audio file: {fileName}\n\n" +
                    "The file may be too corrupted to repair, or FFmpeg encountered an error.\n" +
                    "Check the logs for more details.",
                    "UniPlaySong - Repair Failed");

                _logger.Error($"Audio repair failed for: {fileName}");
            }
        }

        /// <summary>
        /// Scans and repairs all audio files in a game's music folder
        /// </summary>
        public void RepairAllAudioFiles(Game game)
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () => ExecuteRepairAllAudioFiles(game),
                    context: $"repairing all audio files for '{game?.Name}'",
                    showUserMessage: true
                );
            }
            else
            {
                try
                {
                    ExecuteRepairAllAudioFiles(game);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error in RepairAllAudioFiles: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
                }
            }
        }

        private void ExecuteRepairAllAudioFiles(Game game)
        {
            _logger.Debug($"RepairAllAudioFiles called for game: {game?.Name ?? "null"}");

            if (_repairService == null)
            {
                _playniteApi.Dialogs.ShowMessage(
                    "Audio repair service not available.",
                    "UniPlaySong");
                return;
            }

            var settings = _getSettings?.Invoke();
            if (settings == null || string.IsNullOrWhiteSpace(settings.FFmpegPath))
            {
                _playniteApi.Dialogs.ShowMessage(
                    "FFmpeg is required for audio repair. Please configure FFmpeg path in Settings → Audio Normalization.",
                    "UniPlaySong");
                return;
            }

            if (!_repairService.ValidateFFmpegAvailable(settings.FFmpegPath))
            {
                _playniteApi.Dialogs.ShowMessage(
                    $"FFmpeg not found or not working at: {settings.FFmpegPath}\n\nPlease verify the path in Settings → Audio Normalization.",
                    "UniPlaySong");
                return;
            }

            var songs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
            if (songs.Count == 0)
            {
                _playniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                return;
            }

            // Show progress and scan/repair all files
            var progressTitle = "UniPlaySong - Scanning & Repairing Audio";
            var progressOptions = new GlobalProgressOptions(progressTitle, true)
            {
                IsIndeterminate = false
            };

            int scanned = 0;
            int repaired = 0;
            int failed = 0;
            int skipped = 0;

            _playniteApi.Dialogs.ActivateGlobalProgress((args) =>
            {
                args.ProgressMaxValue = songs.Count;

                foreach (var song in songs)
                {
                    if (args.CancelToken.IsCancellationRequested)
                    {
                        _logger.Debug("Audio repair scan cancelled by user");
                        break;
                    }

                    var fileName = Path.GetFileName(song);
                    args.Text = $"Scanning: {fileName}";
                    args.CurrentProgressValue = ++scanned;

                    try
                    {
                        // Probe the file
                        var probeTask = _repairService.ProbeFileAsync(song, settings.FFmpegPath);
                        probeTask.Wait();
                        var probeResult = probeTask.Result;

                        if (!probeResult.Success || !probeResult.HasIssues)
                        {
                            skipped++;
                            continue;
                        }

                        // File has issues - repair it
                        args.Text = $"Repairing: {fileName}";
                        _logger.Debug($"Repairing file with issues ({probeResult.Issues}): {fileName}");

                        var repairTask = _repairService.RepairFileAsync(song, settings.FFmpegPath);
                        repairTask.Wait();

                        if (repairTask.Result)
                        {
                            repaired++;
                            _logger.Debug($"Successfully repaired: {fileName}");
                        }
                        else
                        {
                            failed++;
                            _logger.Error($"Failed to repair: {fileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.Error(ex, $"Error processing {fileName}: {ex.Message}");
                    }
                }
            }, progressOptions);

            // Invalidate cache if we repaired any files
            if (repaired > 0)
            {
                _fileService?.InvalidateCacheForGame(game);
            }

            // Show summary
            var summary = $"Scan & Repair Complete\n\n" +
                         $"Files scanned: {scanned}\n" +
                         $"No issues found: {skipped}\n" +
                         $"Repaired: {repaired}\n" +
                         $"Failed: {failed}";

            if (repaired > 0)
            {
                summary += "\n\nOriginal files have been backed up to PreservedOriginals folder.";
            }

            _playniteApi.Dialogs.ShowMessage(summary, "UniPlaySong - Repair Complete");
            _logger.Debug($"Audio repair scan complete - Scanned: {scanned}, Repaired: {repaired}, Failed: {failed}, Skipped: {skipped}");
        }

        #endregion

        #region Bulk Delete

        /// <summary>
        /// Deletes all music for multiple games with confirmation
        /// </summary>
        public void DeleteAllMusicForGames(List<Game> games)
        {
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () => ExecuteDeleteAllMusicForGames(games),
                    context: $"deleting music for {games.Count} games",
                    showUserMessage: true
                );
            }
            else
            {
                try
                {
                    ExecuteDeleteAllMusicForGames(games);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error in DeleteAllMusicForGames: {ex.Message}");
                    _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "UniPlaySong");
                }
            }
        }

        private void ExecuteDeleteAllMusicForGames(List<Game> games)
        {
            _logger.Debug($"DeleteAllMusicForGames called for {games.Count} game(s)");

            if (games == null || games.Count == 0)
            {
                _playniteApi.Dialogs.ShowMessage("No games selected.", "UniPlaySong");
                return;
            }

            // Count games that have music
            var gamesWithMusic = games.Where(g => _fileService?.HasMusic(g) == true).ToList();

            if (gamesWithMusic.Count == 0)
            {
                _playniteApi.Dialogs.ShowMessage("None of the selected games have music files.", "UniPlaySong");
                return;
            }

            // Show confirmation dialog
            var confirmResult = _playniteApi.Dialogs.ShowMessage(
                $"Delete all music for {gamesWithMusic.Count} games?\n\nThis action cannot be undone.",
                "UniPlaySong - Confirm Delete",
                MessageBoxButton.YesNo
            );

            if (confirmResult != MessageBoxResult.Yes)
            {
                _logger.Debug("User cancelled bulk delete");
                return;
            }

            // Stop playback if any of the games being deleted is currently playing
            if (_playbackService != null)
            {
                var currentGame = _playbackService.CurrentGame;
                if (currentGame != null && gamesWithMusic.Any(g => g.Id == currentGame.Id))
                {
                    _logger.Debug($"Stopping playback for '{currentGame.Name}' before deleting music");
                    _playbackService.Stop();
                }
            }

            // Perform deletion
            var totalDeleted = 0;
            var gamesDeleted = 0;

            foreach (var game in gamesWithMusic)
            {
                var deleted = _fileService.DeleteAllMusic(game);
                if (deleted > 0)
                {
                    totalDeleted += deleted;
                    gamesDeleted++;
                    _logger.Debug($"Deleted {deleted} music file(s) for '{game.Name}'");
                }
            }

            // Show result
            _playniteApi.Dialogs.ShowMessage(
                $"Deleted {totalDeleted} music file(s) from {gamesDeleted} game(s).",
                "UniPlaySong - Delete Complete"
            );

            _logger.Debug($"Bulk delete complete - {totalDeleted} files deleted from {gamesDeleted} games");
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

