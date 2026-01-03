using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Downloaders;
using UniPlaySong.Services;

namespace UniPlaySong.Handlers
{
    /// <summary>
    /// Handles controller-friendly dialog operations for game music management.
    /// Extracted from UniPlaySong.cs to reduce main plugin file size.
    /// </summary>
    public class ControllerDialogHandler
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI _playniteApi;
        private readonly GameMusicFileService _fileService;
        private readonly IMusicPlaybackService _playbackService;
        private readonly DownloadDialogService _downloadDialogService;
        private readonly IDownloadManager _downloadManager;

        public ControllerDialogHandler(
            IPlayniteAPI playniteApi,
            GameMusicFileService fileService,
            IMusicPlaybackService playbackService,
            DownloadDialogService downloadDialogService,
            IDownloadManager downloadManager)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _playbackService = playbackService;
            _downloadDialogService = downloadDialogService;
            _downloadManager = downloadManager;
        }

        /// <summary>
        /// Show controller-friendly file picker for setting primary song
        /// </summary>
        public void ShowSetPrimarySong(Game game)
        {
            try
            {
                logger.Debug($"ShowControllerSetPrimarySong called for game: {game?.Name}");

                var filePickerDialog = new Views.ControllerFilePickerDialog();
                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    $"Set Primary Song - {game?.Name ?? "Unknown Game"}",
                    filePickerDialog,
                    width: 700,
                    height: 500);

                filePickerDialog.InitializeForGame(game, _playniteApi, _fileService, _playbackService, Views.ControllerFilePickerDialog.DialogMode.SetPrimary);
                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "set primary dialog close");

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing controller set primary song dialog");
                _playniteApi.Dialogs.ShowErrorMessage("Failed to open primary song selector.", "UniPlaySong");
            }
        }

        /// <summary>
        /// Clear the primary song for a game (no dialog needed - just clear and notify)
        /// </summary>
        public void ClearPrimarySong(Game game)
        {
            try
            {
                logger.Debug($"ClearPrimarySong called for game: {game?.Name}");

                // Check if there is a primary song to remove
                var currentPrimary = _fileService?.GetPrimarySong(game);
                if (string.IsNullOrEmpty(currentPrimary))
                {
                    _playniteApi.Dialogs.ShowMessage("No primary song is currently set for this game.", "UniPlaySong");
                    return;
                }

                // Get the filename before clearing
                var fileName = System.IO.Path.GetFileName(currentPrimary);

                // Clear the primary song
                _fileService?.RemovePrimarySong(game);

                // Notify the user
                _playniteApi.Dialogs.ShowMessage(
                    $"Primary song cleared:\n{fileName}\n\nSong selection will be randomized on application startup.",
                    "UniPlaySong");

                logger.Info($"Cleared primary song for game '{game?.Name}': {fileName}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error clearing primary song");
                _playniteApi.Dialogs.ShowErrorMessage($"Failed to clear primary song: {ex.Message}", "UniPlaySong");
            }
        }

        /// <summary>
        /// Show controller-friendly dialog for deleting songs
        /// </summary>
        public void ShowDeleteSongs(Game game)
        {
            try
            {
                logger.Debug($"ShowControllerDeleteSongs called for game: {game?.Name}");

                // Check if there are any songs to delete
                var availableSongs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                if (availableSongs.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                    return;
                }

                var deleteSongsDialog = new Views.ControllerDeleteSongsDialog();
                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    $"Delete Songs - {game?.Name ?? "Unknown Game"}",
                    deleteSongsDialog,
                    width: 750,
                    height: 550);

                deleteSongsDialog.InitializeForGame(game, _playniteApi, _fileService, _playbackService);
                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "delete songs dialog close");

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing controller delete songs dialog");
                _playniteApi.Dialogs.ShowErrorMessage("Failed to open song deletion dialog.", "UniPlaySong");
            }
        }

        /// <summary>
        /// Show the controller-optimized download dialog for a specific game
        /// </summary>
        public void ShowDownloadDialog(Game game)
        {
            try
            {
                logger.Info($"Opening controller download dialog for game: {game?.Name}");

                var controllerDialog = new Views.SimpleControllerDialog();
                var window = DialogHelper.CreateDialog(_playniteApi, controllerDialog, new DialogHelper.DialogOptions
                {
                    Title = $"Download Music - {game?.Name ?? "Unknown Game"}",
                    Width = 800,
                    Height = 600,
                    CanResize = true,
                    ShowMaximizeButton = true,
                    ApplyDarkBackground = true  // For fullscreen compatibility
                });

                // Initialize the dialog with real download functionality
                controllerDialog.InitializeForGame(game, _downloadDialogService, _playniteApi, _downloadManager, _playbackService, _fileService);

                // Ensure the window stays in fullscreen context
                window.Focusable = true;
                window.KeyDown += (s, e) => controllerDialog.Focus();

                // Show the dialog and ensure it gets focus
                window.Loaded += (s, e) =>
                {
                    window.Activate();
                    window.Focus();
                    controllerDialog.Focus();
                };

                // Handle window closing to prevent focus loss and dark overlay
                window.Closing += (s, e) =>
                {
                    logger.Debug("Controller dialog window closing");
                    DialogHelper.ReturnFocusToMainWindow(_playniteApi, "controller download dialog close");
                };

                window.Closed += (s, e) =>
                {
                    try
                    {
                        logger.Debug("Controller dialog window closed");

                        // Additional focus restoration after window is fully closed
                        Task.Delay(50).ContinueWith(_ =>
                        {
                            try
                            {
                                var mainWindow = _playniteApi.Dialogs.GetCurrentAppWindow();
                                if (mainWindow != null)
                                {
                                    mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        mainWindow.Activate();
                                        mainWindow.Focus();
                                    }));
                                }
                            }
                            catch (Exception delayEx)
                            {
                                logger.Debug(delayEx, "Error in delayed focus restoration");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, "Error in window closed handler");
                    }
                };

                var result = window.ShowDialog();

                logger.Info($"Controller download dialog completed with result: {result}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing controller download dialog");
                _playniteApi.Dialogs.ShowErrorMessage($"Error showing download dialog: {ex.Message}", "Download Dialog Error");
            }
        }

        /// <summary>
        /// Show controller-friendly file picker for normalizing an individual song
        /// </summary>
        public void ShowNormalizeIndividualSong(Game game)
        {
            try
            {
                logger.Debug($"ShowNormalizeIndividualSong called for game: {game?.Name}");

                // Check if there are any songs
                var availableSongs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                if (availableSongs.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                    return;
                }

                var filePickerDialog = new Views.ControllerFilePickerDialog();
                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    $"Normalize Individual Song - {game?.Name ?? "Unknown Game"}",
                    filePickerDialog,
                    width: 700,
                    height: 500);

                filePickerDialog.InitializeForGame(game, _playniteApi, _fileService, _playbackService, Views.ControllerFilePickerDialog.DialogMode.NormalizeIndividual);
                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "normalize individual dialog close");

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing normalize individual song dialog");
                _playniteApi.Dialogs.ShowErrorMessage("Failed to open song selector.", "UniPlaySong");
            }
        }

        /// <summary>
        /// Show controller-friendly file picker for trimming an individual song
        /// </summary>
        public void ShowTrimIndividualSong(Game game)
        {
            try
            {
                logger.Debug($"ShowTrimIndividualSong called for game: {game?.Name}");

                // Check if there are any songs
                var availableSongs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                if (availableSongs.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                    return;
                }

                var filePickerDialog = new Views.ControllerFilePickerDialog();
                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    $"Silence Trim - {game?.Name ?? "Unknown Game"}",
                    filePickerDialog,
                    width: 700,
                    height: 500);

                filePickerDialog.InitializeForGame(game, _playniteApi, _fileService, _playbackService, Views.ControllerFilePickerDialog.DialogMode.TrimIndividual);
                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "silence trim dialog close");

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing trim individual song dialog");
                _playniteApi.Dialogs.ShowErrorMessage("Failed to open song selector.", "UniPlaySong");
            }
        }

        /// <summary>
        /// Show controller-friendly file picker for repairing an individual audio file
        /// </summary>
        public void ShowRepairIndividualSong(Game game)
        {
            try
            {
                logger.Debug($"ShowRepairIndividualSong called for game: {game?.Name}");

                // Check if there are any songs
                var availableSongs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                if (availableSongs.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("No music files found for this game.", "UniPlaySong");
                    return;
                }

                var filePickerDialog = new Views.ControllerFilePickerDialog();
                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    $"Repair Audio File - {game?.Name ?? "Unknown Game"}",
                    filePickerDialog,
                    width: 700,
                    height: 500);

                filePickerDialog.InitializeForGame(game, _playniteApi, _fileService, _playbackService, Views.ControllerFilePickerDialog.DialogMode.RepairIndividual);
                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "repair audio dialog close");

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing repair individual song dialog");
                _playniteApi.Dialogs.ShowErrorMessage("Failed to open song selector.", "UniPlaySong");
            }
        }

    }
}
