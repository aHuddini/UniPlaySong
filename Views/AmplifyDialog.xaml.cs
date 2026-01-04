using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Playnite.SDK;
using IOPath = System.IO.Path;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Views
{
    public partial class AmplifyDialog : UserControl
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private static void LogDebug(string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                Logger.Debug($"[Amplify:Desktop] {message}");
            }
        }

        private IPlayniteAPI _playniteApi;
        private AudioAmplifyService _amplifyService;
        private IMusicPlaybackService _playbackService;
        private GameMusicFileService _fileService;
        private Game _game;
        private Func<UniPlaySongSettings> _settingsProvider;

        private AmplifyWaveformData _waveformData;
        private CancellationTokenSource _loadCts;

        // Gain state
        private float _currentGainDb = 0f;
        private const float MinGainDb = -12f;
        private const float MaxGainDb = 12f;
        private const float GainStep = 0.5f;

        // Drag state
        private bool _isDragging = false;
        private double _dragStartY;
        private float _dragStartGain;

        public AmplifyDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize the dialog for a specific game
        /// </summary>
        public void Initialize(
            Game game,
            IPlayniteAPI playniteApi,
            AudioAmplifyService amplifyService,
            IMusicPlaybackService playbackService,
            GameMusicFileService fileService,
            Func<UniPlaySongSettings> settingsProvider)
        {
            LogDebug($"Initialize called for game: {game?.Name}");
            _game = game;
            _playniteApi = playniteApi;
            _amplifyService = amplifyService;
            _playbackService = playbackService;
            _fileService = fileService;
            _settingsProvider = settingsProvider;

            LoadFileList();
        }

        private void LoadFileList()
        {
            var songs = _fileService?.GetAvailableSongs(_game) ?? new List<string>();
            LogDebug($"LoadFileList: found {songs.Count} songs");
            FileComboBox.Items.Clear();

            foreach (var song in songs)
            {
                var fileName = IOPath.GetFileName(song);
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
                LogDebug("No audio files found");
                NoWaveformText.Text = "No audio files found for this game";
                NoWaveformText.Visibility = Visibility.Visible;
            }
        }

        private async void FileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileComboBox.SelectedItem is ComboBoxItem item && item.Tag is string filePath)
            {
                LogDebug($"File selected: {IOPath.GetFileName(filePath)}");
                await LoadWaveformAsync(filePath);
            }
        }

        private async Task LoadWaveformAsync(string filePath)
        {
            LogDebug($"LoadWaveformAsync: {IOPath.GetFileName(filePath)}");
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                NoWaveformText.Visibility = Visibility.Collapsed;

                var fileName = IOPath.GetFileName(filePath);
                var fileInfo = new System.IO.FileInfo(filePath);
                FileNameText.Text = fileName;

                // Reset gain
                _currentGainDb = 0f;

                // Generate waveform with amplitude analysis
                _waveformData = await _amplifyService.GenerateWaveformAsync(filePath, token);

                if (token.IsCancellationRequested) return;

                if (_waveformData == null || !_waveformData.IsValid)
                {
                    LogDebug("Failed to load waveform data");
                    NoWaveformText.Text = "Failed to load waveform";
                    NoWaveformText.Visibility = Visibility.Visible;
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                LogDebug($"Waveform loaded: duration={_waveformData.Duration:mm\\:ss\\.fff}, peak={_waveformData.PeakDb:F1}dB");

                // Update file info
                var sizeMB = fileInfo.Length / (1024.0 * 1024.0);
                FileInfoText.Text = $"{_waveformData.Duration:mm\\:ss} | {sizeMB:F1} MB | {_waveformData.SampleRate} Hz";

                // Render waveform and UI
                RenderWaveform();
                UpdateGainDisplay();

                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
                LogDebug("Waveform load cancelled");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error loading waveform");
                NoWaveformText.Text = "Error loading waveform";
                NoWaveformText.Visibility = Visibility.Visible;
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void RenderWaveform()
        {
            if (_waveformData?.Samples == null || WaveformCanvas.ActualWidth <= 0 || WaveformCanvas.ActualHeight <= 0)
                return;

            var width = WaveformCanvas.ActualWidth - 50; // Leave room for dB scale
            var height = WaveformCanvas.ActualHeight;
            var centerY = height / 2;

            // Set center line (0dB reference)
            CenterLine.X1 = 0;
            CenterLine.Y1 = centerY;
            CenterLine.X2 = width;
            CenterLine.Y2 = centerY;

            // Build original (ghost) waveform
            var originalPoints = BuildWaveformPoints(_waveformData.Samples, width, height, centerY);
            OriginalWaveformLine.Points = originalPoints;

            // Build scaled waveform based on current gain
            UpdateScaledWaveform();

            NoWaveformText.Visibility = Visibility.Collapsed;
        }

        private void UpdateScaledWaveform()
        {
            if (_waveformData?.Samples == null || WaveformCanvas.ActualWidth <= 0 || WaveformCanvas.ActualHeight <= 0)
                return;

            var width = WaveformCanvas.ActualWidth - 50;
            var height = WaveformCanvas.ActualHeight;
            var centerY = height / 2;

            // Get scaled samples with clipping info
            var (scaledSamples, clipping) = _waveformData.GetScaledSamples(_currentGainDb);

            // Build scaled waveform
            var scaledPoints = BuildWaveformPoints(scaledSamples, width, height, centerY);
            ScaledWaveformLine.Points = scaledPoints;

            // Check for clipping
            bool hasClipping = false;
            for (int i = 0; i < clipping.Length; i++)
            {
                if (clipping[i])
                {
                    hasClipping = true;
                    break;
                }
            }

            // Build clipping region if needed
            if (hasClipping)
            {
                BuildClippingRegion(scaledSamples, clipping, width, height, centerY);
                ClippingRegion.Visibility = Visibility.Visible;
                ClippingWarning.Visibility = Visibility.Visible;
            }
            else
            {
                ClippingRegion.Visibility = Visibility.Collapsed;
                ClippingWarning.Visibility = Visibility.Collapsed;
            }

            // Update gain line position
            UpdateGainLine();
        }

        private PointCollection BuildWaveformPoints(float[] samples, double width, double height, double centerY)
        {
            var points = new PointCollection();
            var samplesPerPixel = (double)samples.Length / width;

            // Top half
            for (int x = 0; x < (int)width; x++)
            {
                var sampleIndex = (int)(x * samplesPerPixel);
                if (sampleIndex >= samples.Length) sampleIndex = samples.Length - 1;

                var sample = samples[sampleIndex];
                var y = centerY - (sample * centerY * 0.9);
                points.Add(new Point(x, y));
            }

            // Bottom half (mirror)
            for (int x = (int)width - 1; x >= 0; x--)
            {
                var sampleIndex = (int)(x * samplesPerPixel);
                if (sampleIndex >= samples.Length) sampleIndex = samples.Length - 1;

                var sample = samples[sampleIndex];
                var y = centerY + (sample * centerY * 0.9);
                points.Add(new Point(x, y));
            }

            return points;
        }

        private void BuildClippingRegion(float[] scaledSamples, bool[] clipping, double width, double height, double centerY)
        {
            // Find contiguous clipping regions and highlight them
            var points = new PointCollection();
            var samplesPerPixel = (double)scaledSamples.Length / width;

            bool inClip = false;
            int clipStartX = 0;

            for (int x = 0; x <= (int)width; x++)
            {
                var sampleIndex = Math.Min((int)(x * samplesPerPixel), clipping.Length - 1);
                bool isClipping = sampleIndex >= 0 && sampleIndex < clipping.Length && clipping[sampleIndex];

                if (isClipping && !inClip)
                {
                    clipStartX = x;
                    inClip = true;
                }
                else if (!isClipping && inClip)
                {
                    // End of clip region - add rectangle points
                    // For simplicity, we'll just color the entire waveform area red where clipping occurs
                    inClip = false;
                }
            }

            // Simple approach: draw the clipping portions of the waveform
            // We'll overlay a semi-transparent red on clipping areas
            var clippingPoints = new PointCollection();
            bool hasAnyClipping = false;

            for (int x = 0; x < (int)width; x++)
            {
                var sampleIndex = (int)(x * samplesPerPixel);
                if (sampleIndex >= clipping.Length) sampleIndex = clipping.Length - 1;

                if (clipping[sampleIndex])
                {
                    hasAnyClipping = true;
                    // Add top point at 0dB line
                    clippingPoints.Add(new Point(x, centerY * 0.1));
                }
            }

            if (hasAnyClipping)
            {
                // Add bottom points in reverse
                for (int x = (int)width - 1; x >= 0; x--)
                {
                    var sampleIndex = (int)(x * samplesPerPixel);
                    if (sampleIndex >= clipping.Length) sampleIndex = clipping.Length - 1;

                    if (clipping[sampleIndex])
                    {
                        clippingPoints.Add(new Point(x, height - centerY * 0.1));
                    }
                }
            }

            ClippingRegion.Points = clippingPoints;
        }

        private void UpdateGainLine()
        {
            if (WaveformCanvas.ActualWidth <= 0 || WaveformCanvas.ActualHeight <= 0)
                return;

            var width = WaveformCanvas.ActualWidth - 50;
            var height = WaveformCanvas.ActualHeight;
            var centerY = height / 2;

            // Convert gain to Y position
            // 0dB = center, +12dB = top, -12dB = bottom
            var gainRange = MaxGainDb - MinGainDb; // 24dB range
            var gainNormalized = (_currentGainDb - MinGainDb) / gainRange; // 0 to 1
            var lineY = height - (gainNormalized * height); // Invert for screen coords

            // Position gain line
            GainLine.X1 = 20;
            GainLine.Y1 = lineY;
            GainLine.X2 = width - 20;
            GainLine.Y2 = lineY;

            // Position handles
            Canvas.SetLeft(GainHandleLeft, 14);
            Canvas.SetTop(GainHandleLeft, lineY - 6);
            Canvas.SetLeft(GainHandleRight, width - 26);
            Canvas.SetTop(GainHandleRight, lineY - 6);

            // Show headroom line (max safe gain)
            if (_waveformData != null)
            {
                var headroomDb = _waveformData.HeadroomDb;
                if (headroomDb < MaxGainDb)
                {
                    var headroomNormalized = (headroomDb - MinGainDb) / gainRange;
                    var headroomY = height - (headroomNormalized * height);

                    HeadroomLine.X1 = 0;
                    HeadroomLine.Y1 = headroomY;
                    HeadroomLine.X2 = width;
                    HeadroomLine.Y2 = headroomY;
                    HeadroomLine.Visibility = Visibility.Visible;
                }
                else
                {
                    HeadroomLine.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void UpdateGainDisplay()
        {
            // Update gain text
            var gainSign = _currentGainDb >= 0 ? "+" : "";
            GainText.Text = $"{gainSign}{_currentGainDb:F1} dB";

            // Update peak and headroom
            if (_waveformData != null)
            {
                PeakText.Text = $"{_waveformData.PeakDb:F1} dB";
                HeadroomText.Text = $"+{_waveformData.HeadroomDb:F1} dB";

                // Color code based on clipping
                if (_waveformData.WouldClip(_currentGainDb))
                {
                    GainText.Foreground = new SolidColorBrush(Color.FromRgb(255, 68, 68)); // Red
                }
                else if (_currentGainDb > _waveformData.HeadroomDb * 0.8f)
                {
                    GainText.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange
                }
                else
                {
                    GainText.Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // Gold
                }
            }

            // Update buttons
            ApplyButton.IsEnabled = _waveformData != null && Math.Abs(_currentGainDb) >= 0.1f;
            PreviewButton.IsEnabled = _waveformData != null;
            ResetButton.IsEnabled = Math.Abs(_currentGainDb) >= 0.1f;

            // Update scaled waveform
            UpdateScaledWaveform();
        }

        #region Mouse Interaction

        private void WaveformCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_waveformData == null) return;

            var pos = e.GetPosition(WaveformCanvas);
            _isDragging = true;
            _dragStartY = pos.Y;
            _dragStartGain = _currentGainDb;
            WaveformCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void WaveformCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _waveformData == null) return;

            var pos = e.GetPosition(WaveformCanvas);
            var height = WaveformCanvas.ActualHeight;

            // Calculate gain from Y position change
            // Moving up = increase gain, moving down = decrease
            var deltaY = _dragStartY - pos.Y;
            var gainRange = MaxGainDb - MinGainDb;
            var gainDelta = (float)(deltaY / height * gainRange);

            var newGain = _dragStartGain + gainDelta;
            newGain = Math.Max(MinGainDb, Math.Min(MaxGainDb, newGain));

            // Snap to 0.5dB increments
            newGain = (float)Math.Round(newGain / GainStep) * GainStep;

            if (Math.Abs(newGain - _currentGainDb) >= 0.01f)
            {
                _currentGainDb = newGain;
                UpdateGainDisplay();
            }
        }

        private void WaveformCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            WaveformCanvas.ReleaseMouseCapture();
        }

        private void WaveformCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isDragging && !WaveformCanvas.IsMouseCaptured)
            {
                _isDragging = false;
            }
        }

        private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderWaveform();
            UpdateGainDisplay();
        }

        #endregion

        #region Button Handlers

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadFileList();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _currentGainDb = 0f;
            UpdateGainDisplay();
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_waveformData == null) return;

            try
            {
                LogDebug($"Preview with gain: {_currentGainDb:+0.0;-0.0;0}dB");

                // Get user's configured volume setting (0-100) and convert to 0-1 range
                var settings = _settingsProvider?.Invoke();
                var userVolume = (settings?.MusicVolume ?? 100) / 100.0;

                // Calculate gain multiplier from dB
                // dB = 20 * log10(multiplier), so multiplier = 10^(dB/20)
                var gainMultiplier = Math.Pow(10, _currentGainDb / 20.0);

                // Combine user volume with gain adjustment
                // This lets users hear the effect at their normal listening volume
                var combinedVolume = userVolume * gainMultiplier;

                // Clamp to valid range
                var clampedVolume = Math.Min(1.0, Math.Max(0.0, combinedVolume));
                bool wasCapped = combinedVolume > 1.0;

                // Use the same SDL2 backend for preview (consistent volume with normal playback)
                _playbackService?.PlayPreview(_waveformData.FilePath, clampedVolume);

                LogDebug($"Preview at volume {clampedVolume:F2} (user: {userVolume:F2} x gain: {gainMultiplier:F2})");

                if (wasCapped)
                {
                    _playniteApi.Dialogs.ShowMessage(
                        $"Preview playing at maximum volume.\n\n" +
                        $"The full +{_currentGainDb:F1}dB amplification will be permanently " +
                        $"applied to the file when you click Apply.",
                        "Preview Limitation");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error previewing");
            }
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_waveformData == null || Math.Abs(_currentGainDb) < 0.1f) return;

            var filePath = _waveformData.FilePath;
            var fileName = IOPath.GetFileName(filePath);

            LogDebug($"ApplyButton_Click: file={fileName}, gain={_currentGainDb:+0.0;-0.0;0}dB");

            // Warn about clipping
            var clippingWarning = "";
            if (_waveformData.WouldClip(_currentGainDb))
            {
                clippingWarning = "\n\nWARNING: This gain level will cause clipping (distortion)!\n" +
                                  $"Maximum safe gain is +{_waveformData.HeadroomDb:F1}dB.";
            }

            var result = _playniteApi.Dialogs.ShowMessage(
                $"Apply {_currentGainDb:+0.0;-0.0;0}dB gain to '{fileName}'?{clippingWarning}\n\n" +
                $"The original file will be moved to the PreservedOriginals folder.",
                "Confirm Amplify",
                MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                ApplyButton.IsEnabled = false;
                ApplyButton.Content = "Applying...";

                _playbackService?.Stop();

                var settings = _settingsProvider?.Invoke();
                var ffmpegPath = settings?.FFmpegPath;

                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg path is not configured.\n\nPlease configure FFmpeg path in Settings.",
                        "FFmpeg Not Found");
                    return;
                }

                // Use suffix based on gain direction
                var suffix = _currentGainDb >= 0 ? "-amplified" : "-attenuated";

                var success = await _amplifyService.ApplyAmplifyAsync(filePath, _currentGainDb, suffix, ffmpegPath);

                if (success)
                {
                    _playniteApi.Dialogs.ShowMessage(
                        $"Successfully created '{IOPath.GetFileNameWithoutExtension(fileName)}{suffix}{IOPath.GetExtension(fileName)}'.\n\n" +
                        $"Gain applied: {_currentGainDb:+0.0;-0.0;0}dB\n\n" +
                        $"Original file moved to PreservedOriginals folder.",
                        "Amplify Complete");

                    LoadFileList();
                }
                else
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"Failed to amplify '{fileName}'.\n\nCheck that FFmpeg is configured correctly.",
                        "Amplify Failed");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error applying amplify");
                _playniteApi.Dialogs.ShowErrorMessage($"Error: {ex.Message}", "Amplify Error");
            }
            finally
            {
                ApplyButton.IsEnabled = true;
                ApplyButton.Content = "Apply";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            LogDebug("Cancel button clicked");
            _loadCts?.Cancel();
            _playbackService?.Stop();

            var window = Window.GetWindow(this);
            window?.Close();
        }

        #endregion

        public void Cleanup()
        {
            LogDebug("Cleanup called");
            _loadCts?.Cancel();
            _playbackService?.Stop();
        }
    }
}
