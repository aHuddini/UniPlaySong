using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using UniPlaySong.Models;

namespace UniPlaySong.Views
{
    /// <summary>
    /// Progress dialog for audio normalization operations
    /// </summary>
    public partial class NormalizationProgressDialog : UserControl
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

        public NormalizationProgressDialog()
        {
            InitializeComponent();
            _cancellationTokenSource = new CancellationTokenSource();
            StatusMessages.ItemsSource = _statusMessages;
        }

        /// <summary>
        /// Report progress from normalization service
        /// </summary>
        public void ReportProgress(NormalizationProgress progress)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ReportProgress(progress)));
                return;
            }

            try
            {
                // Update current file
                if (!string.IsNullOrEmpty(progress.CurrentFile))
                {
                    CurrentFileText.Text = $"Current File: {progress.CurrentFile}";
                }

                // Update status
                if (!string.IsNullOrEmpty(progress.Status))
                {
                    StatusText.Text = progress.Status;
                }

                // Update progress bar
                if (progress.TotalFiles > 0)
                {
                    var percent = (double)progress.CurrentIndex / progress.TotalFiles * 100;
                    ProgressBar.Value = percent;
                    ProgressText.Text = $"{progress.CurrentIndex} / {progress.TotalFiles} files";
                }

                // Add status message
                if (!string.IsNullOrEmpty(progress.Status) && !progress.Status.Contains("Preparing") && !progress.Status.Contains("Initializing"))
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

                // Update cancel button if complete
                if (progress.IsComplete)
                {
                    CancelButton.Content = "Close";
                }
            }
            catch (Exception ex)
            {
                // Silently handle errors in UI updates
                System.Diagnostics.Debug.WriteLine($"Error updating progress UI: {ex.Message}");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (CancelButton.Content.ToString() == "Close")
            {
                // Dialog is complete, close it
                var window = Window.GetWindow(this);
                window?.Close();
            }
            else
            {
                // Cancel operation
                _cancellationTokenSource?.Cancel();
                CancelButton.IsEnabled = false;
                CancelButton.Content = "Cancelling...";
                StatusText.Text = "Cancellation requested...";
            }
        }

        /// <summary>
        /// Find visual child of specified type
        /// </summary>
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