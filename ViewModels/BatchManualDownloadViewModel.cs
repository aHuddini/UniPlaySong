using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Downloaders;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.ViewModels
{
    /// <summary>
    /// Individual game item for batch manual download tracking
    /// </summary>
    public class GameDownloadItem : INotifyPropertyChanged
    {
        private BatchDownloadStatus _status = BatchDownloadStatus.Pending;
        private string _statusMessage = "Pending";

        public Game Game { get; set; }
        public string GameName { get; set; }
        public string CoverImagePath { get; set; }

        public BatchDownloadStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsClickable));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusIcon));
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public bool IsCompleted => Status == BatchDownloadStatus.Completed;
        public bool IsClickable => Status != BatchDownloadStatus.Completed;

        public string StatusColor
        {
            get
            {
                switch (Status)
                {
                    case BatchDownloadStatus.Pending: return "#757575";
                    case BatchDownloadStatus.Downloading: return "#2196F3";
                    case BatchDownloadStatus.Completed: return "#4CAF50";
                    case BatchDownloadStatus.Failed: return "#F44336";
                    default: return "#757575";
                }
            }
        }

        public string StatusIcon
        {
            get
            {
                switch (Status)
                {
                    case BatchDownloadStatus.Pending: return "Clock";
                    case BatchDownloadStatus.Downloading: return "Download";
                    case BatchDownloadStatus.Completed: return "Check";
                    case BatchDownloadStatus.Failed: return "Close";
                    default: return "Clock";
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// ViewModel for batch manual download dialog
    /// Allows users to manually download music for multiple failed games
    /// </summary>
    public class BatchManualDownloadViewModel : INotifyPropertyChanged
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "BatchManualDownload";

        private readonly IPlayniteAPI _playniteApi;
        private readonly IDownloadManager _downloadManager;
        private readonly IMusicPlaybackService _playbackService;
        private readonly GameMusicFileService _fileService;
        private readonly ErrorHandlerService _errorHandler;
        private CancellationTokenSource _cancellationTokenSource;

        // State
        public ObservableCollection<GameDownloadItem> Games { get; } = new ObservableCollection<GameDownloadItem>();
        public ObservableCollection<Album> Albums { get; } = new ObservableCollection<Album>();

        private GameDownloadItem _selectedGameItem;
        public GameDownloadItem SelectedGameItem
        {
            get => _selectedGameItem;
            set
            {
                _selectedGameItem = value;
                OnPropertyChanged(nameof(SelectedGameItem));
            }
        }

        // View state
        private bool _isGameListVisible = true;
        public bool IsGameListVisible
        {
            get => _isGameListVisible;
            set
            {
                _isGameListVisible = value;
                OnPropertyChanged(nameof(IsGameListVisible));
                OnPropertyChanged(nameof(HeaderText));
            }
        }

        private bool _isAlbumListVisible = false;
        public bool IsAlbumListVisible
        {
            get => _isAlbumListVisible;
            set
            {
                _isAlbumListVisible = value;
                OnPropertyChanged(nameof(IsAlbumListVisible));
                OnPropertyChanged(nameof(HeaderText));
            }
        }

        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        // Search state
        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                _searchQuery = value;
                OnPropertyChanged(nameof(SearchQuery));
            }
        }

        // Preview state
        private bool _isPreviewPlaying = false;
        public bool IsPreviewPlaying
        {
            get => _isPreviewPlaying;
            set
            {
                _isPreviewPlaying = value;
                OnPropertyChanged(nameof(IsPreviewPlaying));
            }
        }

        private Album _previewingAlbum;
        public Album PreviewingAlbum
        {
            get => _previewingAlbum;
            set
            {
                _previewingAlbum = value;
                OnPropertyChanged(nameof(PreviewingAlbum));
            }
        }

        // Summary tracking
        public int SuccessCount { get; private set; }
        public int FailedCount { get; private set; }
        public int TotalGames => Games.Count;

        /// <summary>
        /// When true (single game mode), auto-close after first successful download
        /// instead of returning to game list view
        /// </summary>
        public bool IsSingleGameMode { get; set; } = false;

        public string HeaderText
        {
            get
            {
                if (IsAlbumListVisible && SelectedGameItem != null)
                    return $"Select Album for: {SelectedGameItem.GameName}";
                return $"Manual Download ({Games.Count} games)";
            }
        }

        // Commands
        public ICommand SelectGameCommand { get; }
        public ICommand SelectAlbumCommand { get; }
        public ICommand PreviewAlbumCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand CompleteCommand { get; }
        public ICommand CancelCommand { get; }

        // Action to close dialog (set by dialog service)
        public Action<bool> CloseDialog { get; set; }

        public BatchManualDownloadViewModel(
            List<Game> failedGames,
            IDownloadManager downloadManager,
            IMusicPlaybackService playbackService,
            GameMusicFileService fileService,
            IPlayniteAPI playniteApi,
            ErrorHandlerService errorHandler)
        {
            _downloadManager = downloadManager ?? throw new ArgumentNullException(nameof(downloadManager));
            _playbackService = playbackService;
            _fileService = fileService;
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _cancellationTokenSource = new CancellationTokenSource();

            // Initialize game items
            foreach (var game in failedGames ?? new List<Game>())
            {
                Games.Add(new GameDownloadItem
                {
                    Game = game,
                    GameName = game.Name,
                    CoverImagePath = GetGameCoverPath(game),
                    Status = BatchDownloadStatus.Pending,
                    StatusMessage = "Click to search"
                });
            }

            // Initialize commands (use fully qualified to avoid ambiguity with Playnite.SDK.RelayCommand)
            SelectGameCommand = new Common.RelayCommand<GameDownloadItem>(OnGameSelected, item => item?.IsClickable == true);
            SelectAlbumCommand = new Common.RelayCommand<Album>(async album => await OnAlbumSelectedAsync(album));
            PreviewAlbumCommand = new Common.RelayCommand<Album>(async album => await PreviewAlbumAsync(album));
            SearchCommand = new Common.RelayCommand(async () => await PerformSearchAsync());
            BackCommand = new Common.RelayCommand(OnBackPressed);
            CompleteCommand = new Common.RelayCommand(() => ShowSummary(false));
            CancelCommand = new Common.RelayCommand(() => ShowSummary(true));
        }

        private string GetGameCoverPath(Game game)
        {
            if (game == null) return null;

            // Try to get cover image path from Playnite
            if (!string.IsNullOrEmpty(game.CoverImage))
            {
                var fullPath = _playniteApi.Database.GetFullFilePath(game.CoverImage);
                if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                    return fullPath;
            }

            return null;
        }

        private void OnGameSelected(GameDownloadItem item)
        {
            if (item == null || !item.IsClickable) return;

            SelectedGameItem = item;
            SearchQuery = item.GameName;

            // Switch to album view and search
            IsGameListVisible = false;
            IsAlbumListVisible = true;

            // Trigger search
            _ = PerformSearchAsync();
        }

        private async Task PerformSearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                StatusMessage = "Please enter a search term";
                return;
            }

            IsLoading = true;
            StatusMessage = $"Searching for '{SearchQuery}'...";
            Albums.Clear();

            try
            {
                await Task.Run(() =>
                {
                    var albums = _downloadManager.GetAlbumsForGame(
                        SearchQuery,
                        Source.All,
                        _cancellationTokenSource.Token,
                        auto: false,
                        skipCache: false);

                    var albumList = albums?.ToList() ?? new List<Album>();

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        foreach (var album in albumList)
                        {
                            Albums.Add(album);
                        }

                        StatusMessage = albumList.Count > 0
                            ? $"Found {albumList.Count} albums"
                            : "No albums found. Try different search terms.";
                    });
                });
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Search cancelled";
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(ex, "searching for albums", showUserMessage: false);
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task OnAlbumSelectedAsync(Album album)
        {
            if (album == null || SelectedGameItem == null) return;

            IsLoading = true;
            StatusMessage = $"Downloading from {album.Name}...";

            try
            {
                await Task.Run(() =>
                {
                    // Get songs from album
                    var songs = _downloadManager.GetSongsFromAlbum(album, _cancellationTokenSource.Token);
                    var songsList = songs?.ToList() ?? new List<Song>();

                    if (songsList.Count == 0)
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            StatusMessage = "No songs found in this album";
                            SelectedGameItem.Status = BatchDownloadStatus.Failed;
                            SelectedGameItem.StatusMessage = "No songs found";
                            FailedCount++;
                        });
                        return;
                    }

                    // Use BestSongPick to find best track
                    var bestSongs = _downloadManager.BestSongPick(songsList, SelectedGameItem.Game.Name);
                    var bestSong = bestSongs?.FirstOrDefault();

                    if (bestSong == null)
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            StatusMessage = "Could not pick best song";
                            SelectedGameItem.Status = BatchDownloadStatus.Failed;
                            SelectedGameItem.StatusMessage = "No suitable song found";
                            FailedCount++;
                        });
                        return;
                    }

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        StatusMessage = $"Downloading: {bestSong.Name}...";
                    });

                    // Get download path
                    string downloadPath = null;
                    if (_fileService != null)
                    {
                        var musicDir = _fileService.GetGameMusicDirectory(SelectedGameItem.Game);
                        if (!Directory.Exists(musicDir))
                            Directory.CreateDirectory(musicDir);

                        var fileName = SanitizeFileName(bestSong.Name);
                        // Song.Id contains the URL/path, get extension from there
                        var extension = !string.IsNullOrEmpty(bestSong.Id) ? Path.GetExtension(bestSong.Id) : ".mp3";
                        if (string.IsNullOrEmpty(extension)) extension = ".mp3";
                        downloadPath = Path.Combine(musicDir, fileName + extension);
                    }

                    // Download the song
                    var success = _downloadManager.DownloadSong(bestSong, downloadPath, _cancellationTokenSource.Token);

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (success)
                        {
                            SelectedGameItem.Status = BatchDownloadStatus.Completed;
                            SelectedGameItem.StatusMessage = $"Downloaded: {bestSong.Name}";
                            SuccessCount++;
                            Logger.Info($"[{LogPrefix}] Downloaded music for {SelectedGameItem.GameName}: {bestSong.Name}");
                        }
                        else
                        {
                            SelectedGameItem.Status = BatchDownloadStatus.Failed;
                            SelectedGameItem.StatusMessage = "Download failed";
                            FailedCount++;
                            Logger.Warn($"[{LogPrefix}] Failed to download music for {SelectedGameItem.GameName}");
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(ex, "downloading music", showUserMessage: false);
                SelectedGameItem.Status = BatchDownloadStatus.Failed;
                SelectedGameItem.StatusMessage = $"Error: {ex.Message}";
                FailedCount++;
            }
            finally
            {
                IsLoading = false;

                // Stop any preview
                StopPreview();
            }

            // Handle post-download navigation (must be outside finally block)
            // In single game mode, auto-close after successful download
            if (IsSingleGameMode && SuccessCount > 0)
            {
                Logger.Info($"[{LogPrefix}] Single game mode - auto-closing after successful download");
                CloseDialog?.Invoke(true);
                return;
            }

            // Return to game list
            IsAlbumListVisible = false;
            IsGameListVisible = true;
            SelectedGameItem = null;
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "track";

            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName.Where(c => !invalid.Contains(c)).ToArray());

            // Limit length
            if (sanitized.Length > 100)
                sanitized = sanitized.Substring(0, 100);

            return string.IsNullOrWhiteSpace(sanitized) ? "track" : sanitized;
        }

        private async Task PreviewAlbumAsync(Album album)
        {
            if (album == null) return;

            // Stop current preview if any
            StopPreview();

            if (_playbackService == null) return;

            try
            {
                IsPreviewPlaying = true;
                PreviewingAlbum = album;

                await Task.Run(() =>
                {
                    // Get songs from album
                    var songs = _downloadManager.GetSongsFromAlbum(album, _cancellationTokenSource.Token);
                    var songsList = songs?.ToList() ?? new List<Song>();

                    if (songsList.Count == 0) return;

                    // Use BestSongPick to find best track for preview
                    var bestSongs = _downloadManager.BestSongPick(songsList, SearchQuery);
                    var bestSong = bestSongs?.FirstOrDefault();

                    if (bestSong == null) return;

                    // Download preview to temp
                    var tempPath = Path.Combine(Path.GetTempPath(), "UniPlaySong", "Preview");
                    if (!Directory.Exists(tempPath))
                        Directory.CreateDirectory(tempPath);

                    // Song.Id contains the URL/path
                    var songExt = !string.IsNullOrEmpty(bestSong.Id) ? Path.GetExtension(bestSong.Id) : ".mp3";
                    if (string.IsNullOrEmpty(songExt)) songExt = ".mp3";
                    var previewFile = Path.Combine(tempPath, $"preview_{album.Id?.GetHashCode() ?? 0}{songExt}");

                    // Download if not cached
                    if (!File.Exists(previewFile))
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            StatusMessage = $"Loading preview: {bestSong.Name}...";
                        });

                        var success = _downloadManager.DownloadSong(bestSong, previewFile, _cancellationTokenSource.Token, isPreview: true);
                        if (!success)
                        {
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                StatusMessage = "Preview download failed";
                                IsPreviewPlaying = false;
                                PreviewingAlbum = null;
                            });
                            return;
                        }
                    }

                    // Play preview
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (File.Exists(previewFile))
                        {
                            // Use Manual pause source to pause any game music
                            _playbackService?.AddPauseSource(PauseSource.Manual);
                            // PlayPreview requires a volume parameter (0.0 - 1.0)
                            _playbackService?.PlayPreview(previewFile, 0.7);
                            StatusMessage = $"Playing: {bestSong.Name}";
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(ex, "playing preview", showUserMessage: false);
                IsPreviewPlaying = false;
                PreviewingAlbum = null;
            }
        }

        private void StopPreview()
        {
            if (_playbackService != null && IsPreviewPlaying)
            {
                _playbackService.Stop();
                _playbackService.RemovePauseSource(PauseSource.Manual);
            }
            IsPreviewPlaying = false;
            PreviewingAlbum = null;
        }

        private void OnBackPressed()
        {
            // Stop any preview
            StopPreview();

            // Return to game list
            Albums.Clear();
            IsAlbumListVisible = false;
            IsGameListVisible = true;
            SelectedGameItem = null;
            StatusMessage = string.Empty;
        }

        private void ShowSummary(bool wasCancelled)
        {
            // Stop any preview
            StopPreview();

            // Cancel any pending operations
            _cancellationTokenSource?.Cancel();

            var remaining = Games.Count(g => g.Status == BatchDownloadStatus.Pending);

            var message = wasCancelled
                ? $"Download cancelled.\n\nCompleted: {SuccessCount}\nRemaining: {remaining}"
                : $"Manual download session complete.\n\nSuccessful: {SuccessCount}\nFailed: {FailedCount}\nSkipped: {remaining}";

            _playniteApi.Dialogs.ShowMessage(message, "Download Summary");

            // Close dialog
            CloseDialog?.Invoke(SuccessCount > 0);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
