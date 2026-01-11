using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UniPlaySong.Models;

namespace UniPlaySong.Views
{
    /// <summary>
    /// Progress dialog for batch download operations with parallel support
    /// </summary>
    public partial class BatchDownloadProgressDialog : UserControl
    {
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
        /// Initialize the dialog with a list of game names
        /// </summary>
        public void Initialize(IEnumerable<string> gameNames)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Initialize(gameNames)));
                return;
            }

            // Reset counters
            _completedCount = 0;
            _failedCount = 0;
            _skippedCount = 0;
            _cancelledCount = 0;
            _downloadingCount = 0;

            _downloadItems.Clear();
            foreach (var name in gameNames)
            {
                _downloadItems.Add(new BatchDownloadItem
                {
                    GameName = name,
                    Status = BatchDownloadStatus.Pending,
                    StatusMessage = "Waiting..."
                });
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
            lock (_lockObject)
            {
                var item = _downloadItems.FirstOrDefault(i => i.GameName == gameName);
                if (item != null)
                {
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

                // Update button if all done
                if (finished >= total && total > 0)
                {
                    CancelButton.Content = "Close";
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
            if (CancelButton.Content.ToString() == "Close")
            {
                var window = Window.GetWindow(this);
                window?.Close();
            }
            else
            {
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
    }
}
