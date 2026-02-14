using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Handlers
{
    /// <summary>
    /// Handles audio trimming dialog operations.
    /// Extracted from UniPlaySong.cs to reduce main plugin file size.
    /// </summary>
    public class TrimDialogHandler
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IPlayniteAPI _playniteApi;
        private readonly ITrimService _trimService;
        private readonly IMusicPlaybackService _playbackService;
        private readonly GameMusicFileService _fileService;
        private readonly Func<UniPlaySongSettings> _settingsProvider;

        public TrimDialogHandler(
            IPlayniteAPI playniteApi,
            ITrimService trimService,
            IMusicPlaybackService playbackService,
            GameMusicFileService fileService,
            Func<UniPlaySongSettings> settingsProvider)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _trimService = trimService;
            _playbackService = playbackService;
            _fileService = fileService;
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        }

        /// <summary>
        /// Trim leading silence from all music files in the library
        /// </summary>
        public void TrimAllMusicFiles()
        {
            try
            {
                var settings = _settingsProvider();

                if (_trimService == null)
                {
                    _playniteApi.Dialogs.ShowErrorMessage("Trim service not available.", "UniPlaySong");
                    return;
                }

                if (settings == null)
                {
                    _playniteApi.Dialogs.ShowErrorMessage("Settings not available.", "UniPlaySong");
                    return;
                }

                if (string.IsNullOrWhiteSpace(settings.FFmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg path is not configured. Please configure FFmpeg in Settings → Audio Normalization.",
                        "FFmpeg Not Configured");
                    return;
                }

                if (!_trimService.ValidateFFmpegAvailable(settings.FFmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"FFmpeg not found or not accessible at: {settings.FFmpegPath}\n\nPlease verify the FFmpeg path in extension settings.",
                        "FFmpeg Not Available");
                    return;
                }

                // Stop music playback before trimming to prevent file locking
                StopPlaybackForProcessing("bulk trim");

                // Get all music files from all games
                var allMusicFiles = new List<string>();
                foreach (var game in _playniteApi.Database.Games)
                {
                    var gameFiles = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                    allMusicFiles.AddRange(gameFiles);
                }

                if (allMusicFiles.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("No music files found in your library.", "No Files to Trim");
                    return;
                }

                // Show progress dialog
                ShowTrimProgress(allMusicFiles, "Trimming Leading Silence from All Music Files");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in TrimAllMusicFiles");
                _playniteApi.Dialogs.ShowErrorMessage($"Error starting trim: {ex.Message}", "Trim Error");
            }
        }

        /// <summary>
        /// Trim leading silence from music files for a single game (fullscreen menu)
        /// </summary>
        public void TrimSelectedGamesFullscreen(Game game)
        {
            if (game == null)
            {
                _playniteApi.Dialogs.ShowMessage("No game selected.", "Trim Error");
                return;
            }

            TrimSelectedGames(new List<Game> { game }, showSimpleConfirmation: true);
        }

        /// <summary>
        /// Trim leading silence from music files for selected games
        /// </summary>
        public void TrimSelectedGames(List<Game> games, bool showSimpleConfirmation = false)
        {
            try
            {
                var settings = _settingsProvider();

                if (_trimService == null)
                {
                    _playniteApi.Dialogs.ShowErrorMessage("Trim service not available.", "UniPlaySong");
                    return;
                }

                if (settings == null)
                {
                    _playniteApi.Dialogs.ShowErrorMessage("Settings not available.", "UniPlaySong");
                    return;
                }

                if (games == null || games.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("No games selected.", "No Games Selected");
                    return;
                }

                if (string.IsNullOrWhiteSpace(settings.FFmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg path is not configured. Please configure FFmpeg in Settings → Audio Normalization.",
                        "FFmpeg Not Configured");
                    return;
                }

                if (!_trimService.ValidateFFmpegAvailable(settings.FFmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"FFmpeg not found or not accessible at: {settings.FFmpegPath}\n\nPlease verify the FFmpeg path in extension settings.",
                        "FFmpeg Not Available");
                    return;
                }

                // Stop music playback before trimming to prevent file locking
                StopPlaybackForProcessing("trimming selected games");

                // Get all music files from selected games
                var allMusicFiles = new List<string>();
                foreach (var game in games)
                {
                    var gameFiles = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                    allMusicFiles.AddRange(gameFiles);
                }

                if (allMusicFiles.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("No music files found for selected games.", "No Files to Trim");
                    return;
                }

                // Show progress dialog
                var gameNames = string.Join(", ", games.Select(g => g.Name).Take(3));
                if (games.Count > 3) gameNames += $" and {games.Count - 3} more";
                ShowTrimProgress(allMusicFiles, $"Trimming Leading Silence for {games.Count} Games", showSimpleConfirmation);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in TrimSelectedGames");
                _playniteApi.Dialogs.ShowErrorMessage($"Error starting trim: {ex.Message}", "Trim Error");
            }
        }

        /// <summary>
        /// Trim a single music file
        /// </summary>
        public void TrimSingleFile(Game game, string filePath)
        {
            try
            {
                var settings = _settingsProvider();

                if (_trimService == null)
                {
                    _playniteApi.Dialogs.ShowErrorMessage("Trim service not available.", "UniPlaySong");
                    return;
                }

                if (settings == null)
                {
                    _playniteApi.Dialogs.ShowErrorMessage("Settings not available.", "UniPlaySong");
                    return;
                }

                if (string.IsNullOrWhiteSpace(settings.FFmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg path is not configured. Please configure FFmpeg in Settings → Audio Normalization.",
                        "FFmpeg Not Configured");
                    return;
                }

                if (!_trimService.ValidateFFmpegAvailable(settings.FFmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"FFmpeg not found or not accessible at: {settings.FFmpegPath}\n\nPlease verify the FFmpeg path in extension settings.",
                        "FFmpeg Not Available");
                    return;
                }

                if (!System.IO.File.Exists(filePath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage($"File not found: {filePath}", "File Not Found");
                    return;
                }

                // Stop music playback before trimming to prevent file locking
                StopPlaybackForProcessing("single file trim");

                var fileName = System.IO.Path.GetFileName(filePath);
                ShowTrimProgress(new List<string> { filePath }, $"Trimming: {fileName}", showSimpleConfirmation: true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in TrimSingleFile");
                _playniteApi.Dialogs.ShowErrorMessage($"Error starting trim: {ex.Message}", "Trim Error");
            }
        }

        /// <summary>
        /// Show trim progress dialog and execute trim operation
        /// </summary>
        private void ShowTrimProgress(List<string> musicFiles, string title, bool showSimpleConfirmation = false)
        {
            try
            {
                var settings = _settingsProvider();

                // Create progress dialog (reuse normalization progress dialog)
                var progressDialog = new Views.NormalizationProgressDialog();
                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    title,
                    progressDialog,
                    width: 600,
                    height: 500);

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "trim dialog close");

                // Start trim asynchronously
                Task.Run(async () =>
                {
                    try
                    {
                        var trimSettings = new Models.TrimSettings
                        {
                            SilenceThreshold = -50.0,
                            SilenceDuration = 0.1,
                            MinSilenceToTrim = 0.5,
                            TrimBuffer = 0.15,
                            TrimSuffix = settings.TrimSuffix,
                            SkipAlreadyTrimmed = true,
                            DoNotPreserveOriginals = false,
                            FFmpegPath = settings.FFmpegPath
                        };

                        var progress = new Progress<Models.NormalizationProgress>(p =>
                        {
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                progressDialog.ReportProgress(p);
                            }));
                        });

                        var result = await _trimService.TrimBulkAsync(
                            musicFiles,
                            trimSettings,
                            progress,
                            progressDialog.CancellationToken);

                        // Invalidate cache for all affected directories since we modified files
                        if (result.SuccessCount > 0 && _fileService != null)
                        {
                            var directories = musicFiles.Select(f => Path.GetDirectoryName(f)).Distinct();
                            foreach (var dir in directories)
                            {
                                _fileService.InvalidateCacheForDirectory(dir);
                            }
                        }

                        // Show completion message
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            // Close progress dialog first
                            window.Close();

                            // Build result message
                            var message = $"Trim Complete!\n\n" +
                                        $"Total: {result.TotalFiles} files\n" +
                                        $"Succeeded: {result.SuccessCount}\n" +
                                        $"Skipped: {result.SkippedCount}\n" +
                                        $"Failed: {result.FailureCount}";

                            if (!showSimpleConfirmation)
                            {
                                // Detailed view: Show file lists
                                if (result.SkippedFiles.Count > 0)
                                {
                                    message += $"\n\nSkipped files (not renamed - no leading silence or already trimmed):\n{string.Join("\n", result.SkippedFiles.Take(3).Select(f => Path.GetFileName(f)))}";
                                    if (result.SkippedFiles.Count > 3)
                                    {
                                        message += $"\n... and {result.SkippedFiles.Count - 3} more";
                                    }
                                }

                                if (result.FailedFiles.Count > 0)
                                {
                                    message += $"\n\nFailed files (not renamed - processing error):\n{string.Join("\n", result.FailedFiles.Take(3).Select(f => Path.GetFileName(f)))}";
                                    if (result.FailedFiles.Count > 3)
                                    {
                                        message += $"\n... and {result.FailedFiles.Count - 3} more";
                                    }
                                }
                            }

                            // Add status explanation
                            if (result.SuccessCount > 0 && (result.SkippedCount > 0 || result.FailureCount > 0))
                            {
                                message += "\n\nNote: Only successfully trimmed files have the suffix appended.";
                            }
                            else if (result.SkippedCount > 0 && result.SuccessCount == 0)
                            {
                                message += "\n\nNote: Files were not renamed (no trimming was needed).";
                            }

                            // Add re-select note if any files were successfully trimmed
                            if (result.SuccessCount > 0)
                            {
                                message += "\nChanges will take effect when the game is re-selected.";
                            }

                            _playniteApi.Dialogs.ShowMessage(message, "Trim Complete");
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            _playniteApi.Dialogs.ShowMessage("Trim was cancelled.", "Trim Cancelled");
                        }));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error during trim");
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            window.Close();
                            _playniteApi.Dialogs.ShowErrorMessage($"Error during trim: {ex.Message}", "Trim Error");
                        }));
                    }
                });

                // Show dialog
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error showing trim progress dialog");
                _playniteApi.Dialogs.ShowErrorMessage($"Error showing progress dialog: {ex.Message}", "Trim Error");
            }
        }

        /// <summary>
        /// Helper method to stop music playback before file processing
        /// </summary>
        private void StopPlaybackForProcessing(string context)
        {
            try
            {
                if (_playbackService != null && _playbackService.IsPlaying)
                {
                    Logger.Debug($"Stopping music playback before {context}");
                    _playbackService.Stop();

                    // Give a moment for files to be released
                    System.Threading.Thread.Sleep(300);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Error stopping playback before {context}");
            }
        }

    }
}
