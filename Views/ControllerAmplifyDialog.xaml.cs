using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IOPath = System.IO.Path;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Views
{
    /// <summary>
    /// Controller-friendly audio amplify dialog with two-step interface:
    /// Step 1: File selection
    /// Step 2: Gain adjustment with waveform visualization
    /// </summary>
    public partial class ControllerAmplifyDialog : UserControl
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private static void LogDebug(string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                Logger.Debug($"[Amplify:Controller] {message}");
            }
        }

        // Controller monitoring
        private CancellationTokenSource _controllerMonitoringCancellation;
        private bool _isMonitoring = false;
        private ushort _lastButtonState = 0;

        // D-pad debouncing for file selection
        private DateTime _lastDpadNavigationTime = DateTime.MinValue;
        private const int DpadDebounceMs = 100;

        // Continuous D-pad input tracking for gain adjustment
        private ushort _heldDpadDirection = 0;
        private DateTime _dpadHoldStartTime = DateTime.MinValue;
        private DateTime _lastDpadRepeatTime = DateTime.MinValue;
        private const int InitialRepeatDelayMs = 200;
        private const int FastRepeatIntervalMs = 50;

        // State
        private Game _currentGame;
        private IPlayniteAPI _playniteApi;
        private GameMusicFileService _fileService;
        private IMusicPlaybackService _playbackService;
        private AudioAmplifyService _amplifyService;
        private Func<UniPlaySongSettings> _getSettings;
        private List<string> _musicFiles;
        private string _selectedFilePath;

        // Waveform/Amplify data
        private AmplifyWaveformData _waveformData;
        private CancellationTokenSource _loadingCancellation;

        // Gain state
        private float _currentGainDb = 0f;
        private const float MinGainDb = -12f;
        private const float MaxGainDb = 12f;
        private const float FineGainStep = 0.5f;
        private const float CoarseGainStep = 3f;

        // Current step
        private enum DialogStep { FileSelection, AmplifyEditor }
        private DialogStep _currentStep = DialogStep.FileSelection;

        public ControllerAmplifyDialog()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                FilesListBox.Focus();
                StartControllerMonitoring();
            };

            Unloaded += (s, e) =>
            {
                StopControllerMonitoring();
                _loadingCancellation?.Cancel();
            };

            KeyDown += OnKeyDown;
        }

        /// <summary>
        /// Initialize the dialog for a specific game
        /// </summary>
        public void InitializeForGame(
            Game game,
            IPlayniteAPI playniteApi,
            GameMusicFileService fileService,
            IMusicPlaybackService playbackService,
            AudioAmplifyService amplifyService,
            Func<UniPlaySongSettings> getSettings)
        {
            try
            {
                _currentGame = game;
                _playniteApi = playniteApi;
                _fileService = fileService;
                _playbackService = playbackService;
                _amplifyService = amplifyService;
                _getSettings = getSettings;

                LogDebug($"Initialized controller amplify dialog for game: {game?.Name}");

                ShowFileSelectionStep();
                LoadMusicFiles();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing controller amplify dialog");
                UpdateFeedback("FileSelection", "Error initializing dialog");
            }
        }

        #region Step Navigation

        private void ShowFileSelectionStep()
        {
            _currentStep = DialogStep.FileSelection;
            FileSelectionStep.Visibility = Visibility.Visible;
            AmplifyEditorStep.Visibility = Visibility.Collapsed;
            FilesListBox.Focus();
        }

        private void ShowAmplifyEditorStep()
        {
            _currentStep = DialogStep.AmplifyEditor;
            FileSelectionStep.Visibility = Visibility.Collapsed;
            AmplifyEditorStep.Visibility = Visibility.Visible;
        }

        #endregion

        #region File Selection (Step 1)

        private void LoadMusicFiles()
        {
            try
            {
                UpdateFeedback("FileSelection", "Loading music files...");

                Task.Run(() =>
                {
                    try
                    {
                        _musicFiles = _fileService?.GetAvailableSongs(_currentGame) ?? new List<string>();

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (_musicFiles.Count == 0)
                                {
                                    UpdateFeedback("FileSelection", "No music files found for this game");
                                    ShowNoFilesMessage();
                                    return;
                                }

                                PopulateFilesList();
                                UpdateFeedback("FileSelection", $"Found {_musicFiles.Count} files - D-Pad to navigate, A to select");
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, "Error updating UI with music files");
                                UpdateFeedback("FileSelection", "Error loading files");
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error loading music files");
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            UpdateFeedback("FileSelection", "Error loading music files");
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initiating music file load");
                UpdateFeedback("FileSelection", "Failed to load files");
            }
        }

        private void PopulateFilesList()
        {
            FilesListBox.Items.Clear();

            foreach (var filePath in _musicFiles)
            {
                var listItem = new ListBoxItem();
                var fileName = IOPath.GetFileName(filePath);

                var stackPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var nameBlock = new TextBlock
                {
                    Text = fileName,
                    FontSize = 14,
                    FontWeight = FontWeights.Medium,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                stackPanel.Children.Add(nameBlock);

                listItem.Content = stackPanel;
                listItem.Tag = filePath;
                listItem.HorizontalContentAlignment = HorizontalAlignment.Stretch;

                FilesListBox.Items.Add(listItem);
            }

            if (FilesListBox.Items.Count > 0)
            {
                FilesListBox.SelectedIndex = 0;
                FilesListBox.Focus();
            }
        }

        private void ShowNoFilesMessage()
        {
            FilesListBox.Items.Clear();
            var messageItem = new ListBoxItem
            {
                Content = "No music files found for this game.",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontStyle = FontStyles.Italic,
                IsEnabled = false
            };
            FilesListBox.Items.Add(messageItem);
        }

        private void SelectFileAndProceed()
        {
            var selectedItem = FilesListBox.SelectedItem as ListBoxItem;
            if (selectedItem?.Tag is string filePath)
            {
                _selectedFilePath = filePath;
                CurrentFileText.Text = IOPath.GetFileName(filePath);
                _currentGainDb = 0f; // Reset gain for new file
                ShowAmplifyEditorStep();
                LoadWaveformAsync(filePath);
            }
            else
            {
                UpdateFeedback("FileSelection", "Please select a file");
            }
        }

        #endregion

        #region Amplify Editor (Step 2)

        private async void LoadWaveformAsync(string filePath)
        {
            try
            {
                _loadingCancellation?.Cancel();
                _loadingCancellation = new CancellationTokenSource();
                var token = _loadingCancellation.Token;

                LoadingOverlay.Visibility = Visibility.Visible;
                UpdateFeedback("Amplify", "Loading waveform...");

                await Task.Run(async () =>
                {
                    try
                    {
                        _waveformData = await _amplifyService.GenerateWaveformAsync(filePath, token);

                        if (token.IsCancellationRequested) return;

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (_waveformData?.IsValid == true)
                                {
                                    RenderWaveform();
                                    UpdateGainDisplay();
                                    LoadingOverlay.Visibility = Visibility.Collapsed;
                                    UpdateFeedback("Amplify", "Use D-Pad Up/Down to adjust gain");
                                }
                                else
                                {
                                    UpdateFeedback("Amplify", "Failed to load waveform data");
                                    LoadingOverlay.Visibility = Visibility.Collapsed;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, "Error displaying waveform");
                                UpdateFeedback("Amplify", "Error displaying waveform");
                                LoadingOverlay.Visibility = Visibility.Collapsed;
                            }
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancelled, ignore
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error generating waveform");
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            UpdateFeedback("Amplify", $"Error: {ex.Message}");
                            LoadingOverlay.Visibility = Visibility.Collapsed;
                        }));
                    }
                }, token);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting waveform load");
                UpdateFeedback("Amplify", "Failed to start loading");
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

            // Build original (ghost) waveform
            var originalPoints = BuildWaveformPoints(_waveformData.Samples, width, height, centerY);
            OriginalWaveformLine.Points = originalPoints;

            // Set center line (0dB reference)
            CenterLine.X1 = 0;
            CenterLine.Y1 = centerY;
            CenterLine.X2 = width;
            CenterLine.Y2 = centerY;

            // Update scaled waveform
            UpdateScaledWaveform();
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

            // Update clipping warning
            ClippingWarning.Visibility = hasClipping ? Visibility.Visible : Visibility.Collapsed;
            ClippingRegion.Visibility = hasClipping ? Visibility.Visible : Visibility.Collapsed;

            // Update gain line position
            UpdateGainLine();
        }

        private PointCollection BuildWaveformPoints(float[] samples, double width, double height, double centerY)
        {
            var points = new PointCollection();
            var samplesPerPixel = Math.Max(1, samples.Length / (int)width);

            // Top half
            for (int x = 0; x < (int)width; x++)
            {
                int sampleIndex = (int)((double)x / width * samples.Length);
                sampleIndex = Math.Min(sampleIndex, samples.Length - 1);

                float sample = samples[sampleIndex];
                double y = centerY - (sample * centerY * 0.9);
                points.Add(new Point(x, y));
            }

            // Bottom half (mirror)
            for (int x = (int)width - 1; x >= 0; x--)
            {
                int sampleIndex = (int)((double)x / width * samples.Length);
                sampleIndex = Math.Min(sampleIndex, samples.Length - 1);

                float sample = samples[sampleIndex];
                double y = centerY + (sample * centerY * 0.9);
                points.Add(new Point(x, y));
            }

            return points;
        }

        private void UpdateGainLine()
        {
            if (WaveformCanvas.ActualWidth <= 0 || WaveformCanvas.ActualHeight <= 0)
                return;

            var width = WaveformCanvas.ActualWidth - 50;
            var height = WaveformCanvas.ActualHeight;

            // Convert gain to Y position
            var gainRange = MaxGainDb - MinGainDb;
            var gainNormalized = (_currentGainDb - MinGainDb) / gainRange;
            var lineY = height - (gainNormalized * height);

            // Position gain line
            GainLine.X1 = 20;
            GainLine.Y1 = lineY;
            GainLine.X2 = width - 20;
            GainLine.Y2 = lineY;

            // Position handles (larger for controller visibility)
            Canvas.SetLeft(GainHandleLeft, 12);
            Canvas.SetTop(GainHandleLeft, lineY - 8);
            Canvas.SetLeft(GainHandleRight, width - 28);
            Canvas.SetTop(GainHandleRight, lineY - 8);

            // Show headroom line
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
            var gainSign = _currentGainDb >= 0 ? "+" : "";
            GainText.Text = $"{gainSign}{_currentGainDb:F1} dB";

            if (_waveformData != null)
            {
                PeakText.Text = $"{_waveformData.PeakDb:F1} dB";
                HeadroomText.Text = $"+{_waveformData.HeadroomDb:F1} dB";

                // Color code
                if (_waveformData.WouldClip(_currentGainDb))
                {
                    GainText.Foreground = new SolidColorBrush(Color.FromRgb(255, 68, 68));
                }
                else if (_currentGainDb > _waveformData.HeadroomDb * 0.8f)
                {
                    GainText.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0));
                }
                else
                {
                    GainText.Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0));
                }
            }

            ApplyButton.IsEnabled = _waveformData != null && Math.Abs(_currentGainDb) >= 0.1f;
            PreviewButton.IsEnabled = _waveformData != null;
            ResetButton.IsEnabled = Math.Abs(_currentGainDb) >= 0.1f;

            UpdateScaledWaveform();
        }

        private void AdjustGain(float deltaDb)
        {
            var newGain = _currentGainDb + deltaDb;
            newGain = Math.Max(MinGainDb, Math.Min(MaxGainDb, newGain));

            if (Math.Abs(newGain - _currentGainDb) >= 0.01f)
            {
                _currentGainDb = newGain;
                UpdateGainDisplay();
                LogDebug($"Gain adjusted to {_currentGainDb:+0.0;-0.0;0}dB");
            }
        }

        private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_waveformData?.IsValid == true)
            {
                RenderWaveform();
            }
        }

        #endregion

        #region Controller Input Handling

        private void StartControllerMonitoring()
        {
            if (_isMonitoring) return;

            _isMonitoring = true;
            _controllerMonitoringCancellation = new CancellationTokenSource();

            _ = Task.Run(() => CheckButtonPresses(_controllerMonitoringCancellation.Token));
            LogDebug("Started controller monitoring for amplify dialog");
        }

        private void StopControllerMonitoring()
        {
            if (!_isMonitoring) return;

            _isMonitoring = false;
            _controllerMonitoringCancellation?.Cancel();
            LogDebug("Stopped controller monitoring for amplify dialog");
        }

        private async Task CheckButtonPresses(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                    uint result = (uint)XInputWrapper.XInputGetState(0, ref state);

                    if (result == 0)
                    {
                        ushort currentButtons = state.Gamepad.wButtons;
                        ushort pressedButtons = (ushort)(currentButtons & ~_lastButtonState);

                        if (pressedButtons != 0)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                HandleControllerInput(pressedButtons, state.Gamepad);
                            }));
                        }

                        // In amplify editor mode, check for held D-pad
                        if (_currentStep == DialogStep.AmplifyEditor)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                HandleDpadContinuousInput(state.Gamepad);
                            }));
                        }

                        _lastButtonState = currentButtons;
                    }

                    await Task.Delay(50, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogDebug($"Error in controller monitoring: {ex.Message}");
                    await Task.Delay(100, cancellationToken);
                }
            }
        }

        private void HandleControllerInput(ushort pressedButtons, XInputWrapper.XINPUT_GAMEPAD gamepad)
        {
            try
            {
                if (_currentStep == DialogStep.FileSelection)
                {
                    HandleFileSelectionInput(pressedButtons, gamepad);
                }
                else
                {
                    HandleAmplifyEditorInput(pressedButtons, gamepad);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling controller input");
            }
        }

        private void HandleFileSelectionInput(ushort pressedButtons, XInputWrapper.XINPUT_GAMEPAD gamepad)
        {
            // A button - Select file
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_A) != 0)
            {
                SelectFileAndProceed();
                return;
            }

            // B button - Cancel
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_B) != 0)
            {
                CloseDialog();
                return;
            }

            // D-pad navigation (with debounce)
            var now = DateTime.Now;
            if ((now - _lastDpadNavigationTime).TotalMilliseconds < DpadDebounceMs)
                return;

            int currentIndex = FilesListBox.SelectedIndex;
            int newIndex = currentIndex;

            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_DPAD_UP) != 0)
            {
                newIndex = Math.Max(0, currentIndex - 1);
            }
            else if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_DPAD_DOWN) != 0)
            {
                newIndex = Math.Min(FilesListBox.Items.Count - 1, currentIndex + 1);
            }
            else if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0)
            {
                newIndex = Math.Max(0, currentIndex - 5);
            }
            else if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0)
            {
                newIndex = Math.Min(FilesListBox.Items.Count - 1, currentIndex + 5);
            }

            if (newIndex != currentIndex && newIndex >= 0)
            {
                FilesListBox.SelectedIndex = newIndex;
                FilesListBox.ScrollIntoView(FilesListBox.SelectedItem);
                _lastDpadNavigationTime = now;
            }
        }

        private void HandleAmplifyEditorInput(ushort pressedButtons, XInputWrapper.XINPUT_GAMEPAD gamepad)
        {
            // A button - Preview
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_A) != 0)
            {
                PreviewButton_Click(null, null);
                return;
            }

            // B button - Back to file selection
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_B) != 0)
            {
                BackButton_Click(null, null);
                return;
            }

            // X button - Apply
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_X) != 0)
            {
                ApplyButton_Click(null, null);
                return;
            }

            // Y button - Reset
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_Y) != 0)
            {
                ResetButton_Click(null, null);
                return;
            }

            // LB/RB - Coarse adjustment
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0)
            {
                AdjustGain(-CoarseGainStep);
                UpdateFeedback("Amplify", $"Gain: {_currentGainDb:+0.0;-0.0;0} dB");
            }
            else if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0)
            {
                AdjustGain(+CoarseGainStep);
                UpdateFeedback("Amplify", $"Gain: {_currentGainDb:+0.0;-0.0;0} dB");
            }
        }

        private void HandleDpadContinuousInput(XInputWrapper.XINPUT_GAMEPAD gamepad)
        {
            ushort currentDpad = (ushort)(gamepad.wButtons & (
                XInputWrapper.XINPUT_GAMEPAD_DPAD_UP |
                XInputWrapper.XINPUT_GAMEPAD_DPAD_DOWN));

            var now = DateTime.Now;

            if (currentDpad != 0)
            {
                if (_heldDpadDirection != currentDpad)
                {
                    // New direction pressed
                    _heldDpadDirection = currentDpad;
                    _dpadHoldStartTime = now;
                    _lastDpadRepeatTime = now;

                    // Immediate response
                    ProcessDpadGainAdjustment(currentDpad);
                }
                else
                {
                    // Same direction held
                    var holdDuration = (now - _dpadHoldStartTime).TotalMilliseconds;

                    if (holdDuration >= InitialRepeatDelayMs)
                    {
                        var timeSinceLastRepeat = (now - _lastDpadRepeatTime).TotalMilliseconds;

                        if (timeSinceLastRepeat >= FastRepeatIntervalMs)
                        {
                            ProcessDpadGainAdjustment(currentDpad);
                            _lastDpadRepeatTime = now;
                        }
                    }
                }
            }
            else
            {
                _heldDpadDirection = 0;
            }
        }

        private void ProcessDpadGainAdjustment(ushort dpadDirection)
        {
            if ((dpadDirection & XInputWrapper.XINPUT_GAMEPAD_DPAD_UP) != 0)
            {
                AdjustGain(+FineGainStep);
            }
            else if ((dpadDirection & XInputWrapper.XINPUT_GAMEPAD_DPAD_DOWN) != 0)
            {
                AdjustGain(-FineGainStep);
            }
        }

        #endregion

        #region Keyboard Input (Fallback)

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_currentStep == DialogStep.FileSelection)
            {
                switch (e.Key)
                {
                    case System.Windows.Input.Key.Enter:
                        SelectFileAndProceed();
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Escape:
                        CloseDialog();
                        e.Handled = true;
                        break;
                }
            }
            else
            {
                switch (e.Key)
                {
                    case System.Windows.Input.Key.Up:
                        AdjustGain(+FineGainStep);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Down:
                        AdjustGain(-FineGainStep);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.PageUp:
                        AdjustGain(+CoarseGainStep);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.PageDown:
                        AdjustGain(-CoarseGainStep);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Enter:
                        ApplyButton_Click(null, null);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Escape:
                        BackButton_Click(null, null);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.R:
                        ResetButton_Click(null, null);
                        e.Handled = true;
                        break;
                }
            }
        }

        #endregion

        #region Button Handlers

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseDialog();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _playbackService?.Stop();
            ShowFileSelectionStep();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _currentGainDb = 0f;
            UpdateGainDisplay();
            UpdateFeedback("Amplify", "Gain reset to 0 dB");
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_waveformData == null) return;

            try
            {
                LogDebug($"Preview with gain: {_currentGainDb:+0.0;-0.0;0}dB");

                // Get user's configured volume setting (0-100) and convert to 0-1 range
                var settings = _getSettings?.Invoke();
                var userVolume = (settings?.MusicVolume ?? 100) / 100.0;

                // Calculate gain multiplier from dB
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
                    UpdateFeedback("Amplify", $"Preview at max (full {_currentGainDb:+0.0}dB on Apply)");
                }
                else
                {
                    UpdateFeedback("Amplify", $"Preview at {_currentGainDb:+0.0;-0.0;0}dB (your volume)");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error previewing");
                UpdateFeedback("Amplify", "Error playing preview");
            }
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_waveformData == null || Math.Abs(_currentGainDb) < 0.1f) return;

            var fileName = IOPath.GetFileName(_selectedFilePath);

            LogDebug($"ApplyButton_Click: file={fileName}, gain={_currentGainDb:+0.0;-0.0;0}dB");

            var clippingWarning = "";
            if (_waveformData.WouldClip(_currentGainDb))
            {
                clippingWarning = "\n\nWARNING: This will cause clipping!";
            }

            var result = _playniteApi.Dialogs.ShowMessage(
                $"Apply {_currentGainDb:+0.0;-0.0;0}dB gain to '{fileName}'?{clippingWarning}\n\n" +
                $"Original will be moved to PreservedOriginals.",
                "Confirm Amplify",
                MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                ApplyButton.IsEnabled = false;
                UpdateFeedback("Amplify", "Applying amplification...");

                _playbackService?.Stop();

                var settings = _getSettings?.Invoke();
                var ffmpegPath = settings?.FFmpegPath;

                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg path is not configured.",
                        "FFmpeg Not Found");
                    return;
                }

                var suffix = _currentGainDb >= 0 ? "-amplified" : "-attenuated";

                var success = await _amplifyService.ApplyAmplifyAsync(_selectedFilePath, _currentGainDb, suffix, ffmpegPath);

                if (success)
                {
                    _playniteApi.Dialogs.ShowMessage(
                        $"Created amplified version with {_currentGainDb:+0.0;-0.0;0}dB gain.\n\n" +
                        $"Original file moved to PreservedOriginals.",
                        "Amplify Complete");

                    ShowFileSelectionStep();
                    LoadMusicFiles();
                }
                else
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"Failed to amplify '{fileName}'.",
                        "Amplify Failed");
                    UpdateFeedback("Amplify", "Amplification failed");
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
            }
        }

        private void CloseDialog()
        {
            _playbackService?.Stop();
            var window = Window.GetWindow(this);
            window?.Close();
        }

        #endregion

        #region Helpers

        private void UpdateFeedback(string step, string message)
        {
            if (step == "FileSelection")
            {
                FileSelectionFeedback.Text = $"🎮 {message}";
            }
            else
            {
                AmplifyFeedback.Text = $"🎮 {message}";
            }
        }

        #endregion
    }
}
