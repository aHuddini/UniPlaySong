using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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

            UpdateOverallProgress();
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

            lock (_lockObject)
            {
                var item = _downloadItems.FirstOrDefault(i => i.GameName == gameName);
                if (item != null)
                {
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

            UpdateOverallProgress();
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

            lock (_lockObject)
            {
                if (index >= 0 && index < _downloadItems.Count)
                {
                    var item = _downloadItems[index];
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

            UpdateOverallProgress();
        }

        /// <summary>
        /// Update overall progress display
        /// </summary>
        private void UpdateOverallProgress()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(UpdateOverallProgress));
                return;
            }

            try
            {
                int total = _downloadItems.Count;
                int completed = _downloadItems.Count(i => i.Status == BatchDownloadStatus.Completed);
                int failed = _downloadItems.Count(i => i.Status == BatchDownloadStatus.Failed);
                int skipped = _downloadItems.Count(i => i.Status == BatchDownloadStatus.Skipped);
                int inProgress = _downloadItems.Count(i => i.Status == BatchDownloadStatus.Downloading);
                int cancelled = _downloadItems.Count(i => i.Status == BatchDownloadStatus.Cancelled);

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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating progress: {ex.Message}");
            }
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
                        item.Status = BatchDownloadStatus.Cancelled;
                        item.StatusMessage = "Cancelled";
                    }
                }
            }

            UpdateOverallProgress();
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
