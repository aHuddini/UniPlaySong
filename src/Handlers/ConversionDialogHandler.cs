using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Handlers
{
    // Handles bulk audio format conversion dialog operations
    public class ConversionDialogHandler
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IPlayniteAPI _playniteApi;
        private readonly AudioConversionService _conversionService;
        private readonly IMusicPlaybackService _playbackService;
        private readonly GameMusicFileService _fileService;
        private readonly Func<UniPlaySongSettings> _settingsProvider;

        public ConversionDialogHandler(
            IPlayniteAPI playniteApi,
            AudioConversionService conversionService,
            IMusicPlaybackService playbackService,
            GameMusicFileService fileService,
            Func<UniPlaySongSettings> settingsProvider)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _conversionService = conversionService;
            _playbackService = playbackService;
            _fileService = fileService;
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        }

        // Convert all music files in the library to the configured format
        public async void ConvertAllMusicFiles()
        {
            try
            {
                var settings = _settingsProvider();

                if (_conversionService == null)
                {
                    _playniteApi.Dialogs.ShowErrorMessage("Conversion service not available.", "UniPlaySong");
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
                        "FFmpeg path is not configured. Please configure FFmpeg in Settings → Editing tab.",
                        "FFmpeg Not Configured");
                    return;
                }

                if (!_conversionService.ValidateFFmpegAvailable(settings.FFmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"FFmpeg not found or not accessible at: {settings.FFmpegPath}\n\nPlease verify the FFmpeg path in extension settings.",
                        "FFmpeg Not Available");
                    return;
                }

                await StopPlaybackForProcessingAsync("bulk conversion");

                var allMusicFiles = new List<string>();
                foreach (var game in _playniteApi.Database.Games)
                {
                    var gameFiles = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                    allMusicFiles.AddRange(gameFiles);
                }

                if (allMusicFiles.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("No music files found in your library.", "No Files to Convert");
                    return;
                }

                var format = settings.ConversionTargetFormat?.ToUpperInvariant() ?? "OGG";
                var bitrate = settings.ConversionBitrate ?? "192";
                var keepText = settings.ConversionKeepOriginals
                    ? "Original files will be kept (renamed with -preconvert suffix)."
                    : "Original files will be DELETED after conversion.";

                var confirm = _playniteApi.Dialogs.ShowMessage(
                    $"Convert {allMusicFiles.Count} music file(s) to .{format} at {bitrate}kbps?\n\n{keepText}\n\nFiles already in .{format} format will be skipped.\n\nThis operation cannot be undone.",
                    "Confirm Bulk Conversion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                    return;

                ShowConversionProgress(allMusicFiles);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in ConvertAllMusicFiles");
                _playniteApi.Dialogs.ShowErrorMessage($"Error starting conversion: {ex.Message}", "Conversion Error");
            }
        }

        private void ShowConversionProgress(List<string> musicFiles)
        {
            try
            {
                var settings = _settingsProvider();
                var format = settings.ConversionTargetFormat?.ToUpperInvariant() ?? "OGG";
                var bitrate = settings.ConversionBitrate ?? "192";

                var progressDialog = new Views.NormalizationProgressDialog();
                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    $"Converting to {format} {bitrate}kbps",
                    progressDialog,
                    width: 600,
                    height: 500);

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "conversion dialog close");

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

                        var result = await _conversionService.ConvertBulkAsync(
                            musicFiles,
                            settings.FFmpegPath,
                            settings.ConversionTargetFormat ?? "ogg",
                            settings.ConversionBitrate ?? "192",
                            settings.ConversionKeepOriginals,
                            progress,
                            progressDialog.CancellationToken);

                        // Invalidate cache for all affected directories
                        if (result.SuccessCount > 0 && _fileService != null)
                        {
                            var directories = musicFiles.Select(f => Path.GetDirectoryName(f)).Distinct();
                            foreach (var dir in directories)
                            {
                                _fileService.InvalidateCacheForDirectory(dir);
                            }
                        }

                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            window.Close();

                            var originalSizeStr = result.FormatBytes(result.TotalOriginalBytes);
                            var newSizeStr = result.FormatBytes(result.TotalNewBytes);
                            var diff = result.TotalOriginalBytes - result.TotalNewBytes;

                            string sizeInfo;
                            if (diff >= 0)
                            {
                                var percentage = result.TotalOriginalBytes > 0
                                    ? (double)diff / result.TotalOriginalBytes * 100
                                    : 0;
                                sizeInfo = $"Original size: {originalSizeStr}\n" +
                                           $"New size: {newSizeStr}\n" +
                                           $"Space saved: {result.FormatBytes(diff)} ({percentage:0.#}%)";
                            }
                            else
                            {
                                var increase = -diff;
                                var percentage = result.TotalOriginalBytes > 0
                                    ? (double)increase / result.TotalOriginalBytes * 100
                                    : 0;
                                sizeInfo = $"Original size: {originalSizeStr}\n" +
                                           $"New size: {newSizeStr}\n" +
                                           $"Space increased: {result.FormatBytes(increase)} ({percentage:0.#}%)";
                            }

                            var message = $"Conversion Complete!\n\n" +
                                          $"Converted: {result.SuccessCount}\n" +
                                          $"Failed: {result.FailureCount}\n" +
                                          $"Total: {result.TotalFiles}\n\n" +
                                          sizeInfo;

                            if (result.FailedFiles.Count > 0)
                            {
                                message += $"\n\nFailed files:\n{string.Join("\n", result.FailedFiles.Take(5).Select(f => Path.GetFileName(f)))}";
                                if (result.FailedFiles.Count > 5)
                                {
                                    message += $"\n... and {result.FailedFiles.Count - 5} more";
                                }
                            }

                            _playniteApi.Dialogs.ShowMessage(message, "Conversion Complete");
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            window.Close();
                            _playniteApi.Dialogs.ShowMessage("Conversion was cancelled.", "Conversion Cancelled");
                        }));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error during conversion");
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            window.Close();
                            _playniteApi.Dialogs.ShowErrorMessage($"Error during conversion: {ex.Message}", "Conversion Error");
                        }));
                    }
                });

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error showing conversion progress dialog");
                _playniteApi.Dialogs.ShowErrorMessage($"Error showing progress dialog: {ex.Message}", "Conversion Error");
            }
        }

        // Stop music playback before file processing
        private async Task StopPlaybackForProcessingAsync(string context)
        {
            try
            {
                if (_playbackService != null && _playbackService.IsPlaying)
                {
                    Logger.Debug($"Stopping music playback before {context}");
                    _playbackService.Stop();
                    await Task.Delay(300);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Error stopping playback before {context}");
            }
        }
    }
}
