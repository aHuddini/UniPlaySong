using System;
using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Handlers
{
    /// <summary>
    /// Handles audio amplification dialog operations
    /// </summary>
    public class AmplifyDialogHandler
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private static void LogDebug(string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                logger.Debug($"[Amplify] {message}");
            }
        }

        private readonly IPlayniteAPI _playniteApi;
        private readonly Func<UniPlaySongSettings> _settingsProvider;
        private readonly GameMusicFileService _fileService;
        private readonly IMusicPlaybackService _playbackService;
        private readonly AudioAmplifyService _amplifyService;

        public AmplifyDialogHandler(
            IPlayniteAPI playniteApi,
            Func<UniPlaySongSettings> settingsProvider,
            GameMusicFileService fileService,
            IMusicPlaybackService playbackService,
            AudioAmplifyService amplifyService)
        {
            _playniteApi = playniteApi;
            _settingsProvider = settingsProvider;
            _fileService = fileService;
            _playbackService = playbackService;
            _amplifyService = amplifyService;
        }

        /// <summary>
        /// Show the desktop amplify dialog for a game
        /// </summary>
        public void ShowAmplifyDialog(Game game)
        {
            try
            {
                LogDebug($"ShowAmplifyDialog called for game: {game?.Name}");

                if (game == null)
                {
                    _playniteApi.Dialogs.ShowMessage("No game selected.", "Amplify Audio");
                    return;
                }

                // Check if there are any songs
                var availableSongs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                LogDebug($"Found {availableSongs.Count} available songs for game");
                if (availableSongs.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage(
                        $"No music files found for '{game.Name}'.\n\nDownload some music first.",
                        "No Music Files");
                    return;
                }

                // Validate FFmpeg
                var settings = _settingsProvider?.Invoke();
                var ffmpegPath = settings?.FFmpegPath;
                LogDebug($"FFmpeg path from settings: {ffmpegPath}");
                if (string.IsNullOrEmpty(ffmpegPath) || !_amplifyService.ValidateFFmpegAvailable(ffmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg is required for audio amplification.\n\n" +
                        "Please configure FFmpeg path in Settings → Audio Normalization.",
                        "FFmpeg Not Found");
                    return;
                }

                // Stop current playback
                LogDebug("Stopping current playback");
                _playbackService?.Stop();

                // Create and show dialog
                LogDebug("Creating desktop AmplifyDialog");
                var dialog = new Views.AmplifyDialog();
                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    $"Amplify Audio - {game.Name}",
                    dialog,
                    width: 750,
                    height: 500);

                dialog.Initialize(
                    game,
                    _playniteApi,
                    _amplifyService,
                    _playbackService,
                    _fileService,
                    _settingsProvider);

                // Handle window closing
                window.Closing += (s, e) =>
                {
                    LogDebug("Desktop dialog closing, running cleanup");
                    dialog.Cleanup();
                };

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "amplify dialog close");

                LogDebug("Showing desktop dialog");
                window.ShowDialog();
                LogDebug("Desktop dialog closed");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing amplify dialog");
                _playniteApi.Dialogs.ShowErrorMessage(
                    $"Error opening amplify dialog: {ex.Message}",
                    "Amplify Error");
            }
        }

        /// <summary>
        /// Show controller-friendly amplify dialog
        /// </summary>
        public void ShowControllerAmplifyDialog(Game game)
        {
            try
            {
                LogDebug($"ShowControllerAmplifyDialog called for game: {game?.Name}");

                if (game == null)
                {
                    _playniteApi.Dialogs.ShowMessage("No game selected.", "Amplify Audio");
                    return;
                }

                // Check if there are any songs
                var availableSongs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                LogDebug($"Found {availableSongs.Count} available songs for game");
                if (availableSongs.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage(
                        $"No music files found for '{game.Name}'.\n\nDownload some music first.",
                        "No Music Files");
                    return;
                }

                // Validate FFmpeg
                var settings = _settingsProvider?.Invoke();
                var ffmpegPath = settings?.FFmpegPath;
                LogDebug($"FFmpeg path from settings: {ffmpegPath}");
                if (string.IsNullOrEmpty(ffmpegPath) || !_amplifyService.ValidateFFmpegAvailable(ffmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg is required for audio amplification.\n\n" +
                        "Please configure FFmpeg path in Settings → Audio Normalization.",
                        "FFmpeg Not Found");
                    return;
                }

                // Stop current playback
                LogDebug("Stopping current playback");
                _playbackService?.Stop();

                // Create and show controller dialog
                LogDebug("Creating controller ControllerAmplifyDialog");
                var dialog = new Views.ControllerAmplifyDialog();
                var window = DialogHelper.CreateFixedDialog(
                    _playniteApi,
                    $"🎮 Amplify Audio - {game.Name}",
                    dialog,
                    width: 900,
                    height: 650);

                dialog.InitializeForGame(
                    game,
                    _playniteApi,
                    _fileService,
                    _playbackService,
                    _amplifyService,
                    _settingsProvider);

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "controller amplify dialog close");

                LogDebug("Showing controller dialog");
                window.ShowDialog();
                LogDebug("Controller dialog closed");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing controller amplify dialog");
                _playniteApi.Dialogs.ShowErrorMessage(
                    $"Error opening amplify dialog: {ex.Message}",
                    "Amplify Error");
            }
        }
    }
}
