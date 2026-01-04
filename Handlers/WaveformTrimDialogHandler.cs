using System;
using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Handlers
{
    /// <summary>
    /// Handles waveform-based precise trim dialog operations
    /// </summary>
    public class WaveformTrimDialogHandler
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        /// <summary>
        /// Logs a debug message only if debug logging is enabled in settings.
        /// </summary>
        private static void LogDebug(string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                Logger.Debug($"[PreciseTrim] {message}");
            }
        }

        private readonly IPlayniteAPI _playniteApi;
        private readonly Func<UniPlaySongSettings> _settingsProvider;
        private readonly GameMusicFileService _fileService;
        private readonly IMusicPlaybackService _playbackService;
        private readonly IWaveformTrimService _waveformTrimService;

        public WaveformTrimDialogHandler(
            IPlayniteAPI playniteApi,
            Func<UniPlaySongSettings> settingsProvider,
            GameMusicFileService fileService,
            IMusicPlaybackService playbackService,
            IWaveformTrimService waveformTrimService)
        {
            _playniteApi = playniteApi;
            _settingsProvider = settingsProvider;
            _fileService = fileService;
            _playbackService = playbackService;
            _waveformTrimService = waveformTrimService;
        }

        /// <summary>
        /// Show the desktop waveform trim dialog for a game
        /// </summary>
        public void ShowPreciseTrimDialog(Game game)
        {
            try
            {
                LogDebug($"ShowPreciseTrimDialog called for game: {game?.Name}");

                if (game == null)
                {
                    LogDebug("No game selected, showing error");
                    _playniteApi.Dialogs.ShowMessage("No game selected.", "Precise Trim");
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
                if (string.IsNullOrEmpty(ffmpegPath) || !_waveformTrimService.ValidateFFmpegAvailable(ffmpegPath))
                {
                    LogDebug("FFmpeg validation failed");
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg is required for precise trimming.\n\n" +
                        "Please configure FFmpeg path in Settings → Audio Normalization.",
                        "FFmpeg Not Found");
                    return;
                }

                // Stop current playback
                LogDebug("Stopping current playback");
                _playbackService?.Stop();

                // Create and show dialog
                LogDebug("Creating desktop WaveformTrimDialog");
                var dialog = new Views.WaveformTrimDialog();
                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    $"Precise Trim - {game.Name}",
                    dialog,
                    width: 750,
                    height: 500);

                dialog.Initialize(
                    game,
                    _playniteApi,
                    _waveformTrimService,
                    _playbackService,
                    _fileService,
                    _settingsProvider);

                // Handle window closing
                window.Closing += (s, e) =>
                {
                    LogDebug("Desktop dialog closing, running cleanup");
                    dialog.Cleanup();
                };

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "precise trim dialog close");

                LogDebug("Showing desktop dialog");
                window.ShowDialog();
                LogDebug("Desktop dialog closed");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error showing precise trim dialog");
                _playniteApi.Dialogs.ShowErrorMessage(
                    $"Error opening precise trim dialog: {ex.Message}",
                    "Precise Trim Error");
            }
        }

        /// <summary>
        /// Show controller-friendly waveform trim dialog
        /// </summary>
        public void ShowControllerPreciseTrimDialog(Game game)
        {
            try
            {
                LogDebug($"ShowControllerPreciseTrimDialog called for game: {game?.Name}");

                if (game == null)
                {
                    LogDebug("No game selected, showing error");
                    _playniteApi.Dialogs.ShowMessage("No game selected.", "Precise Trim");
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
                if (string.IsNullOrEmpty(ffmpegPath) || !_waveformTrimService.ValidateFFmpegAvailable(ffmpegPath))
                {
                    LogDebug("FFmpeg validation failed");
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg is required for precise trimming.\n\n" +
                        "Please configure FFmpeg path in Settings → Audio Normalization.",
                        "FFmpeg Not Found");
                    return;
                }

                // Stop current playback
                LogDebug("Stopping current playback");
                _playbackService?.Stop();

                // Create and show controller dialog
                LogDebug("Creating controller ControllerWaveformTrimDialog");
                var dialog = new Views.ControllerWaveformTrimDialog();
                var window = DialogHelper.CreateFixedDialog(
                    _playniteApi,
                    $"🎮 Precise Trim - {game.Name}",
                    dialog,
                    width: 900,
                    height: 650);

                dialog.InitializeForGame(
                    game,
                    _playniteApi,
                    _fileService,
                    _playbackService,
                    _waveformTrimService,
                    _settingsProvider);

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "controller precise trim dialog close");

                LogDebug("Showing controller dialog");
                window.ShowDialog();
                LogDebug("Controller dialog closed");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error showing controller precise trim dialog");
                _playniteApi.Dialogs.ShowErrorMessage(
                    $"Error opening precise trim dialog: {ex.Message}",
                    "Precise Trim Error");
            }
        }
    }
}
