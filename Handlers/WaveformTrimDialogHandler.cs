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
        private const string LogPrefix = "PreciseTrim";

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
                Logger.DebugIf(LogPrefix,$"ShowPreciseTrimDialog called for game: {game?.Name}");

                if (game == null)
                {
                    Logger.DebugIf(LogPrefix,"No game selected, showing error");
                    _playniteApi.Dialogs.ShowMessage("No game selected.", "Precise Trim");
                    return;
                }

                // Check if there are any songs
                var availableSongs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                Logger.DebugIf(LogPrefix,$"Found {availableSongs.Count} available songs for game");
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
                Logger.DebugIf(LogPrefix,$"FFmpeg path from settings: {ffmpegPath}");
                if (string.IsNullOrEmpty(ffmpegPath) || !_waveformTrimService.ValidateFFmpegAvailable(ffmpegPath))
                {
                    Logger.DebugIf(LogPrefix,"FFmpeg validation failed");
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg is required for precise trimming.\n\n" +
                        "Please configure FFmpeg path in Settings → Audio Normalization.",
                        "FFmpeg Not Found");
                    return;
                }

                // Stop current playback
                Logger.DebugIf(LogPrefix,"Stopping current playback");
                _playbackService?.Stop();

                // Create and show dialog
                Logger.DebugIf(LogPrefix,"Creating desktop WaveformTrimDialog");
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
                    Logger.DebugIf(LogPrefix,"Desktop dialog closing, running cleanup");
                    dialog.Cleanup();
                };

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "precise trim dialog close");

                Logger.DebugIf(LogPrefix,"Showing desktop dialog");
                window.ShowDialog();
                Logger.DebugIf(LogPrefix,"Desktop dialog closed");
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
                Logger.DebugIf(LogPrefix,$"ShowControllerPreciseTrimDialog called for game: {game?.Name}");

                if (game == null)
                {
                    Logger.DebugIf(LogPrefix,"No game selected, showing error");
                    _playniteApi.Dialogs.ShowMessage("No game selected.", "Precise Trim");
                    return;
                }

                // Check if there are any songs
                var availableSongs = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                Logger.DebugIf(LogPrefix,$"Found {availableSongs.Count} available songs for game");
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
                Logger.DebugIf(LogPrefix,$"FFmpeg path from settings: {ffmpegPath}");
                if (string.IsNullOrEmpty(ffmpegPath) || !_waveformTrimService.ValidateFFmpegAvailable(ffmpegPath))
                {
                    Logger.DebugIf(LogPrefix,"FFmpeg validation failed");
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg is required for precise trimming.\n\n" +
                        "Please configure FFmpeg path in Settings → Audio Normalization.",
                        "FFmpeg Not Found");
                    return;
                }

                // Stop current playback
                Logger.DebugIf(LogPrefix,"Stopping current playback");
                _playbackService?.Stop();

                // Create and show controller dialog
                Logger.DebugIf(LogPrefix,"Creating controller ControllerWaveformTrimDialog");
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

                Logger.DebugIf(LogPrefix,"Showing controller dialog");
                window.ShowDialog();
                Logger.DebugIf(LogPrefix,"Controller dialog closed");
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
