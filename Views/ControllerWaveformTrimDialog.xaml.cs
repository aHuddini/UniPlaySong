using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Models.WaveformTrim;
using UniPlaySong.Services;

namespace UniPlaySong.Views
{
    /// <summary>
    /// Controller-friendly waveform trim dialog with two-step interface:
    /// Step 1: File selection
    /// Step 2: Waveform-based trim editing
    /// </summary>
    public partial class ControllerWaveformTrimDialog : UserControl
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "PreciseTrim:Controller";

        // Controller monitoring
        private CancellationTokenSource _controllerMonitoringCancellation;
        private bool _isMonitoring = false;
        private ushort _lastButtonState = 0;
        private byte _lastLeftTrigger = 0;
        private byte _lastRightTrigger = 0;

        // D-pad debouncing for file selection (300ms for reliable single-item navigation)
        private DateTime _lastDpadNavigationTime = DateTime.MinValue;
        private const int DpadDebounceMs = 300;

        // Continuous D-pad input tracking for waveform editor
        private ushort _heldDpadDirection = 0;
        private DateTime _dpadHoldStartTime = DateTime.MinValue;
        private DateTime _lastDpadRepeatTime = DateTime.MinValue;
        private const int InitialRepeatDelayMs = 200;  // Initial delay before repeat starts
        private const int FastRepeatIntervalMs = 50;   // Fast repeat interval when holding

        // State
        private Game _currentGame;
        private IPlayniteAPI _playniteApi;
        private GameMusicFileService _fileService;
        private IMusicPlaybackService _playbackService;
        private IWaveformTrimService _waveformTrimService;
        private Func<UniPlaySongSettings> _getSettings;
        private List<string> _musicFiles;
        private string _selectedFilePath;

        // Waveform data
        private WaveformData _waveformData;
        private TrimWindow _trimWindow;
        private CancellationTokenSource _loadingCancellation;

        // Preview
        private DispatcherTimer _previewStopTimer;
        private DispatcherTimer _playheadTimer;
        private DateTime _previewStartTime;
        private bool _isPreviewing = false;

        // Flag to block input during modal dialogs
        private volatile bool _isShowingModalDialog = false;

        // Cooldown timestamp - blocks ALL button processing for a period after modal dialogs close
        // This prevents the race condition where a button press that closed the modal is
        // detected as a "new press" by the background polling loop
        private DateTime _modalCooldownUntil = DateTime.MinValue;
        private const int ModalCooldownMs = 350; // Block input for 350ms after modal closes

        // Current step
        private enum DialogStep { FileSelection, WaveformEditor }
        private DialogStep _currentStep = DialogStep.FileSelection;

        public ControllerWaveformTrimDialog()
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
                StopPreview();
                _loadingCancellation?.Cancel();
            };

            // Use PreviewKeyDown to intercept keyboard events BEFORE they reach child controls
            // This prevents WPF's ListBox from also processing arrow keys alongside our handler
            PreviewKeyDown += OnKeyDown;
        }

        /// <summary>
        /// Initialize the dialog for a specific game
        /// </summary>
        public void InitializeForGame(
            Game game,
            IPlayniteAPI playniteApi,
            GameMusicFileService fileService,
            IMusicPlaybackService playbackService,
            IWaveformTrimService waveformTrimService,
            Func<UniPlaySongSettings> getSettings)
        {
            try
            {
                _currentGame = game;
                _playniteApi = playniteApi;
                _fileService = fileService;
                _playbackService = playbackService;
                _waveformTrimService = waveformTrimService;
                _getSettings = getSettings;

                Logger.DebugIf(LogPrefix,$"Initialized controller waveform trim for game: {game?.Name}");

                // Start at file selection
                ShowFileSelectionStep();
                LoadMusicFiles();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing controller waveform trim dialog");
                UpdateFeedback("FileSelection", "Error initializing dialog");
            }
        }

        #region Step Navigation

        private void ShowFileSelectionStep()
        {
            _currentStep = DialogStep.FileSelection;
            FileSelectionStep.Visibility = Visibility.Visible;
            WaveformEditorStep.Visibility = Visibility.Collapsed;
            FilesListBox.Focus();
        }

        private void ShowWaveformEditorStep()
        {
            _currentStep = DialogStep.WaveformEditor;
            FileSelectionStep.Visibility = Visibility.Collapsed;
            WaveformEditorStep.Visibility = Visibility.Visible;
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
            var settings = _getSettings?.Invoke();
            var suffix = settings?.PreciseTrimSuffix ?? "-ptrimmed";

            foreach (var filePath in _musicFiles)
            {
                var listItem = new ListBoxItem();
                var fileName = Path.GetFileName(filePath);

                var stackPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // Already trimmed indicator
                if (_waveformTrimService?.IsAlreadyTrimmed(filePath, suffix) == true)
                {
                    var trimmedIcon = new TextBlock
                    {
                        Text = "[Trimmed] ",
                        FontSize = 13,
                        Foreground = Brushes.Orange,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    stackPanel.Children.Add(trimmedIcon);
                }

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
                CurrentFileText.Text = Path.GetFileName(filePath);
                ShowWaveformEditorStep();
                LoadWaveformAsync(filePath);
            }
            else
            {
                UpdateFeedback("FileSelection", "Please select a file");
            }
        }

        #endregion

        #region Waveform Editor (Step 2)

        private async void LoadWaveformAsync(string filePath)
        {
            try
            {
                _loadingCancellation?.Cancel();
                _loadingCancellation = new CancellationTokenSource();
                var token = _loadingCancellation.Token;

                LoadingOverlay.Visibility = Visibility.Visible;
                UpdateFeedback("Waveform", "Loading waveform...");

                await Task.Run(async () =>
                {
                    try
                    {
                        _waveformData = await _waveformTrimService.GenerateWaveformAsync(filePath, token);

                        if (token.IsCancellationRequested) return;

                        // Discard suppresses CS4014 warning - we don't need to await UI dispatch
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (_waveformData?.IsValid == true)
                                {
                                    _trimWindow = TrimWindow.FullDuration(_waveformData.Duration);
                                    RenderWaveform();
                                    UpdateTimeDisplay();
                                    LoadingOverlay.Visibility = Visibility.Collapsed;
                                    UpdateFeedback("Waveform", "Use D-Pad to adjust trim window");
                                }
                                else
                                {
                                    UpdateFeedback("Waveform", "Failed to load waveform data");
                                    LoadingOverlay.Visibility = Visibility.Collapsed;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, "Error displaying waveform");
                                UpdateFeedback("Waveform", "Error displaying waveform");
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
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            UpdateFeedback("Waveform", $"Error: {ex.Message}");
                            LoadingOverlay.Visibility = Visibility.Collapsed;
                        }));
                    }
                }, token);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting waveform load");
                UpdateFeedback("Waveform", "Failed to start loading");
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

            // Build waveform points
            var points = new PointCollection();
            var samples = _waveformData.Samples;
            var samplesPerPixel = Math.Max(1, samples.Length / (int)width);

            for (int x = 0; x < (int)width; x++)
            {
                int sampleIndex = (int)((double)x / width * samples.Length);
                sampleIndex = Math.Min(sampleIndex, samples.Length - 1);

                float sample = samples[sampleIndex];
                double y = centerY - (sample * centerY * 0.9);
                points.Add(new Point(x, y));
            }

            WaveformLine.Points = points;

            UpdateTrimWindowVisuals();
        }

        private void UpdateTrimWindowVisuals()
        {
            if (_trimWindow == null || WaveformCanvas.ActualWidth <= 0)
                return;

            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;

            double startX = width * _trimWindow.StartPercent / 100;
            double endX = width * _trimWindow.EndPercent / 100;

            // Excluded left region
            Canvas.SetLeft(ExcludedLeft, 0);
            Canvas.SetTop(ExcludedLeft, 0);
            ExcludedLeft.Width = Math.Max(0, startX);
            ExcludedLeft.Height = height;

            // Excluded right region
            Canvas.SetLeft(ExcludedRight, endX);
            Canvas.SetTop(ExcludedRight, 0);
            ExcludedRight.Width = Math.Max(0, width - endX);
            ExcludedRight.Height = height;

            // Trim window rectangle
            Canvas.SetLeft(TrimWindowRect, startX);
            Canvas.SetTop(TrimWindowRect, 0);
            TrimWindowRect.Width = Math.Max(0, endX - startX);
            TrimWindowRect.Height = height;

            // Start marker
            Canvas.SetLeft(StartMarker, startX - 3);
            Canvas.SetTop(StartMarker, 0);
            StartMarker.Height = height;

            // End marker
            Canvas.SetLeft(EndMarker, endX - 3);
            Canvas.SetTop(EndMarker, 0);
            EndMarker.Height = height;
        }

        private void UpdateTimeDisplay()
        {
            if (_trimWindow == null) return;

            StartTimeText.Text = FormatTime(_trimWindow.StartTime);
            EndTimeText.Text = FormatTime(_trimWindow.EndTime);
            DurationText.Text = FormatTime(_trimWindow.Duration);
            TotalDurationText.Text = $"of {FormatTime(_trimWindow.TotalDuration)}";
        }

        private string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
                return time.ToString(@"h\:mm\:ss\.f");
            return time.ToString(@"m\:ss\.f");
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

            // Initialize _lastButtonState with current controller state to prevent
            // detecting held buttons from previous dialogs as new presses
            try
            {
                XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                if (XInputWrapper.XInputGetState(0, ref state) == 0)
                {
                    _lastButtonState = state.Gamepad.wButtons;
                    _lastLeftTrigger = state.Gamepad.bLeftTrigger;
                    _lastRightTrigger = state.Gamepad.bRightTrigger;
                    Logger.DebugIf(LogPrefix, $"Initialized controller state: buttons={state.Gamepad.wButtons}");
                }
            }
            catch { /* Ignore initialization errors */ }

            _ = Task.Run(async () =>
            {
                // Small delay to let any held buttons be released
                await Task.Delay(150);
                await CheckButtonPresses(_controllerMonitoringCancellation.Token);
            });
            Logger.DebugIf(LogPrefix,"Started controller monitoring for waveform trim dialog");
        }

        private void StopControllerMonitoring()
        {
            if (!_isMonitoring) return;

            _isMonitoring = false;
            _controllerMonitoringCancellation?.Cancel();
            Logger.DebugIf(LogPrefix,"Stopped controller monitoring for waveform trim dialog");
        }

        /// <summary>
        /// Refresh controller state and activate cooldown to prevent stale button states from triggering actions.
        /// Call this after modal dialogs close to re-sync the controller state and block immediate re-detection.
        /// </summary>
        private void RefreshControllerStateWithCooldown()
        {
            try
            {
                // First, refresh the stored button state to current
                XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                if (XInputWrapper.XInputGetState(0, ref state) == 0)
                {
                    _lastButtonState = state.Gamepad.wButtons;
                    _lastLeftTrigger = state.Gamepad.bLeftTrigger;
                    _lastRightTrigger = state.Gamepad.bRightTrigger;
                    Logger.DebugIf(LogPrefix, $"Refreshed controller state: buttons={state.Gamepad.wButtons}");
                }

                // Set cooldown to block any input processing for ModalCooldownMs
                // This is the key defense against race conditions
                _modalCooldownUntil = DateTime.Now.AddMilliseconds(ModalCooldownMs);
                Logger.DebugIf(LogPrefix, $"Modal cooldown active until {_modalCooldownUntil:HH:mm:ss.fff}");
            }
            catch (Exception ex)
            {
                Logger.DebugIf(LogPrefix, $"Error refreshing controller state: {ex.Message}");
                // Still set cooldown even on error
                _modalCooldownUntil = DateTime.Now.AddMilliseconds(ModalCooldownMs);
            }
        }

        private async Task CheckButtonPresses(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                    uint result = (uint)XInputWrapper.XInputGetState(0, ref state);

                    if (result == 0) // Success
                    {
                        ushort currentButtons = state.Gamepad.wButtons;
                        ushort pressedButtons = (ushort)(currentButtons & ~_lastButtonState);

                        // Handle newly pressed buttons (edge detection)
                        if (pressedButtons != 0)
                        {
                            // Discard suppresses CS4014 warning - we don't need to await UI dispatch
                            _ = Dispatcher.BeginInvoke(new Action(() =>
                            {
                                HandleControllerInput(pressedButtons, state.Gamepad);
                            }));
                        }

                        // In waveform editor mode, also check for held D-pad for continuous movement
                        if (_currentStep == DialogStep.WaveformEditor)
                        {
                            _ = Dispatcher.BeginInvoke(new Action(() =>
                            {
                                HandleDpadContinuousInput(state.Gamepad);
                            }));
                            HandleTriggerInput(state.Gamepad);
                        }

                        _lastButtonState = currentButtons;
                        _lastLeftTrigger = state.Gamepad.bLeftTrigger;
                        _lastRightTrigger = state.Gamepad.bRightTrigger;
                    }

                    await Task.Delay(33, cancellationToken); // Check every 33ms (~30Hz) for smoother polling
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.DebugIf(LogPrefix,$"Error in controller monitoring: {ex.Message}");
                    await Task.Delay(100, cancellationToken);
                }
            }
        }

        private void HandleControllerInput(ushort pressedButtons, XInputWrapper.XINPUT_GAMEPAD gamepad)
        {
            try
            {
                // Block input if a modal dialog is being shown
                if (_isShowingModalDialog)
                {
                    return;
                }

                // Block input during cooldown period after modal dialogs close
                // This prevents the button press that closed the modal from being detected as a new press
                if (DateTime.Now < _modalCooldownUntil)
                {
                    Logger.DebugIf(LogPrefix, "Ignoring input - modal cooldown active");
                    return;
                }

                if (_currentStep == DialogStep.FileSelection)
                {
                    HandleFileSelectionInput(pressedButtons, gamepad);
                }
                else
                {
                    HandleWaveformEditorInput(pressedButtons, gamepad);
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
                CancelButton_Click(null, null);
                return;
            }

            // D-Pad navigation
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_DPAD_UP) != 0)
            {
                if (TryDpadNavigation())
                    NavigateList(-1);
            }
            else if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_DPAD_DOWN) != 0)
            {
                if (TryDpadNavigation())
                    NavigateList(1);
            }

            // Shoulder buttons - Page navigation
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0)
            {
                NavigateList(-5);
            }
            else if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0)
            {
                NavigateList(5);
            }
        }

        // Fixed time increment for marker movement (simple, consistent 0.5 second steps)
        private static readonly TimeSpan MarkerIncrement = TimeSpan.FromMilliseconds(500);
        // Size adjustment increment for LB/RB
        private static readonly TimeSpan SizeIncrement = TimeSpan.FromMilliseconds(500);

        private void HandleWaveformEditorInput(ushort pressedButtons, XInputWrapper.XINPUT_GAMEPAD gamepad)
        {
            if (_trimWindow == null) return;

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

            // X button - Apply trim
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_X) != 0)
            {
                ApplyTrimButton_Click(null, null);
                return;
            }

            // Y button - Reset
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_Y) != 0)
            {
                ResetButton_Click(null, null);
                return;
            }

            // LB - Contract window (move both markers inward)
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0)
            {
                _trimWindow.AdjustSizeByTime(-SizeIncrement);
                UpdateTrimWindowVisuals();
                UpdateTimeDisplay();
                UpdateFeedback("Waveform", "Window contracted");
            }

            // RB - Expand window (move both markers outward)
            if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0)
            {
                _trimWindow.AdjustSizeByTime(SizeIncrement);
                UpdateTrimWindowVisuals();
                UpdateTimeDisplay();
                UpdateFeedback("Waveform", "Window expanded");
            }

            // Note: D-pad continuous input is handled separately in CheckButtonPresses
            // to properly track held state vs newly pressed state
        }

        /// <summary>
        /// Handles D-pad input with continuous movement when held.
        /// D-Pad Left/Right: Move start marker (blue)
        /// D-Pad Up/Down: Move end marker (red)
        /// </summary>
        private void HandleDpadContinuousInput(XInputWrapper.XINPUT_GAMEPAD gamepad)
        {
            var now = DateTime.Now;
            ushort currentDpad = (ushort)(gamepad.wButtons & (
                XInputWrapper.XINPUT_GAMEPAD_DPAD_LEFT |
                XInputWrapper.XINPUT_GAMEPAD_DPAD_RIGHT |
                XInputWrapper.XINPUT_GAMEPAD_DPAD_UP |
                XInputWrapper.XINPUT_GAMEPAD_DPAD_DOWN));

            // Check if D-pad direction changed
            if (currentDpad != _heldDpadDirection)
            {
                _heldDpadDirection = currentDpad;
                if (currentDpad != 0)
                {
                    // New direction pressed - apply immediately and start hold tracking
                    _dpadHoldStartTime = now;
                    _lastDpadRepeatTime = now;
                    ApplyDpadAction(currentDpad);
                }
                return;
            }

            // If D-pad is held, check for continuous repeat
            if (currentDpad != 0)
            {
                var holdDuration = (now - _dpadHoldStartTime).TotalMilliseconds;
                var timeSinceLastRepeat = (now - _lastDpadRepeatTime).TotalMilliseconds;

                // After initial delay, repeat at fast interval
                if (holdDuration > InitialRepeatDelayMs && timeSinceLastRepeat >= FastRepeatIntervalMs)
                {
                    _lastDpadRepeatTime = now;
                    ApplyDpadAction(currentDpad);
                }
            }
        }

        /// <summary>
        /// Apply the marker adjustment based on D-pad direction.
        /// </summary>
        private void ApplyDpadAction(ushort dpadDirection)
        {
            if (_trimWindow == null) return;

            // D-Pad Left - Move start marker earlier
            if ((dpadDirection & XInputWrapper.XINPUT_GAMEPAD_DPAD_LEFT) != 0)
            {
                _trimWindow.AdjustStartByTime(-MarkerIncrement);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateTrimWindowVisuals();
                    UpdateTimeDisplay();
                    UpdateFeedback("Waveform", $"Start: {FormatTime(_trimWindow.StartTime)}");
                }));
            }
            // D-Pad Right - Move start marker later
            else if ((dpadDirection & XInputWrapper.XINPUT_GAMEPAD_DPAD_RIGHT) != 0)
            {
                _trimWindow.AdjustStartByTime(MarkerIncrement);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateTrimWindowVisuals();
                    UpdateTimeDisplay();
                    UpdateFeedback("Waveform", $"Start: {FormatTime(_trimWindow.StartTime)}");
                }));
            }
            // D-Pad Up - Move end marker later (extend)
            else if ((dpadDirection & XInputWrapper.XINPUT_GAMEPAD_DPAD_UP) != 0)
            {
                _trimWindow.AdjustEndByTime(MarkerIncrement);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateTrimWindowVisuals();
                    UpdateTimeDisplay();
                    UpdateFeedback("Waveform", $"End: {FormatTime(_trimWindow.EndTime)}");
                }));
            }
            // D-Pad Down - Move end marker earlier (shorten)
            else if ((dpadDirection & XInputWrapper.XINPUT_GAMEPAD_DPAD_DOWN) != 0)
            {
                _trimWindow.AdjustEndByTime(-MarkerIncrement);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateTrimWindowVisuals();
                    UpdateTimeDisplay();
                    UpdateFeedback("Waveform", $"End: {FormatTime(_trimWindow.EndTime)}");
                }));
            }
        }

        private void HandleTriggerInput(XInputWrapper.XINPUT_GAMEPAD gamepad)
        {
            // Triggers not used in waveform editor mode currently
            // Could be used for fine-tuning in the future if needed
        }

        private bool TryDpadNavigation()
        {
            var now = DateTime.Now;
            var timeSinceLastNav = (now - _lastDpadNavigationTime).TotalMilliseconds;
            if (timeSinceLastNav < DpadDebounceMs)
            {
                return false;
            }
            _lastDpadNavigationTime = now;
            return true;
        }

        private void NavigateList(int offset)
        {
            if (FilesListBox.Items.Count == 0) return;

            int newIndex = Math.Max(0, Math.Min(FilesListBox.Items.Count - 1, FilesListBox.SelectedIndex + offset));
            FilesListBox.SelectedIndex = newIndex;
            FilesListBox.ScrollIntoView(FilesListBox.SelectedItem);
        }

        #endregion

        #region Preview

        private void StartPreview()
        {
            if (_trimWindow == null || !_trimWindow.IsValid || string.IsNullOrEmpty(_selectedFilePath))
            {
                UpdateFeedback("Waveform", "Cannot preview - invalid selection");
                return;
            }

            try
            {
                StopPreview();

                // Load and play the file from the start marker position
                Logger.DebugIf(LogPrefix,$"Starting preview from {_trimWindow.StartTime:mm\\:ss\\.fff}");
                _playbackService.LoadAndPlayFileFrom(_selectedFilePath, _trimWindow.StartTime);

                _isPreviewing = true;
                _previewStartTime = DateTime.Now;

                // Show playhead
                Playhead.Visibility = Visibility.Visible;
                UpdatePlayheadPosition();

                // Set up stop timer
                _previewStopTimer = new DispatcherTimer
                {
                    Interval = _trimWindow.Duration
                };
                _previewStopTimer.Tick += (s, e) =>
                {
                    StopPreview();
                    UpdateFeedback("Waveform", "Preview ended - A to play again");
                };
                _previewStopTimer.Start();

                // Set up playhead animation
                _playheadTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(50)
                };
                _playheadTimer.Tick += (s, e) => UpdatePlayheadPosition();
                _playheadTimer.Start();

                UpdateFeedback("Waveform", $"Playing preview ({FormatTime(_trimWindow.Duration)})");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting preview");
                UpdateFeedback("Waveform", $"Preview error: {ex.Message}");
            }
        }

        private void StopPreview()
        {
            try
            {
                _previewStopTimer?.Stop();
                _previewStopTimer = null;

                _playheadTimer?.Stop();
                _playheadTimer = null;

                if (_isPreviewing)
                {
                    _playbackService?.Stop();
                    _isPreviewing = false;
                }

                Playhead.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error stopping preview");
            }
        }

        private void UpdatePlayheadPosition()
        {
            if (!_isPreviewing || _trimWindow == null || WaveformCanvas.ActualWidth <= 0)
                return;

            var elapsed = DateTime.Now - _previewStartTime;
            var currentTime = _trimWindow.StartTime + elapsed;

            if (currentTime > _trimWindow.EndTime)
                currentTime = _trimWindow.EndTime;

            var percent = currentTime.TotalMilliseconds / _trimWindow.TotalDuration.TotalMilliseconds * 100;
            var x = WaveformCanvas.ActualWidth * percent / 100;

            Playhead.X1 = x;
            Playhead.X2 = x;
            Playhead.Y1 = 0;
            Playhead.Y2 = WaveformCanvas.ActualHeight;
        }

        #endregion

        #region Apply Trim

        private async void ApplyTrim()
        {
            if (_trimWindow == null || !_trimWindow.IsValid || string.IsNullOrEmpty(_selectedFilePath))
            {
                UpdateFeedback("Waveform", "Cannot apply - invalid selection");
                return;
            }

            try
            {
                StopPreview();

                var settings = _getSettings?.Invoke();
                var suffix = settings?.PreciseTrimSuffix ?? "-ptrimmed";

                // Validate FFmpeg
                var ffmpegPath = settings?.FFmpegPath;
                if (!_waveformTrimService.ValidateFFmpegAvailable(ffmpegPath))
                {
                    DialogHelper.ShowErrorToast(
                        _playniteApi,
                        "FFmpeg is required for trimming. Please configure FFmpeg in Settings.",
                        "FFmpeg Required");
                    UpdateFeedback("Waveform", "FFmpeg not configured - check Settings");
                    return;
                }

                UpdateFeedback("Waveform", "Applying trim...");
                ApplyTrimButton.IsEnabled = false;

                // Pass FFmpeg path directly instead of relying on reflection
                Logger.DebugIf(LogPrefix,$"Applying trim with FFmpeg path: {ffmpegPath}");
                var success = await _waveformTrimService.ApplyTrimAsync(
                    _selectedFilePath, _trimWindow, suffix, ffmpegPath, CancellationToken.None);

                if (success)
                {
                    var fileName = Path.GetFileName(_selectedFilePath);

                    // Use auto-closing toast popup instead of modal dialog
                    DialogHelper.ShowSuccessToast(
                        _playniteApi,
                        $"Successfully trimmed: {fileName}\nOriginal preserved.",
                        "Trim Complete");

                    // Resume music playback with the newly trimmed file
                    if (_currentGame != null)
                    {
                        _playbackService?.PlayGameMusic(_currentGame, settings, forceReload: true);
                    }

                    // Return to file selection step for intuitive workflow
                    // (user can continue editing other songs without reopening dialog)
                    ReturnToFileSelectionAfterSuccess();
                }
                else
                {
                    DialogHelper.ShowErrorToast(_playniteApi, "Trim operation failed.", "Trim Failed");
                    UpdateFeedback("Waveform", "Trim failed");
                    ApplyTrimButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error applying trim");
                UpdateFeedback("Waveform", $"Error: {ex.Message}");
                ApplyTrimButton.IsEnabled = true;
            }
        }

        #endregion

        #region UI Helpers

        private void UpdateFeedback(string step, string message)
        {
            if (step == "FileSelection" && FileSelectionFeedback != null)
            {
                FileSelectionFeedback.Text = message;
            }
            else if (step == "Waveform" && WaveformFeedback != null)
            {
                WaveformFeedback.Text = message;
            }
        }

        #endregion

        #region Event Handlers

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Block input if a modal dialog is being shown
            if (_isShowingModalDialog)
            {
                e.Handled = true;
                return;
            }

            if (_currentStep == DialogStep.FileSelection)
            {
                switch (e.Key)
                {
                    // D-pad Up/Down mapped to arrow keys - use debounce to prevent double-input
                    case System.Windows.Input.Key.Up:
                        if (TryDpadNavigation())
                            NavigateList(-1);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Down:
                        if (TryDpadNavigation())
                            NavigateList(1);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Enter:
                        SelectFileAndProceed();
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Escape:
                        CancelButton_Click(null, null);
                        e.Handled = true;
                        break;
                }
            }
            else
            {
                // In waveform editor mode, D-pad adjusts markers - handled by XInput continuous input
                // Arrow keys here just mark as handled to prevent WPF default behavior
                switch (e.Key)
                {
                    case System.Windows.Input.Key.Up:
                    case System.Windows.Input.Key.Down:
                    case System.Windows.Input.Key.Left:
                    case System.Windows.Input.Key.Right:
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Space:
                        PreviewButton_Click(null, null);
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Enter:
                        ApplyTrimButton_Click(null, null);
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            StopPreview();
            CloseDialog(false);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            StopPreview();
            _waveformData = null;
            _trimWindow = null;
            _selectedFilePath = null;
            WaveformLine.Points?.Clear();
            ShowFileSelectionStep();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_waveformData?.IsValid == true)
            {
                _trimWindow?.Reset();
                UpdateTrimWindowVisuals();
                UpdateTimeDisplay();
                UpdateFeedback("Waveform", "Reset to full duration");
            }
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPreviewing)
            {
                StopPreview();
                UpdateFeedback("Waveform", "Preview stopped");
            }
            else
            {
                StartPreview();
            }
        }

        private void ApplyTrimButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyTrim();
        }

        #endregion

        private void CloseDialog(bool success, bool skipStopPreview = false)
        {
            try
            {
                // Only stop preview if not skipping (e.g., when we're about to start new music)
                if (!skipStopPreview)
                {
                    StopPreview();
                }
                else
                {
                    // Still stop timers but don't stop playback service
                    _previewStopTimer?.Stop();
                    _previewStopTimer = null;
                    _playheadTimer?.Stop();
                    _playheadTimer = null;
                    _isPreviewing = false;
                    Playhead.Visibility = Visibility.Collapsed;
                }

                StopControllerMonitoring();
                _loadingCancellation?.Cancel();

                var window = Window.GetWindow(this);
                if (window != null)
                {
                    try
                    {
                        var mainWindow = _playniteApi?.Dialogs?.GetCurrentAppWindow();
                        if (mainWindow != null)
                        {
                            mainWindow.Activate();
                            mainWindow.Focus();
                            mainWindow.Topmost = true;
                            mainWindow.Topmost = false;
                        }
                    }
                    catch (Exception focusEx)
                    {
                        Logger.DebugIf(LogPrefix,$"Error returning focus: {focusEx.Message}");
                    }

                    window.DialogResult = success;
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error closing dialog");
            }
        }

        /// <summary>
        /// Returns to the file selection step after a successful operation.
        /// Resets the trim state and reloads the file list so the user can
        /// continue editing other songs without reopening the dialog.
        /// </summary>
        private void ReturnToFileSelectionAfterSuccess()
        {
            try
            {
                // Stop any preview
                StopPreview();

                // Reset trim state
                _waveformData = null;
                _trimWindow = null;
                _selectedFilePath = null;

                // Clear waveform display
                WaveformLine.Points?.Clear();

                // Cancel any pending waveform loading
                _loadingCancellation?.Cancel();

                // Return to file selection
                ShowFileSelectionStep();

                // Reload file list to reflect any changes (new trimmed files, updated labels)
                LoadMusicFiles();

                UpdateFeedback("FileSelection", "Select another file to edit, or press B to exit");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error returning to file selection");
                UpdateFeedback("FileSelection", "Ready for selection");
            }
        }
    }
}
