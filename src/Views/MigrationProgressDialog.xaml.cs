using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using UniPlaySong.Services;

namespace UniPlaySong.Views
{
    /// <summary>
    /// Progress dialog for music migration operations
    /// </summary>
    public partial class MigrationProgressDialog : UserControl
    {
        private CancellationTokenSource _cancellationTokenSource;

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

        private ObservableCollection<string> _statusMessages = new ObservableCollection<string>();

        public MigrationProgressDialog()
        {
            InitializeComponent();
            _cancellationTokenSource = new CancellationTokenSource();
            StatusMessages.ItemsSource = _statusMessages;
        }

        /// <summary>
        /// Sets the dialog title
        /// </summary>
        public void SetTitle(string title)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetTitle(title)));
                return;
            }

            TitleText.Text = title;
        }

        /// <summary>
        /// Report progress from migration service
        /// </summary>
        public void ReportProgress(MigrationProgress progress)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ReportProgress(progress)));
                return;
            }

            try
            {
                // Update current game
                if (!string.IsNullOrEmpty(progress.CurrentGameName))
                {
                    CurrentGameText.Text = $"Game: {progress.CurrentGameName}";
                }
                else if (!string.IsNullOrEmpty(progress.CurrentGameId))
                {
                    CurrentGameText.Text = $"Game ID: {progress.CurrentGameId}";
                }

                // Update status
                if (!string.IsNullOrEmpty(progress.Status))
                {
                    StatusText.Text = progress.Status;
                }

                // Update progress bar
                if (progress.TotalCount > 0)
                {
                    ProgressBar.Value = progress.ProgressPercentage;
                    ProgressText.Text = $"{progress.CurrentIndex} / {progress.TotalCount} games";
                }

                // Add status message
                if (!string.IsNullOrEmpty(progress.Status) &&
                    !progress.Status.Contains("Preparing") &&
                    !progress.Status.Contains("Initializing"))
                {
                    var message = $"[{DateTime.Now:HH:mm:ss}] {progress.Status}";
                    _statusMessages.Add(message);

                    // Keep only last 50 messages
                    while (_statusMessages.Count > 50)
                    {
                        _statusMessages.RemoveAt(0);
                    }
                }

                // Scroll to bottom
                if (StatusMessages.Items.Count > 0)
                {
                    var scrollViewer = FindVisualChild<ScrollViewer>(StatusMessages);
                    scrollViewer?.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating progress UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Report final results from batch migration
        /// </summary>
        public void ReportCompletion(MigrationBatchResult result)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ReportCompletion(result)));
                return;
            }

            try
            {
                // Update statistics
                CopiedCountText.Text = result.TotalFilesCopied.ToString();
                SkippedCountText.Text = result.TotalFilesSkipped.ToString();
                FailedCountText.Text = result.FailedGames.ToString();

                // Update progress to 100%
                ProgressBar.Value = 100;
                ProgressText.Text = $"{result.TotalGames} / {result.TotalGames} games";

                // Update status
                if (result.WasCancelled)
                {
                    StatusText.Text = "Migration cancelled by user.";
                    CurrentGameText.Text = "Operation cancelled";
                }
                else
                {
                    StatusText.Text = $"Migration complete! {result.TotalFilesCopied} files copied.";
                    CurrentGameText.Text = "Migration finished";
                }

                // Add completion message
                var summaryMessage = result.WasCancelled
                    ? $"[{DateTime.Now:HH:mm:ss}] Migration cancelled. {result.SuccessfulGames}/{result.TotalGames} games processed."
                    : $"[{DateTime.Now:HH:mm:ss}] Migration complete. {result.SuccessfulGames} games migrated, {result.TotalFilesCopied} files copied, {result.TotalFilesSkipped} skipped.";

                _statusMessages.Add(summaryMessage);

                // Change button to Close
                CancelButton.Content = "Close";
                CancelButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reporting completion: {ex.Message}");
            }
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
                StatusText.Text = "Cancellation requested...";
            }
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                {
                    return result;
                }
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }
    }
}
