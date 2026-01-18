using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Models;

namespace UniPlaySong.Views
{
    /// <summary>
    /// Progress dialog for batch download operations with parallel support
    /// </summary>
    public partial class BatchDownloadProgressDialog : UserControl
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private CancellationTokenSource _cancellationTokenSource;
        private ObservableCollection<BatchDownloadItem> _downloadItems;
        private readonly object _lockObject = new object();

        // Cached counters to avoid expensive LINQ operations on every update
        private int _completedCount = 0;
        private int _failedCount = 0;
        private int _skippedCount = 0;
        private int _cancelledCount = 0;
        private int _downloadingCount = 0;

        // Throttle UI updates to prevent flooding the dispatcher
        private DateTime _lastUIUpdate = DateTime.MinValue;
        private const int UIUpdateThrottleMs = 100; // Update UI at most every 100ms
        private bool _uiUpdatePending = false;
        private DispatcherTimer _throttleTimer;

        // Review mode state
        private bool _isReviewMode = false;
        private IPlayniteAPI _playniteApi;

        /// <summary>
        /// Event fired when user clicks a completed game to re-download (during review mode)
        /// </summary>
        public event Action<BatchDownloadItem> OnGameRedownloadRequested;

        /// <summary>
        /// Event fired when user clicks the pause/play music button
        /// </summary>
        public event Action OnMusicPausePlayRequested;

        /// <summary>
        /// Event fired when user clicks the Auto-Add More Songs button
        /// Parameters: number of songs to add (1-3)
        /// </summary>
        public event Action<int> OnAutoAddSongsRequested;

        // Music playback state
        private bool _isMusicPaused = false;

        /// <summary>
        /// Whether the dialog is in review mode (downloads complete, user reviewing results)
        /// </summary>
        public bool IsReviewMode
        {
            get => _isReviewMode;
            private set
            {
                _isReviewMode = value;
                if (value)
                {
                    ReviewButton.Visibility = Visibility.Collapsed;
                    ReviewModeHint.Visibility = Visibility.Visible;
                    ReviewModeBadge.Visibility = Visibility.Visible;
                    AutoAddSongsButton.Visibility = Visibility.Visible;
                    CancelButton.Content = "Finish";
                    CancelButton.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2E7D32"));
                }
                else
                {
                    ReviewModeHint.Visibility = Visibility.Collapsed;
                    ReviewModeBadge.Visibility = Visibility.Collapsed;
                    AutoAddSongsButton.Visibility = Visibility.Collapsed;
                }
            }
        }

        public CancellationToken CancellationToken
        {
            get
            {
                if (_cancellationTokenSource == null)
                {
                    _cancellationTokenSource = new CancellationTokenSource();
                }
                return _cancellationTokenSource.Token;
            }
        }

        public ObservableCollection<BatchDownloadItem> DownloadItems => _downloadItems;

        public BatchDownloadProgressDialog()
        {
            InitializeComponent();
            _cancellationTokenSource = new CancellationTokenSource();
            _downloadItems = new ObservableCollection<BatchDownloadItem>();
            GamesList.ItemsSource = _downloadItems;

            // Setup throttle timer for batched UI updates
            _throttleTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(UIUpdateThrottleMs)
            };
            _throttleTimer.Tick += (s, e) =>
            {
                _throttleTimer.Stop();
                if (_uiUpdatePending)
                {
                    _uiUpdatePending = false;
                    UpdateOverallProgressInternal();
                }
            };
        }

        /// <summary>
        /// Initialize the dialog with a list of game names (legacy method)
        /// </summary>
        public void Initialize(IEnumerable<string> gameNames)
        {
            // Convert to game names list and call the new method
            var names = gameNames?.ToList() ?? new List<string>();
            InitializeInternal(names.Select(n => new BatchDownloadItem
            {
                GameName = n,
                Status = BatchDownloadStatus.Pending,
                StatusMessage = "Waiting..."
            }));
        }

        /// <summary>
        /// Initialize the dialog with a list of Game objects (includes library info)
        /// </summary>
        public void Initialize(IEnumerable<Game> games, IPlayniteAPI playniteApi)
        {
            _playniteApi = playniteApi;

            var items = new List<BatchDownloadItem>();
            foreach (var game in games ?? Enumerable.Empty<Game>())
            {
                var libraryName = GetLibraryName(game, playniteApi);
                items.Add(new BatchDownloadItem
                {
                    Game = game,
                    GameName = game.Name,
                    LibraryName = libraryName,
                    Status = BatchDownloadStatus.Pending,
                    StatusMessage = "Waiting..."
                });
            }

            InitializeInternal(items);
        }

        /// <summary>
        /// Gets the library name for a game
        /// </summary>
        private string GetLibraryName(Game game, IPlayniteAPI playniteApi)
        {
            if (game == null || playniteApi == null)
                return null;

            try
            {
                // Try to get the library plugin info
                if (game.PluginId != Guid.Empty)
                {
                    var library = playniteApi.Addons?.Plugins?
                        .FirstOrDefault(p => p.Id == game.PluginId);
                    if (library != null)
                    {
                        // Extract just the library name (e.g., "GOG" from "GOG Library")
                        var name = library.GetType().Name
                            .Replace("Library", "")
                            .Replace("Plugin", "")
                            .Trim();
                        if (!string.IsNullOrEmpty(name))
                            return name;
                    }
                }

                // Fallback to source name if available
                if (game.Source != null && !string.IsNullOrEmpty(game.Source.Name))
                {
                    return game.Source.Name;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[BatchDownloadProgressDialog] Error getting library name: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Internal initialization method
        /// </summary>
        private void InitializeInternal(IEnumerable<BatchDownloadItem> items)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => InitializeInternal(items)));
                return;
            }

            // Reset counters
            _completedCount = 0;
            _failedCount = 0;
            _skippedCount = 0;
            _cancelledCount = 0;
            _downloadingCount = 0;
            _isReviewMode = false;

            // Reset UI
            ReviewButton.Visibility = Visibility.Collapsed;

            _downloadItems.Clear();
            foreach (var item in items)
            {
                _downloadItems.Add(item);
            }

            UpdateOverallProgressInternal(); // Force immediate update on init
        }

        /// <summary>
        /// Update the status of a specific game by name
        /// </summary>
        public void UpdateGameStatus(string gameName, BatchDownloadStatus status, string message = null, string albumName = null, string sourceName = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateGameStatus(gameName, status, message, albumName, sourceName)));
                return;
            }

            BatchDownloadStatus? oldStatus = null;
            bool itemFound = false;
            lock (_lockObject)
            {
                // Use case-insensitive comparison to handle any name variations
                var item = _downloadItems.FirstOrDefault(i =>
                    string.Equals(i.GameName, gameName, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    itemFound = true;
                    oldStatus = item.Status;
                    item.Status = status;
                    if (message != null)
                    {
                        item.StatusMessage = message;
                    }
                    if (albumName != null)
                    {
                        item.AlbumName = albumName;
                    }
                    if (sourceName != null)
                    {
                        item.SourceName = sourceName;
                    }
                }
            }

            if (!itemFound)
            {
                Logger.Warn($"[BatchDownloadProgressDialog] Game not found in list: '{gameName}'");
            }

            // Update counters incrementally (much faster than recounting)
            if (oldStatus.HasValue && oldStatus.Value != status)
            {
                UpdateCounters(oldStatus.Value, status);
            }

            ScheduleUIUpdate();
        }

        /// <summary>
        /// Update the status of a game by index
        /// </summary>
        public void UpdateGameStatusByIndex(int index, BatchDownloadStatus status, string message = null, string albumName = null, string sourceName = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateGameStatusByIndex(index, status, message, albumName, sourceName)));
                return;
            }

            BatchDownloadStatus? oldStatus = null;
            lock (_lockObject)
            {
                if (index >= 0 && index < _downloadItems.Count)
                {
                    var item = _downloadItems[index];
                    oldStatus = item.Status;
                    item.Status = status;
                    if (message != null)
                    {
                        item.StatusMessage = message;
                    }
                    if (albumName != null)
                    {
                        item.AlbumName = albumName;
                    }
                    if (sourceName != null)
                    {
                        item.SourceName = sourceName;
                    }
                }
            }

            // Update counters incrementally
            if (oldStatus.HasValue && oldStatus.Value != status)
            {
                UpdateCounters(oldStatus.Value, status);
            }

            ScheduleUIUpdate();
        }

        /// <summary>
        /// Update the status of a game by Game ID (handles duplicate game names correctly)
        /// </summary>
        public void UpdateGameStatusByGame(Game game, BatchDownloadStatus status, string message = null, string albumName = null, string sourceName = null)
        {
            if (game == null)
            {
                Logger.Warn("[BatchDownloadProgressDialog] UpdateGameStatusByGame called with null game");
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateGameStatusByGame(game, status, message, albumName, sourceName)));
                return;
            }

            BatchDownloadStatus? oldStatus = null;
            bool itemFound = false;
            lock (_lockObject)
            {
                // Match by Game ID - this handles duplicate game names correctly
                var item = _downloadItems.FirstOrDefault(i =>
                    i.Game != null && i.Game.Id == game.Id);

                if (item != null)
                {
                    itemFound = true;
                    oldStatus = item.Status;
                    item.Status = status;
                    if (message != null)
                    {
                        item.StatusMessage = message;
                    }
                    if (albumName != null)
                    {
                        item.AlbumName = albumName;
                    }
                    if (sourceName != null)
                    {
                        item.SourceName = sourceName;
                    }
                }
            }

            if (!itemFound)
            {
                Logger.Warn($"[BatchDownloadProgressDialog] Game not found in list by ID: '{game.Name}' (ID: {game.Id})");
            }

            // Update counters incrementally
            if (oldStatus.HasValue && oldStatus.Value != status)
            {
                UpdateCounters(oldStatus.Value, status);
            }

            ScheduleUIUpdate();
        }

        /// <summary>
        /// Update counters when a status changes (incremental, O(1) instead of O(n))
        /// </summary>
        private void UpdateCounters(BatchDownloadStatus oldStatus, BatchDownloadStatus newStatus)
        {
            // Decrement old status counter
            switch (oldStatus)
            {
                case BatchDownloadStatus.Completed: Interlocked.Decrement(ref _completedCount); break;
                case BatchDownloadStatus.Failed: Interlocked.Decrement(ref _failedCount); break;
                case BatchDownloadStatus.Skipped: Interlocked.Decrement(ref _skippedCount); break;
                case BatchDownloadStatus.Cancelled: Interlocked.Decrement(ref _cancelledCount); break;
                case BatchDownloadStatus.Downloading: Interlocked.Decrement(ref _downloadingCount); break;
            }

            // Increment new status counter
            switch (newStatus)
            {
                case BatchDownloadStatus.Completed: Interlocked.Increment(ref _completedCount); break;
                case BatchDownloadStatus.Failed: Interlocked.Increment(ref _failedCount); break;
                case BatchDownloadStatus.Skipped: Interlocked.Increment(ref _skippedCount); break;
                case BatchDownloadStatus.Cancelled: Interlocked.Increment(ref _cancelledCount); break;
                case BatchDownloadStatus.Downloading: Interlocked.Increment(ref _downloadingCount); break;
            }
        }

        /// <summary>
        /// Schedule a throttled UI update to prevent flooding the dispatcher
        /// </summary>
        private void ScheduleUIUpdate()
        {
            if (!_uiUpdatePending)
            {
                _uiUpdatePending = true;
                if (!_throttleTimer.IsEnabled)
                {
                    _throttleTimer.Start();
                }
            }
        }

        /// <summary>
        /// Update overall progress display (internal, called from timer)
        /// </summary>
        private void UpdateOverallProgressInternal()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(UpdateOverallProgressInternal));
                return;
            }

            try
            {
                int total = _downloadItems.Count;
                int completed = _completedCount;
                int failed = _failedCount;
                int skipped = _skippedCount;
                int cancelled = _cancelledCount;

                int finished = completed + failed + skipped + cancelled;

                // Update progress bar
                if (total > 0)
                {
                    OverallProgressBar.Value = (double)finished / total * 100;
                    OverallProgressText.Text = $"{finished} / {total} games";
                }

                // Update statistics
                DownloadedCountText.Text = completed.ToString();
                SkippedCountText.Text = skipped.ToString();
                FailedCountText.Text = failed.ToString();

                // Update buttons if all done
                if (finished >= total && total > 0 && !_isReviewMode)
                {
                    CancelButton.Content = "Close";

                    // Show Review button if there were successful downloads OR skipped games
                    // (user might want to correct downloads or replace skipped games)
                    if (completed > 0 || skipped > 0)
                    {
                        ReviewButton.Visibility = Visibility.Visible;
                    }
                }

                _lastUIUpdate = DateTime.Now;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating progress: {ex.Message}");
            }
        }

        /// <summary>
        /// Legacy method for compatibility - schedules throttled update
        /// </summary>
        private void UpdateOverallProgress()
        {
            ScheduleUIUpdate();
        }

        /// <summary>
        /// Force an immediate UI update - call this after all downloads complete
        /// to ensure the Close/Review buttons appear without waiting for throttle timer
        /// </summary>
        public void ForceUIUpdate()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ForceUIUpdate));
                return;
            }

            // Stop any pending throttled update
            _throttleTimer.Stop();
            _uiUpdatePending = false;

            // Force immediate update
            UpdateOverallProgressInternal();
        }

        /// <summary>
        /// Mark all pending/in-progress items as cancelled
        /// </summary>
        public void CancelAllPending()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(CancelAllPending));
                return;
            }

            lock (_lockObject)
            {
                foreach (var item in _downloadItems)
                {
                    if (item.Status == BatchDownloadStatus.Pending ||
                        item.Status == BatchDownloadStatus.Downloading)
                    {
                        var oldStatus = item.Status;
                        item.Status = BatchDownloadStatus.Cancelled;
                        item.StatusMessage = "Cancelled";
                        UpdateCounters(oldStatus, BatchDownloadStatus.Cancelled);
                    }
                }
            }

            UpdateOverallProgressInternal(); // Force immediate update on cancel
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            var buttonContent = CancelButton.Content.ToString();

            // If downloads are complete (Close, Done, or Finish), close immediately
            if (buttonContent == "Close" || buttonContent == "Done" || buttonContent == "Finish")
            {
                var window = Window.GetWindow(this);
                window?.Close();
            }
            else
            {
                // Downloads still in progress - cancel them
                _cancellationTokenSource?.Cancel();
                CancelButton.IsEnabled = false;
                CancelButton.Content = "Cancelling...";
                CancelAllPending();
            }
        }

        /// <summary>
        /// Get current progress summary
        /// </summary>
        public BatchDownloadProgress GetProgress()
        {
            lock (_lockObject)
            {
                return new BatchDownloadProgress
                {
                    TotalGames = _downloadItems.Count,
                    CompletedCount = _downloadItems.Count(i => i.Status == BatchDownloadStatus.Completed),
                    FailedCount = _downloadItems.Count(i => i.Status == BatchDownloadStatus.Failed),
                    SkippedCount = _downloadItems.Count(i => i.Status == BatchDownloadStatus.Skipped),
                    InProgressCount = _downloadItems.Count(i => i.Status == BatchDownloadStatus.Downloading),
                    IsComplete = _downloadItems.All(i =>
                        i.Status != BatchDownloadStatus.Pending &&
                        i.Status != BatchDownloadStatus.Downloading)
                };
            }
        }

        /// <summary>
        /// Check if there's any work remaining
        /// </summary>
        public bool HasPendingWork()
        {
            lock (_lockObject)
            {
                return _downloadItems.Any(i =>
                    i.Status == BatchDownloadStatus.Pending ||
                    i.Status == BatchDownloadStatus.Downloading);
            }
        }

        /// <summary>
        /// Handle Review button click - enter review mode
        /// </summary>
        private void ReviewButton_Click(object sender, RoutedEventArgs e)
        {
            IsReviewMode = true;
            Logger.Info("[BatchDownloadProgressDialog] Entered review mode");
        }

        /// <summary>
        /// Handle click on a game item - in review mode, allow re-download for completed or skipped items
        /// </summary>
        private void GameItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_isReviewMode)
                return;

            // Find the BatchDownloadItem from the clicked element
            var element = sender as FrameworkElement;
            if (element?.DataContext is BatchDownloadItem item)
            {
                // Allow re-download for completed items (user wants to correct a wrong download)
                // or skipped items (user wants to download/replace music for games that were skipped)
                if ((item.Status == BatchDownloadStatus.Completed || item.Status == BatchDownloadStatus.Skipped) && item.Game != null)
                {
                    Logger.Info($"[BatchDownloadProgressDialog] Re-download requested for: {item.GameName} (status: {item.Status})");
                    OnGameRedownloadRequested?.Invoke(item);
                }
            }
        }

        /// <summary>
        /// Update a specific item after re-download completes
        /// </summary>
        public void UpdateItemAfterRedownload(BatchDownloadItem item, string newAlbumName, string newSourceName)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateItemAfterRedownload(item, newAlbumName, newSourceName)));
                return;
            }

            if (item != null)
            {
                item.AlbumName = newAlbumName;
                item.SourceName = newSourceName;
                item.StatusMessage = $"Re-downloaded ({newSourceName})";
                item.WasRedownloaded = true;  // Mark as re-downloaded for orange bold styling
                Logger.Info($"[BatchDownloadProgressDialog] Updated item after re-download: {item.GameName} -> {newAlbumName}");
            }
        }

        /// <summary>
        /// Gets all completed items (for review processing)
        /// </summary>
        public IEnumerable<BatchDownloadItem> GetCompletedItems()
        {
            lock (_lockObject)
            {
                return _downloadItems.Where(i => i.Status == BatchDownloadStatus.Completed).ToList();
            }
        }

        /// <summary>
        /// Handle music pause/play button click
        /// </summary>
        private void MusicPausePlayButton_Click(object sender, RoutedEventArgs e)
        {
            _isMusicPaused = !_isMusicPaused;
            UpdateMusicButtonState();
            OnMusicPausePlayRequested?.Invoke();
        }

        /// <summary>
        /// Update the music button icon and label based on pause state
        /// </summary>
        private void UpdateMusicButtonState()
        {
            if (_isMusicPaused)
            {
                MusicPausePlayIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Play;
                MusicControlLabel.Text = "Resume Music";
            }
            else
            {
                MusicPausePlayIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Pause;
                MusicControlLabel.Text = "Pause Music";
            }
        }

        /// <summary>
        /// Set the music pause state from external source (e.g., when playback service state changes)
        /// </summary>
        public void SetMusicPauseState(bool isPaused)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetMusicPauseState(isPaused)));
                return;
            }

            _isMusicPaused = isPaused;
            UpdateMusicButtonState();
        }

        /// <summary>
        /// Mark a game as having songs added via Auto-Add (for purple bold styling)
        /// </summary>
        public void MarkGameAsSongsAdded(Game game)
        {
            if (game == null) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => MarkGameAsSongsAdded(game)));
                return;
            }

            lock (_lockObject)
            {
                var item = _downloadItems.FirstOrDefault(i => i.Game != null && i.Game.Id == game.Id);
                if (item != null)
                {
                    item.HadSongsAdded = true;
                }
            }
        }

        /// <summary>
        /// Hide the Auto-Add More Songs button (call after auto-add completes)
        /// </summary>
        public void HideAutoAddButton()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(HideAutoAddButton));
                return;
            }

            AutoAddSongsButton.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handle Auto-Add More Songs button click - shows song count selection dialog
        /// </summary>
        private void AutoAddSongsButton_Click(object sender, RoutedEventArgs e)
        {
            // Use simple message box with options for cleaner UX
            var options = new List<MessageBoxOption>
            {
                new MessageBoxOption("1 song", false, false),
                new MessageBoxOption("2 songs", true, false),  // Default
                new MessageBoxOption("3 songs (max)", false, false),
                new MessageBoxOption("Cancel", false, true)
            };

            var selected = _playniteApi?.Dialogs?.ShowMessage(
                "How many additional songs per game?\n\n(Max 3 to prevent API rate limiting)",
                "Auto-Add More Songs",
                System.Windows.MessageBoxImage.Question,
                options);

            if (selected != null && selected.Title != "Cancel")
            {
                int songCount = 2; // Default
                if (selected.Title.StartsWith("1")) songCount = 1;
                else if (selected.Title.StartsWith("3")) songCount = 3;

                Logger.Info($"[BatchDownloadProgressDialog] Auto-Add More Songs requested: {songCount} songs per game");
                OnAutoAddSongsRequested?.Invoke(songCount);
            }
        }
    }
}
