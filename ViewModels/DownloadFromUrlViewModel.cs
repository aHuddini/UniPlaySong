using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Downloaders;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.ViewModels
{
    public class DownloadFromUrlViewModel : System.Collections.Generic.ObservableObject
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "DownloadFromUrl";

        private static readonly Regex YoutubeVideoRegex = new Regex(
            @"(?:youtube\.com\/(?:watch\?v=|embed\/|v\/)|youtu\.be\/)([a-zA-Z0-9_-]{11})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IPlayniteAPI _playniteApi;
        private readonly IDownloadManager _downloadManager;
        private readonly GameMusicFileService _fileService;
        private readonly SettingsService _settingsService;
        private readonly ErrorHandlerService _errorHandler;
        private readonly IMusicPlaybackService _playbackService;
        private readonly Game _game;

        private IMusicPlayer _previewPlayer;
        private CancellationTokenSource _currentCts;
        private string _previewFilePath;

        private string _title = "Download From URL";
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        private string _url = string.Empty;
        public string Url
        {
            get => _url;
            set
            {
                _url = value ?? string.Empty;
                OnPropertyChanged();
                if (!string.IsNullOrEmpty(_url))
                {
                    IsValidUrl = false;
                    ShowValidationResult = false;
                    VideoTitle = string.Empty;
                    VideoInfo = string.Empty;
                }
                UpdateCommandStates();
            }
        }

        private bool _isValidUrl = false;
        public bool IsValidUrl
        {
            get => _isValidUrl;
            set { _isValidUrl = value; OnPropertyChanged(); UpdateCommandStates(); }
        }

        private bool _showValidationResult = false;
        public bool ShowValidationResult
        {
            get => _showValidationResult;
            set { _showValidationResult = value; OnPropertyChanged(); }
        }

        private string _videoTitle = string.Empty;
        public string VideoTitle
        {
            get => _videoTitle;
            set { _videoTitle = value; OnPropertyChanged(); }
        }

        private string _videoInfo = string.Empty;
        public string VideoInfo
        {
            get => _videoInfo;
            set { _videoInfo = value; OnPropertyChanged(); }
        }

        private string _validationIcon = "Check";
        public string ValidationIcon
        {
            get => _validationIcon;
            set { _validationIcon = value; OnPropertyChanged(); }
        }

        private Brush _validationIconColor = Brushes.LimeGreen;
        public Brush ValidationIconColor
        {
            get => _validationIconColor;
            set { _validationIconColor = value; OnPropertyChanged(); }
        }

        private bool _isPreviewPlaying = false;
        public bool IsPreviewPlaying
        {
            get => _isPreviewPlaying;
            set { _isPreviewPlaying = value; OnPropertyChanged(); }
        }

        private bool _showProgress = false;
        public bool ShowProgress
        {
            get => _showProgress;
            set { _showProgress = value; OnPropertyChanged(); }
        }

        private bool _isIndeterminate = true;
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set { _isIndeterminate = value; OnPropertyChanged(); }
        }

        private double _progressValue = 0;
        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        private double _progressMax = 100;
        public double ProgressMax
        {
            get => _progressMax;
            set { _progressMax = value; OnPropertyChanged(); }
        }

        private string _progressText = string.Empty;
        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value ?? string.Empty; OnPropertyChanged(); }
        }

        private bool _isDownloading = false;
        public bool IsDownloading
        {
            get => _isDownloading;
            set { _isDownloading = value; OnPropertyChanged(); UpdateCommandStates(); }
        }

        private string _extractedVideoId = string.Empty;

        public ICommand ValidateUrlCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand DownloadCommand { get; set; }
        public ICommand CancelCommand { get; set; }

        public Action<bool> OnDownloadComplete { get; set; }

        public DownloadFromUrlViewModel(
            IPlayniteAPI playniteApi,
            IDownloadManager downloadManager,
            GameMusicFileService fileService,
            SettingsService settingsService,
            ErrorHandlerService errorHandler,
            IMusicPlaybackService playbackService,
            Game game)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _downloadManager = downloadManager ?? throw new ArgumentNullException(nameof(downloadManager));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _playbackService = playbackService; // Can be null
            _game = game ?? throw new ArgumentNullException(nameof(game));

            Title = $"Download From URL - {game.Name}";

            ValidateUrlCommand = new Common.RelayCommand(() => ValidateUrlSync(), CanValidateUrl);
            PreviewCommand = new Common.RelayCommand(() => TogglePreviewSync());
        }

        private bool CanValidateUrl()
        {
            return !string.IsNullOrWhiteSpace(Url) && !IsDownloading;
        }

        private void UpdateCommandStates()
        {
            if (ValidateUrlCommand is Common.RelayCommand validateCmd)
            {
                validateCmd.RaiseCanExecuteChanged();
            }
            if (DownloadCommand is Common.RelayCommand downloadCmd)
            {
                downloadCmd.RaiseCanExecuteChanged();
            }
        }

        private void ValidateUrlSync()
        {
            Task.Run(async () => await ValidateUrlAsync());
        }

        private async Task ValidateUrlAsync()
        {
            if (string.IsNullOrWhiteSpace(Url))
                return;

            UpdateOnUIThread(() =>
            {
                IsValidUrl = false;
                ShowValidationResult = false;
            });
            StopPreview();

            var match = YoutubeVideoRegex.Match(Url);
            if (!match.Success)
            {
                UpdateOnUIThread(() => ShowValidationError("Invalid URL. Please enter a valid YouTube video URL."));
                return;
            }

            _extractedVideoId = match.Groups[1].Value;
            Logger.DebugIf(LogPrefix,$"Extracted YouTube video ID: {_extractedVideoId}");

            UpdateOnUIThread(() =>
            {
                ShowProgress = true;
                IsIndeterminate = true;
                ProgressText = "Fetching video information...";
            });

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                var videoInfo = await Task.Run(() => GetVideoInfo(_extractedVideoId, _currentCts.Token));

                if (_currentCts.Token.IsCancellationRequested)
                    return;

                UpdateOnUIThread(() =>
                {
                    if (videoInfo != null)
                    {
                        VideoTitle = videoInfo.Title ?? "Unknown Title";
                        VideoInfo = $"{videoInfo.Channel ?? "Unknown Channel"} | {videoInfo.Duration ?? "Unknown Duration"}";
                        ValidationIcon = "CheckCircle";
                        ValidationIconColor = Brushes.LimeGreen;
                        IsValidUrl = true;
                        ShowValidationResult = true;
                        Logger.DebugIf(LogPrefix,$"Video validated: {VideoTitle}");
                    }
                    else
                    {
                        ShowValidationError("Could not fetch video information. The video may be unavailable or restricted.");
                    }
                    ShowProgress = false;
                });
            }
            catch (OperationCanceledException)
            {
                Logger.DebugIf(LogPrefix,"URL validation cancelled");
                UpdateOnUIThread(() => ShowProgress = false);
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(ex, "validating YouTube URL", showUserMessage: false);
                UpdateOnUIThread(() =>
                {
                    ShowValidationError($"Error validating URL: {ex.Message}");
                    ShowProgress = false;
                });
            }
        }

        private void UpdateOnUIThread(Action action)
        {
            var app = System.Windows.Application.Current;
            if (app?.Dispatcher != null)
            {
                if (app.Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    app.Dispatcher.Invoke(action);
                }
            }
            else
            {
                action();
            }
        }

        private void ShowValidationError(string message)
        {
            VideoTitle = message;
            VideoInfo = string.Empty;
            ValidationIcon = "AlertCircle";
            ValidationIconColor = Brushes.OrangeRed;
            IsValidUrl = false;
            ShowValidationResult = true;
        }

        private VideoInfoResult GetVideoInfo(string videoId, CancellationToken cancellationToken)
        {
            var ytDlpPath = _settingsService.Current?.YtDlpPath;
            if (string.IsNullOrWhiteSpace(ytDlpPath) || !File.Exists(ytDlpPath))
            {
                Logger.Error("yt-dlp not configured or not found");
                return null;
            }

            try
            {
                var url = $"https://www.youtube.com/watch?v={videoId}";
                // Rate limiting options to avoid throttling
                var rateLimitOptions = " --sleep-requests 1 --sleep-interval 2 --max-sleep-interval 5";
                var arguments = $"--dump-json --no-download{rateLimitOptions} \"{url}\"";

                if (_settingsService.Current?.UseFirefoxCookies == true)
                {
                    arguments = $"--cookies-from-browser firefox " + arguments;
                }

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    if (process == null)
                        return null;

                    var outputTask = Task.Run(() => process.StandardOutput.ReadToEnd());
                    var errorTask = Task.Run(() => process.StandardError.ReadToEnd());

                    while (!process.WaitForExit(100))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { process.Kill(); } catch { }
                            return null;
                        }
                    }

                    outputTask.Wait();
                    errorTask.Wait();

                    var output = outputTask.Result;
                    var error = errorTask.Result;

                    if (process.ExitCode != 0)
                    {
                        Logger.Error($"yt-dlp failed to get video info: {error}");
                        return null;
                    }

                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        try
                        {
                            var json = Newtonsoft.Json.Linq.JObject.Parse(output);
                            var durationToken = json["duration"];
                            var duration = durationToken != null ? (int)durationToken : 0;
                            var durationStr = duration > 0
                                ? TimeSpan.FromSeconds(duration).ToString(@"mm\:ss")
                                : "Unknown";

                            return new VideoInfoResult
                            {
                                Title = json["title"]?.ToString(),
                                Channel = json["channel"]?.ToString() ?? json["uploader"]?.ToString(),
                                Duration = durationStr,
                                VideoId = videoId
                            };
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to parse yt-dlp JSON output: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error getting video info: {ex.Message}");
            }

            return null;
        }

        private void TogglePreviewSync()
        {
            if (IsPreviewPlaying)
            {
                StopPreview();
                return;
            }

            Task.Run(async () => await TogglePreviewAsync());
        }

        private async Task TogglePreviewAsync()
        {
            if (!IsValidUrl || string.IsNullOrEmpty(_extractedVideoId))
                return;

            UpdateOnUIThread(() =>
            {
                ShowProgress = true;
                IsIndeterminate = true;
                ProgressText = "Downloading preview...";
            });

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                var tempDir = Path.Combine(Path.GetTempPath(), "UniPlaySong", "Preview");
                Directory.CreateDirectory(tempDir);
                _previewFilePath = Path.Combine(tempDir, $"{_extractedVideoId}_preview.mp3");

                if (!File.Exists(_previewFilePath))
                {
                    var song = new Song
                    {
                        Id = _extractedVideoId,
                        Name = VideoTitle,
                        Source = Source.YouTube
                    };

                    var success = await Task.Run(() =>
                        _downloadManager.DownloadSong(song, _previewFilePath, _currentCts.Token, isPreview: true));

                    if (!success || !File.Exists(_previewFilePath))
                    {
                        UpdateOnUIThread(() =>
                        {
                            ShowProgress = false;
                            _playniteApi.Dialogs.ShowErrorMessage("Failed to download preview. Check logs for details.", "UniPlaySong");
                        });
                        return;
                    }
                }

                UpdateOnUIThread(() =>
                {
                    ShowProgress = false;
                    PlayPreview(_previewFilePath);
                });
            }
            catch (OperationCanceledException)
            {
                Logger.DebugIf(LogPrefix,"Preview download cancelled");
                UpdateOnUIThread(() => ShowProgress = false);
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(ex, "downloading preview", showUserMessage: true);
                UpdateOnUIThread(() => ShowProgress = false);
            }
        }

        private void PlayPreview(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                _previewPlayer = new MusicPlayer();
                _previewPlayer.MediaEnded += (s, e) => StopPreview();
                _previewPlayer.MediaFailed += (s, e) =>
                {
                    _playniteApi.Dialogs.ShowErrorMessage($"Preview playback failed: {e.ErrorException?.Message ?? "Unknown error"}", "UniPlaySong");
                    StopPreview();
                };
                _previewPlayer.Volume = 0.7;
                _previewPlayer.Load(filePath);
                _previewPlayer.Play();
                IsPreviewPlaying = true;
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(ex, "playing preview", showUserMessage: true);
                StopPreview();
            }
        }

        public void StopPreview()
        {
            try
            {
                if (_previewPlayer != null)
                {
                    _previewPlayer.Stop();
                    _previewPlayer.Close();
                    _previewPlayer = null;
                }
                IsPreviewPlaying = false;
            }
            catch { }
        }

        public async Task DownloadAsync()
        {
            if (!IsValidUrl || string.IsNullOrEmpty(_extractedVideoId))
                return;

            StopPreview();
            IsDownloading = true;
            ShowProgress = true;
            IsIndeterminate = false;
            ProgressMax = 100;
            ProgressValue = 0;
            ProgressText = "Preparing download...";

            try
            {
                _currentCts?.Cancel();
                _currentCts = new CancellationTokenSource();

                var musicDir = _fileService.GetGameMusicDirectory(_game);
                Directory.CreateDirectory(musicDir);

                var sanitizedName = StringHelper.CleanForPath(VideoTitle);

                if (sanitizedName.Length > 100)
                {
                    sanitizedName = sanitizedName.Substring(0, 100);
                }

                var fileName = $"{sanitizedName}.mp3";
                var filePath = Path.Combine(musicDir, fileName);

                if (File.Exists(filePath))
                {
                    var overwrite = _playniteApi.Dialogs.ShowMessage(
                        $"A file named '{fileName}' already exists.\n\nDo you want to overwrite it?",
                        "UniPlaySong",
                        System.Windows.MessageBoxButton.YesNo);

                    if (overwrite != System.Windows.MessageBoxResult.Yes)
                    {
                        IsDownloading = false;
                        ShowProgress = false;
                        return;
                    }

                    try { File.Delete(filePath); } catch { }
                }

                ProgressText = $"Downloading: {VideoTitle}...";
                ProgressValue = 25;

                var song = new Song
                {
                    Id = _extractedVideoId,
                    Name = VideoTitle,
                    Source = Source.YouTube
                };

                var success = await Task.Run(() =>
                    _downloadManager.DownloadSong(song, filePath, _currentCts.Token, isPreview: false));

                if (_currentCts.Token.IsCancellationRequested)
                {
                    ProgressText = "Download cancelled.";
                    return;
                }

                ProgressValue = 100;

                if (success && File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    ProgressText = $"Download complete! ({fileInfo.Length / 1024.0 / 1024.0:F2} MB)";
                    Logger.Debug($"Successfully downloaded audio from URL to: {filePath}");

                    // Play notification sound in sync with the status text
                    if (_settingsService?.Current?.PlaySoundOnDownloadComplete == true)
                    {
                        bool previewWasPlaying = _previewPlayer != null && _previewPlayer.IsActive;
                        bool musicWasPlaying = _playbackService != null && _playbackService.IsPlaying;

                        if (previewWasPlaying)
                            try { _previewPlayer.Pause(); } catch { }
                        if (musicWasPlaying)
                            try { _playbackService.PauseImmediate(); } catch { }

                        System.Media.SystemSounds.Asterisk.Play();

                        // Fire-and-forget: resume audio after notification sound
#pragma warning disable CS4014
                        Task.Delay(1200).ContinueWith(_ =>
                        {
                            try
                            {
                                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    if (previewWasPlaying && _previewPlayer != null)
                                        try { _previewPlayer.Resume(); } catch { }
                                    if (musicWasPlaying && _playbackService != null)
                                        try { _playbackService.ResumeImmediate(); } catch { }
                                }));
                            }
                            catch { }
                        });
#pragma warning restore CS4014
                    }

                    // Invalidate song cache since we added a new file
                    _fileService.InvalidateCacheForGame(_game);

                    // Force reload music so the new song can play immediately
                    try
                    {
                        if (_playbackService != null && _settingsService?.Current != null)
                        {
                            Logger.DebugIf(LogPrefix,$"Force reloading music for game: {_game.Name}");
                            _playbackService.PlayGameMusic(_game, _settingsService.Current, forceReload: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.DebugIf(LogPrefix,$"Error reloading music after download: {ex.Message}");
                    }

                    await Task.Delay(1500);

                    OnDownloadComplete?.Invoke(true);
                }
                else
                {
                    ProgressText = "Download failed. Check logs for details.";
                    Logger.Error($"Download failed for video ID: {_extractedVideoId}");

                    await Task.Delay(2000);
                    OnDownloadComplete?.Invoke(false);
                }
            }
            catch (OperationCanceledException)
            {
                Logger.DebugIf(LogPrefix,"Download cancelled");
                ProgressText = "Download cancelled.";
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(ex, "downloading audio from URL", showUserMessage: true);
                ProgressText = $"Error: {ex.Message}";
            }
            finally
            {
                IsDownloading = false;
                await Task.Delay(1000);
                ShowProgress = false;
            }
        }

        public void Cleanup()
        {
            StopPreview();
            _currentCts?.Cancel();

            try
            {
                if (!string.IsNullOrEmpty(_previewFilePath) && File.Exists(_previewFilePath))
                {
                    File.Delete(_previewFilePath);
                }
            }
            catch { }
        }

        private class VideoInfoResult
        {
            public string Title { get; set; }
            public string Channel { get; set; }
            public string Duration { get; set; }
            public string VideoId { get; set; }
        }
    }
}
