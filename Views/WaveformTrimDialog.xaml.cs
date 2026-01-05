using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Models.WaveformTrim;
using UniPlaySong.Services;

namespace UniPlaySong.Views
{
    public partial class WaveformTrimDialog : UserControl
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "PreciseTrim:Desktop";

        private IPlayniteAPI _playniteApi;
        private IWaveformTrimService _waveformService;
        private IMusicPlaybackService _playbackService;
        private GameMusicFileService _fileService;
        private Game _game;
        private Func<UniPlaySongSettings> _settingsProvider;

        private WaveformData _waveformData;
        private TrimWindow _trimWindow;
        private CancellationTokenSource _loadCts;
        private DispatcherTimer _previewTimer;
        private bool _isPreviewing;

        // Drag state
        private enum DragMode { None, StartMarker, EndMarker, Window }
        private DragMode _dragMode = DragMode.None;
        private Point _dragStartPoint;
        private double _dragStartValue;
        private double _dragEndValue;

        public WaveformTrimDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize the dialog for a specific game
        /// </summary>
        public void Initialize(
            Game game,
            IPlayniteAPI playniteApi,
            IWaveformTrimService waveformService,
            IMusicPlaybackService playbackService,
            GameMusicFileService fileService,
            Func<UniPlaySongSettings> settingsProvider)
        {
            Logger.DebugIf(LogPrefix,$"Initialize called for game: {game?.Name}");
            _game = game;
            _playniteApi = playniteApi;
            _waveformService = waveformService;
            _playbackService = playbackService;
            _fileService = fileService;
            _settingsProvider = settingsProvider;

            LoadFileList();
        }

        private void LoadFileList()
        {
            var songs = _fileService?.GetAvailableSongs(_game) ?? new List<string>();
            Logger.DebugIf(LogPrefix,$"LoadFileList: found {songs.Count} songs");
            FileComboBox.Items.Clear();

            foreach (var song in songs)
            {
                var fileName = System.IO.Path.GetFileName(song);
                FileComboBox.Items.Add(new ComboBoxItem
                {
                    Content = fileName,
                    Tag = song
                });
            }

            if (FileComboBox.Items.Count > 0)
            {
                FileComboBox.SelectedIndex = 0;
            }
            else
            {
                Logger.DebugIf(LogPrefix,"No audio files found");
                NoWaveformText.Text = "No audio files found for this game";
                NoWaveformText.Visibility = Visibility.Visible;
            }
        }

        private async void FileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileComboBox.SelectedItem is ComboBoxItem item && item.Tag is string filePath)
            {
                Logger.DebugIf(LogPrefix,$"File selected: {System.IO.Path.GetFileName(filePath)}");
                await LoadWaveformAsync(filePath);
            }
        }

        private async Task LoadWaveformAsync(string filePath)
        {
            Logger.DebugIf(LogPrefix,$"LoadWaveformAsync: {System.IO.Path.GetFileName(filePath)}");
            // Cancel any previous load
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            try
            {
                // Show loading
                LoadingOverlay.Visibility = Visibility.Visible;
                NoWaveformText.Visibility = Visibility.Collapsed;

                // Update file info
                var fileName = System.IO.Path.GetFileName(filePath);
                var fileInfo = new FileInfo(filePath);
                FileNameText.Text = fileName;

                // Check if already trimmed
                var suffix = _settingsProvider?.Invoke()?.PreciseTrimSuffix ?? "-ptrimmed";
                var isAlreadyTrimmed = _waveformService?.IsAlreadyTrimmed(filePath, suffix) ?? false;
                var trimIndicator = isAlreadyTrimmed ? " [Already Trimmed]" : "";

                // Generate waveform
                _waveformData = await _waveformService.GenerateWaveformAsync(filePath, token);

                if (token.IsCancellationRequested) return;

                if (_waveformData == null || !_waveformData.IsValid)
                {
                    Logger.DebugIf(LogPrefix,"Failed to load waveform data");
                    NoWaveformText.Text = "Failed to load waveform";
                    NoWaveformText.Visibility = Visibility.Visible;
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                Logger.DebugIf(LogPrefix,$"Waveform loaded: duration={_waveformData.Duration:mm\\:ss\\.fff}, samples={_waveformData.Samples.Length}");

                // Update file info with duration
                var sizeMB = fileInfo.Length / (1024.0 * 1024.0);
                FileInfoText.Text = $"{_waveformData.Duration:mm\\:ss} | {sizeMB:F1} MB | {_waveformData.SampleRate} Hz{trimIndicator}";

                // Initialize trim window to full duration
                _trimWindow = TrimWindow.FullDuration(_waveformData.Duration);

                // Render waveform
                RenderWaveform();
                UpdateTrimDisplay();

                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
                Logger.DebugIf(LogPrefix,"Waveform load cancelled");
                // Cancelled, ignore
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error loading waveform");
                Logger.DebugIf(LogPrefix,$"Error loading waveform: {ex.Message}");
                NoWaveformText.Text = "Error loading waveform";
                NoWaveformText.Visibility = Visibility.Visible;
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void RenderWaveform()
        {
            if (_waveformData?.Samples == null || WaveformCanvas.ActualWidth <= 0 || WaveformCanvas.ActualHeight <= 0)
                return;

            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;
            var centerY = height / 2;

            // Set center line
            CenterLine.X1 = 0;
            CenterLine.Y1 = centerY;
            CenterLine.X2 = width;
            CenterLine.Y2 = centerY;

            // Build waveform points
            var points = new PointCollection();
            var samples = _waveformData.Samples;
            var samplesPerPixel = (double)samples.Length / width;

            for (int x = 0; x < (int)width; x++)
            {
                var sampleIndex = (int)(x * samplesPerPixel);
                if (sampleIndex >= samples.Length) sampleIndex = samples.Length - 1;

                var sample = samples[sampleIndex];
                var y = centerY - (sample * centerY * 0.9); // 90% of half-height

                points.Add(new Point(x, y));
            }

            // Add bottom half (mirror)
            for (int x = (int)width - 1; x >= 0; x--)
            {
                var sampleIndex = (int)(x * samplesPerPixel);
                if (sampleIndex >= samples.Length) sampleIndex = samples.Length - 1;

                var sample = samples[sampleIndex];
                var y = centerY + (sample * centerY * 0.9);

                points.Add(new Point(x, y));
            }

            WaveformLine.Points = points;
            NoWaveformText.Visibility = Visibility.Collapsed;
        }

        private void UpdateTrimDisplay()
        {
            if (_trimWindow == null || WaveformCanvas.ActualWidth <= 0 || WaveformCanvas.ActualHeight <= 0)
                return;

            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;

            var startX = (width * _trimWindow.StartPercent) / 100;
            var endX = (width * _trimWindow.EndPercent) / 100;

            // Update excluded regions
            Canvas.SetLeft(ExcludedLeft, 0);
            Canvas.SetTop(ExcludedLeft, 0);
            ExcludedLeft.Width = Math.Max(0, startX);
            ExcludedLeft.Height = height;

            Canvas.SetLeft(ExcludedRight, endX);
            Canvas.SetTop(ExcludedRight, 0);
            ExcludedRight.Width = Math.Max(0, width - endX);
            ExcludedRight.Height = height;

            // Update trim window rect
            Canvas.SetLeft(TrimWindowRect, startX);
            Canvas.SetTop(TrimWindowRect, 0);
            TrimWindowRect.Width = Math.Max(0, endX - startX);
            TrimWindowRect.Height = height;

            // Update markers
            Canvas.SetLeft(StartMarker, startX - 3);
            Canvas.SetTop(StartMarker, 0);
            StartMarker.Height = height;

            Canvas.SetLeft(EndMarker, endX - 3);
            Canvas.SetTop(EndMarker, 0);
            EndMarker.Height = height;

            // Update time displays
            StartTimeText.Text = FormatTime(_trimWindow.StartTime);
            EndTimeText.Text = FormatTime(_trimWindow.EndTime);
            DurationText.Text = FormatTime(_trimWindow.Duration);
            TotalDurationText.Text = FormatTime(_trimWindow.TotalDuration);

            // Update button states
            ApplyButton.IsEnabled = _trimWindow.IsValid &&
                (_trimWindow.StartTime > TimeSpan.Zero || _trimWindow.EndTime < _trimWindow.TotalDuration);
            PreviewButton.IsEnabled = _trimWindow.IsValid;
            ResetButton.IsEnabled = _trimWindow.StartTime > TimeSpan.Zero || _trimWindow.EndTime < _trimWindow.TotalDuration;
        }

        private string FormatTime(TimeSpan time)
        {
            return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
        }

        #region Mouse Interaction

        private void WaveformCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_trimWindow == null) return;

            var pos = e.GetPosition(WaveformCanvas);
            var width = WaveformCanvas.ActualWidth;

            // Check if clicking on trim window to drag both markers
            var startX = (width * _trimWindow.StartPercent) / 100;
            var endX = (width * _trimWindow.EndPercent) / 100;

            if (pos.X >= startX + 10 && pos.X <= endX - 10)
            {
                _dragMode = DragMode.Window;
                _dragStartPoint = pos;
                _dragStartValue = _trimWindow.StartPercent;
                _dragEndValue = _trimWindow.EndPercent;
                WaveformCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void StartMarker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_trimWindow == null) return;

            _dragMode = DragMode.StartMarker;
            _dragStartPoint = e.GetPosition(WaveformCanvas);
            _dragStartValue = _trimWindow.StartPercent;
            WaveformCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void EndMarker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_trimWindow == null) return;

            _dragMode = DragMode.EndMarker;
            _dragStartPoint = e.GetPosition(WaveformCanvas);
            _dragEndValue = _trimWindow.EndPercent;
            WaveformCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void WaveformCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragMode == DragMode.None || _trimWindow == null) return;

            var pos = e.GetPosition(WaveformCanvas);
            var width = WaveformCanvas.ActualWidth;
            var percent = (pos.X / width) * 100;

            percent = Math.Max(0, Math.Min(100, percent));

            switch (_dragMode)
            {
                case DragMode.StartMarker:
                    var newStartPercent = percent;
                    if (newStartPercent < _trimWindow.EndPercent - 1) // Min 1% duration
                    {
                        _trimWindow.StartTime = TimeSpan.FromMilliseconds(
                            _trimWindow.TotalDuration.TotalMilliseconds * newStartPercent / 100);
                    }
                    break;

                case DragMode.EndMarker:
                    var newEndPercent = percent;
                    if (newEndPercent > _trimWindow.StartPercent + 1) // Min 1% duration
                    {
                        _trimWindow.EndTime = TimeSpan.FromMilliseconds(
                            _trimWindow.TotalDuration.TotalMilliseconds * newEndPercent / 100);
                    }
                    break;

                case DragMode.Window:
                    var delta = percent - (_dragStartPoint.X / width * 100);
                    var newStart = _dragStartValue + delta;
                    var newEnd = _dragEndValue + delta;

                    if (newStart >= 0 && newEnd <= 100)
                    {
                        _trimWindow.StartTime = TimeSpan.FromMilliseconds(
                            _trimWindow.TotalDuration.TotalMilliseconds * newStart / 100);
                        _trimWindow.EndTime = TimeSpan.FromMilliseconds(
                            _trimWindow.TotalDuration.TotalMilliseconds * newEnd / 100);
                    }
                    break;
            }

            UpdateTrimDisplay();
        }

        private void WaveformCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragMode = DragMode.None;
            WaveformCanvas.ReleaseMouseCapture();
        }

        private void WaveformCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_dragMode != DragMode.None && !WaveformCanvas.IsMouseCaptured)
            {
                _dragMode = DragMode.None;
            }
        }

        private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderWaveform();
            UpdateTrimDisplay();
        }

        #endregion

        #region Button Handlers

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadFileList();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _trimWindow?.Reset();
            UpdateTrimDisplay();
        }

        private async void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_trimWindow == null || !_trimWindow.IsValid || _waveformData == null) return;

            if (_isPreviewing)
            {
                Logger.DebugIf(LogPrefix,"Stopping preview");
                StopPreview();
                return;
            }

            try
            {
                Logger.DebugIf(LogPrefix,$"Starting preview: {FormatTime(_trimWindow.StartTime)} to {FormatTime(_trimWindow.EndTime)}");
                _isPreviewing = true;
                PreviewButton.Content = "Stop";

                // Stop current playback
                _playbackService?.Stop();

                // Play the file from the start marker position
                _playbackService?.LoadAndPlayFileFrom(_waveformData.FilePath, _trimWindow.StartTime);

                // Set up timer to stop at end time (duration of the trim window)
                var previewDuration = _trimWindow.Duration;
                _previewTimer = new DispatcherTimer
                {
                    Interval = previewDuration
                };
                _previewTimer.Tick += (s, args) =>
                {
                    StopPreview();
                };
                _previewTimer.Start();

                // Show playhead animation
                await AnimatePlayhead();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error previewing trim");
                StopPreview();
            }
        }

        private void StopPreview()
        {
            _isPreviewing = false;
            PreviewButton.Content = "Preview";
            _previewTimer?.Stop();
            _previewTimer = null;
            _playbackService?.Stop();
            Playhead.Visibility = Visibility.Collapsed;
        }

        private async Task AnimatePlayhead()
        {
            if (_trimWindow == null || WaveformCanvas.ActualWidth <= 0) return;

            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;
            var startX = (width * _trimWindow.StartPercent) / 100;
            var endX = (width * _trimWindow.EndPercent) / 100;

            Playhead.X1 = startX;
            Playhead.Y1 = 0;
            Playhead.X2 = startX;
            Playhead.Y2 = height;
            Playhead.Visibility = Visibility.Visible;

            var duration = _trimWindow.Duration;
            var startTime = DateTime.Now;

            while (_isPreviewing)
            {
                var elapsed = DateTime.Now - startTime;
                var progress = elapsed.TotalMilliseconds / duration.TotalMilliseconds;

                if (progress >= 1) break;

                var x = startX + (endX - startX) * progress;
                Playhead.X1 = x;
                Playhead.X2 = x;

                await Task.Delay(16); // ~60fps
            }

            Playhead.Visibility = Visibility.Collapsed;
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_trimWindow == null || !_trimWindow.IsValid || _waveformData == null) return;

            var filePath = _waveformData.FilePath;
            var fileName = System.IO.Path.GetFileName(filePath);
            var suffix = _settingsProvider?.Invoke()?.PreciseTrimSuffix ?? "-ptrimmed";

            Logger.DebugIf(LogPrefix,$"ApplyButton_Click: file={fileName}, start={FormatTime(_trimWindow.StartTime)}, end={FormatTime(_trimWindow.EndTime)}");

            // Confirm with user
            var result = _playniteApi.Dialogs.ShowMessage(
                $"Apply precise trim to '{fileName}'?\n\n" +
                $"Keep: {FormatTime(_trimWindow.StartTime)} to {FormatTime(_trimWindow.EndTime)}\n" +
                $"Duration: {FormatTime(_trimWindow.Duration)}\n\n" +
                $"The original file will be preserved in the PreservedOriginals folder.",
                "Confirm Trim",
                MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes)
            {
                Logger.DebugIf(LogPrefix,"User cancelled trim confirmation");
                return;
            }

            try
            {
                Logger.DebugIf(LogPrefix,"Applying trim...");
                ApplyButton.IsEnabled = false;
                ApplyButton.Content = "Applying...";

                // Stop any playback
                _playbackService?.Stop();

                // Get FFmpeg path from settings and pass it directly
                var settings = _settingsProvider?.Invoke();
                var ffmpegPath = settings?.FFmpegPath;
                Logger.DebugIf(LogPrefix,$"FFmpeg path from settings: {ffmpegPath}");

                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    Logger.DebugIf(LogPrefix,"FFmpeg path is null or empty from settings");
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg path is not configured.\n\nPlease configure FFmpeg path in Settings → Audio Normalization.",
                        "FFmpeg Not Found");
                    ApplyButton.IsEnabled = true;
                    ApplyButton.Content = "Apply Trim";
                    return;
                }

                var success = await _waveformService.ApplyTrimAsync(filePath, _trimWindow, suffix, ffmpegPath);

                if (success)
                {
                    Logger.DebugIf(LogPrefix,"Trim applied successfully");
                    _playniteApi.Dialogs.ShowMessage(
                        $"Successfully trimmed '{fileName}'.\n\n" +
                        $"Original file has been preserved.",
                        "Trim Complete");

                    // Refresh file list
                    LoadFileList();
                }
                else
                {
                    Logger.DebugIf(LogPrefix,"Trim operation failed");
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"Failed to trim '{fileName}'.\n\nCheck that FFmpeg is configured correctly.",
                        "Trim Failed");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error applying trim");
                Logger.DebugIf(LogPrefix,$"Exception during trim: {ex.Message}");
                _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "Trim Error");
            }
            finally
            {
                ApplyButton.IsEnabled = true;
                ApplyButton.Content = "Apply Trim";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.DebugIf(LogPrefix,"Cancel button clicked");
            StopPreview();
            _loadCts?.Cancel();

            // Close the parent window
            var window = Window.GetWindow(this);
            window?.Close();
        }

        #endregion

        /// <summary>
        /// Cleanup when dialog closes
        /// </summary>
        public void Cleanup()
        {
            Logger.DebugIf(LogPrefix,"Cleanup called");
            StopPreview();
            _loadCts?.Cancel();
        }
    }
}
