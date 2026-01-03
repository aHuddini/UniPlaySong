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
    /// Handles audio normalization dialog operations.
    /// Extracted from UniPlaySong.cs to reduce main plugin file size.
    /// </summary>
    public class NormalizationDialogHandler
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI _playniteApi;
        private readonly INormalizationService _normalizationService;
        private readonly IMusicPlaybackService _playbackService;
        private readonly GameMusicFileService _fileService;
        private readonly Func<UniPlaySongSettings> _settingsProvider;

        public NormalizationDialogHandler(
            IPlayniteAPI playniteApi,
            INormalizationService normalizationService,
            IMusicPlaybackService playbackService,
            GameMusicFileService fileService,
            Func<UniPlaySongSettings> settingsProvider)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _normalizationService = normalizationService;
            _playbackService = playbackService;
            _fileService = fileService;
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        }

        /// <summary>
        /// Normalize all music files in the library
        /// </summary>
        public void NormalizeAllMusicFiles()
        {
            try
            {
                var settings = _settingsProvider();

                if (_normalizationService == null)
                {
                    _playniteApi.Dialogs.ShowErrorMessage("Normalization service not available.", "UniPlaySong");
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

                if (!_normalizationService.ValidateFFmpegAvailable(settings.FFmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"FFmpeg not found or not accessible at: {settings.FFmpegPath}\n\nPlease verify the FFmpeg path in extension settings.",
                        "FFmpeg Not Available");
                    return;
                }

                // Stop music playback before normalization to prevent file locking
                StopPlaybackForProcessing("bulk normalization");

                // Get all music files from all games
                var allMusicFiles = new List<string>();
                foreach (var game in _playniteApi.Database.Games)
                {
                    var gameFiles = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                    allMusicFiles.AddRange(gameFiles);
                }

                if (allMusicFiles.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("No music files found in your library.", "No Files to Normalize");
                    return;
                }

                // Show progress dialog
                ShowNormalizationProgress(allMusicFiles, "Normalizing All Music Files");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in NormalizeAllMusicFiles");
                _playniteApi.Dialogs.ShowErrorMessage($"Error starting normalization: {ex.Message}", "Normalization Error");
            }
        }

        /// <summary>
        /// Normalize music files for a single game (fullscreen menu)
        /// </summary>
        public void NormalizeSelectedGamesFullscreen(Game game)
        {
            if (game == null)
            {
                _playniteApi.Dialogs.ShowMessage("No game selected.", "Normalization Error");
                return;
            }

            NormalizeSelectedGames(new List<Game> { game }, showSimpleConfirmation: true);
        }

        /// <summary>
        /// Normalize music files for selected games
        /// </summary>
        public void NormalizeSelectedGames(List<Game> games, bool showSimpleConfirmation = false)
        {
            try
            {
                var settings = _settingsProvider();

                if (_normalizationService == null)
                {
                    _playniteApi.Dialogs.ShowErrorMessage("Normalization service not available.", "UniPlaySong");
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

                if (!_normalizationService.ValidateFFmpegAvailable(settings.FFmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"FFmpeg not found or not accessible at: {settings.FFmpegPath}\n\nPlease verify the FFmpeg path in extension settings.",
                        "FFmpeg Not Available");
                    return;
                }

                // Stop music playback before normalization to prevent file locking
                StopPlaybackForProcessing("normalizing selected games");

                // Get all music files from selected games
                var allMusicFiles = new List<string>();
                foreach (var game in games)
                {
                    var gameFiles = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                    allMusicFiles.AddRange(gameFiles);
                }

                if (allMusicFiles.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("No music files found for selected games.", "No Files to Normalize");
                    return;
                }

                // Show progress dialog
                var gameNames = string.Join(", ", games.Select(g => g.Name).Take(3));
                if (games.Count > 3) gameNames += $" and {games.Count - 3} more";
                ShowNormalizationProgress(allMusicFiles, $"Normalizing Music for {games.Count} Games", showSimpleConfirmation);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in NormalizeSelectedGames");
                _playniteApi.Dialogs.ShowErrorMessage($"Error starting normalization: {ex.Message}", "Normalization Error");
            }
        }

        /// <summary>
        /// Normalize a single music file
        /// </summary>
        public void NormalizeSingleFile(Game game, string filePath)
        {
            try
            {
                var settings = _settingsProvider();

                if (_normalizationService == null)
                {
                    _playniteApi.Dialogs.ShowErrorMessage("Normalization service not available.", "UniPlaySong");
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

                if (!_normalizationService.ValidateFFmpegAvailable(settings.FFmpegPath))
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

                // Stop music playback before normalization to prevent file locking
                StopPlaybackForProcessing("single file normalization");

                var fileName = System.IO.Path.GetFileName(filePath);
                ShowNormalizationProgress(new List<string> { filePath }, $"Normalizing: {fileName}", showSimpleConfirmation: true);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in NormalizeSingleFile");
                _playniteApi.Dialogs.ShowErrorMessage($"Error starting normalization: {ex.Message}", "Normalization Error");
            }
        }

        /// <summary>
        /// Show normalization progress dialog and execute normalization
        /// </summary>
        private void ShowNormalizationProgress(List<string> musicFiles, string title, bool showSimpleConfirmation = false)
        {
            try
            {
                var settings = _settingsProvider();

                // Create progress dialog
                var progressDialog = new Views.NormalizationProgressDialog();
                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    title,
                    progressDialog,
                    width: 600,
                    height: 500);

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "normalization dialog close");

                // Start normalization asynchronously
                Task.Run(async () =>
                {
                    try
                    {
                        var normSettings = new Models.NormalizationSettings
                        {
                            TargetLoudness = settings.NormalizationTargetLoudness,
                            TruePeak = settings.NormalizationTruePeak,
                            LoudnessRange = settings.NormalizationLoudnessRange,
                            AudioCodec = settings.NormalizationCodec,
                            NormalizationSuffix = settings.NormalizationSuffix,
                            TrimSuffix = settings.TrimSuffix,
                            SkipAlreadyNormalized = settings.SkipAlreadyNormalized,
                            DoNotPreserveOriginals = settings.DoNotPreserveOriginals,
                            FFmpegPath = settings.FFmpegPath
                        };

                        var progress = new Progress<Models.NormalizationProgress>(p =>
                        {
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                progressDialog.ReportProgress(p);
                            }));
                        });

                        var result = await _normalizationService.NormalizeBulkAsync(
                            musicFiles,
                            normSettings,
                            progress,
                            progressDialog.CancellationToken);

                        // Show completion message
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            // Close progress dialog first
                            window.Close();

                            if (showSimpleConfirmation)
                            {
                                // Simple confirmation for fullscreen menu
                                _playniteApi.Dialogs.ShowMessage(
                                    "Music normalized successfully.\n\nChanges will take effect when the game is re-selected.",
                                    "Normalization Complete");
                            }
                            else
                            {
                                // Detailed confirmation for settings menu
                                var message = $"Normalization Complete!\n\n" +
                                            $"Total: {result.TotalFiles} files\n" +
                                            $"Succeeded: {result.SuccessCount}\n" +
                                            $"Failed: {result.FailureCount}";

                                if (result.FailedFiles.Count > 0)
                                {
                                    message += $"\n\nFailed files:\n{string.Join("\n", result.FailedFiles.Take(5).Select(f => Path.GetFileName(f)))}";
                                    if (result.FailedFiles.Count > 5)
                                    {
                                        message += $"\n... and {result.FailedFiles.Count - 5} more";
                                    }
                                }

                                _playniteApi.Dialogs.ShowMessage(message, "Normalization Complete");
                            }
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            _playniteApi.Dialogs.ShowMessage("Normalization was cancelled.", "Normalization Cancelled");
                        }));
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error during normalization");
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            window.Close();
                            _playniteApi.Dialogs.ShowErrorMessage($"Error during normalization: {ex.Message}", "Normalization Error");
                        }));
                    }
                });

                // Show dialog
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing normalization progress dialog");
                _playniteApi.Dialogs.ShowErrorMessage($"Error showing progress dialog: {ex.Message}", "Normalization Error");
            }
        }

        /// <summary>
        /// Delete all files in the PreservedOriginals folder
        /// </summary>
        public void DeletePreservedOriginals()
        {
            try
            {
                var preservedOriginalsDir = Path.Combine(
                    Path.Combine(_playniteApi.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName),
                    Constants.PreservedOriginalsFolderName);

                if (!Directory.Exists(preservedOriginalsDir))
                {
                    _playniteApi.Dialogs.ShowMessage("PreservedOriginals folder does not exist or is empty.", "No Files to Delete");
                    return;
                }

                // Count files before deletion
                var allFiles = Directory.GetFiles(preservedOriginalsDir, "*.*", SearchOption.AllDirectories);
                var fileCount = allFiles.Length;

                if (fileCount == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("PreservedOriginals folder is empty.", "No Files to Delete");
                    return;
                }

                // Delete all files and directories
                try
                {
                    Directory.Delete(preservedOriginalsDir, true);
                    Directory.CreateDirectory(preservedOriginalsDir); // Recreate empty directory

                    logger.Info($"Deleted {fileCount} files from PreservedOriginals folder");
                    _playniteApi.Dialogs.ShowMessage(
                        $"Successfully deleted {fileCount} preserved original file(s).\n\nDisk space has been freed.",
                        "Deletion Complete");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Error deleting PreservedOriginals folder contents");
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"Error deleting preserved originals: {ex.Message}",
                        "Deletion Error");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in DeletePreservedOriginals");
                _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "Delete Preserved Originals Error");
            }
        }

        /// <summary>
        /// Restore original files from PreservedOriginals folder
        /// </summary>
        public void RestoreNormalizedFiles()
        {
            try
            {
                if (_normalizationService == null)
                {
                    _playniteApi.Dialogs.ShowErrorMessage("Normalization service not available.", "UniPlaySong");
                    return;
                }

                // Get all normalized files (files with -normalized suffix or files in music folders that have backups)
                var allMusicFiles = new List<string>();
                foreach (var game in _playniteApi.Database.Games)
                {
                    var gameFiles = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                    allMusicFiles.AddRange(gameFiles);
                }

                if (allMusicFiles.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("No music files found in your library.", "No Files to Restore");
                    return;
                }

                // Show progress dialog
                ShowRestoreProgress(allMusicFiles, "Restoring Original Files");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in RestoreNormalizedFiles");
                _playniteApi.Dialogs.ShowErrorMessage($"Error starting restore: {ex.Message}", "Restore Error");
            }
        }

        /// <summary>
        /// Show restoration progress dialog and execute restoration of original files
        /// </summary>
        private void ShowRestoreProgress(List<string> musicFiles, string title)
        {
            try
            {
                var settings = _settingsProvider();

                // Create progress dialog
                var progressDialog = new Views.NormalizationProgressDialog();
                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    title,
                    progressDialog,
                    width: 600,
                    height: 500);

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "restore dialog close");

                // Get normalization suffix from settings
                var suffix = settings?.NormalizationSuffix ?? "-normalized";

                // Start deletion asynchronously
                Task.Run(async () =>
                {
                    try
                    {
                        var progress = new Progress<Models.NormalizationProgress>(p =>
                        {
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                progressDialog.ReportProgress(p);
                            }));
                        });

                        var result = await _normalizationService.RestoreFromBackupsAsync(
                            musicFiles,
                            suffix,
                            progress,
                            progressDialog.CancellationToken);

                        // Show completion message
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            var message = $"Deletion Complete!\n\n" +
                                        $"Total: {result.TotalFiles} normalized files\n" +
                                        $"Deleted: {result.SuccessCount}\n" +
                                        $"Failed: {result.FailureCount}";

                            if (result.FailedFiles.Count > 0)
                            {
                                message += $"\n\nFailed files:\n{string.Join("\n", result.FailedFiles.Take(5).Select(f => Path.GetFileName(f)))}";
                                if (result.FailedFiles.Count > 5)
                                {
                                    message += $"\n... and {result.FailedFiles.Count - 5} more";
                                }
                            }

                            _playniteApi.Dialogs.ShowMessage(message, "Deletion Complete");
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            _playniteApi.Dialogs.ShowMessage("Restore was cancelled.", "Restore Cancelled");
                        }));
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error during restore");
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            _playniteApi.Dialogs.ShowErrorMessage($"Error during restore: {ex.Message}", "Restore Error");
                        }));
                    }
                });

                // Show dialog
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing restore progress dialog");
                _playniteApi.Dialogs.ShowErrorMessage($"Error showing progress dialog: {ex.Message}", "Restore Error");
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
                    logger.Info($"Stopping music playback before {context}");
                    _playbackService.Stop();

                    // Give a moment for files to be released
                    System.Threading.Thread.Sleep(300);
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"Error stopping playback before {context}");
            }
        }

    }
}
