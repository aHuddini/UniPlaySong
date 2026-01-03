using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Views
{
    /// <summary>
    /// Controller-friendly file picker dialog for setting/removing primary songs
    /// </summary>
    public partial class ControllerFilePickerDialog : UserControl
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        /// <summary>
        /// Logs a debug message only if debug logging is enabled in settings.
        /// </summary>
        private static void LogDebug(string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                Logger.Debug(message);
            }
        }

        /// <summary>
        /// Logs a debug message with exception only if debug logging is enabled.
        /// </summary>
        private static void LogDebug(Exception ex, string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                Logger.Debug(ex, message);
            }
        }

        // Controller monitoring
        private CancellationTokenSource _controllerMonitoringCancellation;
        private bool _isMonitoring = false;
        private ushort _lastButtonState = 0;

        // D-pad debouncing - prevents double-input from both XInput and WPF processing
        private DateTime _lastDpadNavigationTime = DateTime.MinValue;
        private const int DpadDebounceMs = 150; // Minimum ms between D-pad navigations

        // Dialog state
        private Game _currentGame;
        private IPlayniteAPI _playniteApi;
        private GameMusicFileService _fileService;
        private IMusicPlaybackService _playbackService;
        private List<string> _musicFiles;
        private string _currentPrimarySong;
        private bool _wasGameMusicPlaying = false;

        // Preview functionality
        private string _currentlyPreviewing = null;
        private System.Windows.Media.MediaPlayer _previewPlayer;

        public enum DialogMode
        {
            SetPrimary,
            RemovePrimary,
            NormalizeIndividual,
            TrimIndividual,
            RepairIndividual
        }

        private DialogMode _mode;

        public ControllerFilePickerDialog()
        {
            InitializeComponent();
            
            // Focus on the list box when loaded
            Loaded += (s, e) => 
            {
                FilesListBox.Focus();
                StartControllerMonitoring();
            };
            
            Unloaded += (s, e) => 
            {
                StopControllerMonitoring();
                StopCurrentPreview();
            };

            // Handle keyboard input as fallback
            KeyDown += OnKeyDown;
        }

        /// <summary>
        /// Initialize the dialog for a specific game and mode
        /// </summary>
        public void InitializeForGame(Game game, IPlayniteAPI playniteApi, GameMusicFileService fileService, IMusicPlaybackService playbackService, DialogMode mode)
        {
            try
            {
                _currentGame = game;
                _playniteApi = playniteApi;
                _fileService = fileService;
                _playbackService = playbackService;
                _mode = mode;

                LogDebug($"Initialized file picker for game: {game?.Name}, mode: {mode}");

                // Update UI based on mode
                UpdateUIForMode();

                // Load music files
                LoadMusicFiles();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing file picker dialog");
                UpdateInputFeedback("❌ Failed to initialize dialog");
            }
        }

        /// <summary>
        /// Update UI elements based on dialog mode
        /// </summary>
        private void UpdateUIForMode()
        {
            switch (_mode)
            {
                case DialogMode.SetPrimary:
                    DialogTitle.Text = "🎮 Select Primary Song";
                    ConfirmButton.Content = "Set as Primary";
                    RemoveButton.Visibility = Visibility.Collapsed;
                    break;

                case DialogMode.RemovePrimary:
                    DialogTitle.Text = "🎮 Remove Primary Song";
                    ConfirmButton.Content = "Remove Primary";
                    RemoveButton.Visibility = Visibility.Visible;
                    RemoveButton.Content = "Remove Primary";
                    break;

                case DialogMode.NormalizeIndividual:
                    DialogTitle.Text = "🎮 Select Song to Normalize";
                    ConfirmButton.Content = "Normalize";
                    RemoveButton.Visibility = Visibility.Collapsed;
                    break;

                case DialogMode.TrimIndividual:
                    DialogTitle.Text = "🎮 Silence Trim - Select Song";
                    ConfirmButton.Content = "Silence Trim";
                    RemoveButton.Visibility = Visibility.Collapsed;
                    break;

                case DialogMode.RepairIndividual:
                    DialogTitle.Text = "🎮 Repair Audio File";
                    ConfirmButton.Content = "Repair";
                    RemoveButton.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        /// <summary>
        /// Load music files for the current game
        /// </summary>
        private void LoadMusicFiles()
        {
            try
            {
                UpdateInputFeedback("🔍 Loading music files...");

                Task.Run(() =>
                {
                    try
                    {
                        // Get available songs
                        _musicFiles = _fileService?.GetAvailableSongs(_currentGame) ?? new List<string>();
                        
                        // Get current primary song
                        _currentPrimarySong = _fileService?.GetPrimarySong(_currentGame);

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (_musicFiles.Count == 0)
                                {
                                    UpdateInputFeedback("❌ No music files found for this game");
                                    ShowNoFilesMessage();
                                    return;
                                }

                                // Show current primary song info
                                UpdateCurrentPrimaryDisplay();

                                // Populate file list
                                PopulateFilesList();

                                UpdateInputFeedback($"🎵 Found {_musicFiles.Count} music files - Use D-Pad to navigate");
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, "Error updating UI with music files");
                                UpdateInputFeedback("❌ Error loading files");
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error loading music files");
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            UpdateInputFeedback("❌ Error loading music files");
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initiating music file load");
                UpdateInputFeedback("❌ Failed to load files");
            }
        }

        /// <summary>
        /// Update the current primary song display
        /// </summary>
        private void UpdateCurrentPrimaryDisplay()
        {
            if (!string.IsNullOrEmpty(_currentPrimarySong))
            {
                CurrentPrimaryBorder.Visibility = Visibility.Visible;
                CurrentPrimaryText.Text = Path.GetFileName(_currentPrimarySong);
            }
            else
            {
                CurrentPrimaryBorder.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Populate the files list with music files
        /// </summary>
        private void PopulateFilesList()
        {
            FilesListBox.Items.Clear();

            foreach (var filePath in _musicFiles)
            {
                var listItem = new ListBoxItem();
                var fileName = Path.GetFileName(filePath);
                
                // Create content layout
                var stackPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // Primary song indicator
                if (filePath == _currentPrimarySong)
                {
                    var primaryIcon = new TextBlock
                    {
                        Text = "⭐ ",
                        FontSize = 16,
                        Foreground = System.Windows.Media.Brushes.Gold,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    stackPanel.Children.Add(primaryIcon);
                }

                // File name
                var nameBlock = new TextBlock
                {
                    Text = fileName,
                    FontSize = 15,
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

            // Select first item or current primary
            if (FilesListBox.Items.Count > 0)
            {
                var primaryIndex = _musicFiles.IndexOf(_currentPrimarySong);
                FilesListBox.SelectedIndex = primaryIndex >= 0 ? primaryIndex : 0;
                FilesListBox.Focus();
            }
        }

        /// <summary>
        /// Show message when no files are found
        /// </summary>
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

        /// <summary>
        /// Update input feedback text
        /// </summary>
        private void UpdateInputFeedback(string message)
        {
            if (InputFeedback != null)
            {
                InputFeedback.Text = message;
            }
        }

        #region Controller Support

        /// <summary>
        /// Start monitoring Xbox controller input
        /// </summary>
        private void StartControllerMonitoring()
        {
            if (_isMonitoring) return;

            _isMonitoring = true;
            _controllerMonitoringCancellation = new CancellationTokenSource();
            
            _ = Task.Run(() => CheckButtonPresses(_controllerMonitoringCancellation.Token));
            LogDebug("Started controller monitoring for file picker");
        }

        /// <summary>
        /// Stop monitoring Xbox controller input
        /// </summary>
        private void StopControllerMonitoring()
        {
            if (!_isMonitoring) return;

            _isMonitoring = false;
            _controllerMonitoringCancellation?.Cancel();
            LogDebug("Stopped controller monitoring for file picker");
        }

        /// <summary>
        /// Check for Xbox controller button presses
        /// </summary>
        private async Task CheckButtonPresses(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                    uint result = (uint)XInputWrapper.XInputGetState(0, ref state); // Check controller 0

                    if (result == 0) // Success
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

                        _lastButtonState = currentButtons;
                    }

                    await Task.Delay(50, cancellationToken); // Check every 50ms
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogDebug(ex, "Error in controller monitoring");
                    await Task.Delay(100, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Handle Xbox controller input
        /// </summary>
        private void HandleControllerInput(ushort pressedButtons, XInputWrapper.XINPUT_GAMEPAD gamepad)
        {
            try
            {
                // A button - Confirm selection
                if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_A) != 0)
                {
                    ConfirmButton_Click(null, null);
                    return;
                }

                // B button - Cancel
                if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_B) != 0)
                {
                    CancelButton_Click(null, null);
                    return;
                }

                // X/Y buttons - Preview
                if ((pressedButtons & (XInputWrapper.XINPUT_GAMEPAD_X | XInputWrapper.XINPUT_GAMEPAD_Y)) != 0)
                {
                    PreviewSelectedFile();
                    return;
                }

                // D-Pad navigation with debouncing
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
                    NavigateList(-5); // Page up
                }
                else if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0)
                {
                    NavigateList(5); // Page down
                }

                // Triggers - Jump to top/bottom
                if (gamepad.bLeftTrigger > 128)
                {
                    JumpToItem(0); // Top
                }
                else if (gamepad.bRightTrigger > 128)
                {
                    JumpToItem(FilesListBox.Items.Count - 1); // Bottom
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling controller input");
            }
        }

        /// <summary>
        /// Check if enough time has passed since last D-pad navigation (debouncing)
        /// </summary>
        private bool TryDpadNavigation()
        {
            var now = DateTime.Now;
            var timeSinceLastNav = (now - _lastDpadNavigationTime).TotalMilliseconds;
            if (timeSinceLastNav < DpadDebounceMs)
            {
                return false; // Too soon, ignore this input
            }
            _lastDpadNavigationTime = now;
            return true;
        }

        /// <summary>
        /// Navigate the list by a specified offset
        /// </summary>
        private void NavigateList(int offset)
        {
            if (FilesListBox.Items.Count == 0) return;

            int newIndex = Math.Max(0, Math.Min(FilesListBox.Items.Count - 1, FilesListBox.SelectedIndex + offset));
            FilesListBox.SelectedIndex = newIndex;
            FilesListBox.ScrollIntoView(FilesListBox.SelectedItem);
        }

        /// <summary>
        /// Jump to a specific item index
        /// </summary>
        private void JumpToItem(int index)
        {
            if (FilesListBox.Items.Count == 0) return;

            int targetIndex = Math.Max(0, Math.Min(FilesListBox.Items.Count - 1, index));
            FilesListBox.SelectedIndex = targetIndex;
            FilesListBox.ScrollIntoView(FilesListBox.SelectedItem);
        }

        #endregion

        #region Preview Support

        /// <summary>
        /// Preview the currently selected file
        /// </summary>
        private void PreviewSelectedFile()
        {
            try
            {
                var selectedItem = FilesListBox.SelectedItem as ListBoxItem;
                if (selectedItem?.Tag is string filePath)
                {
                    var fileName = Path.GetFileName(filePath);
                    
                    if (_currentlyPreviewing == filePath)
                    {
                        // Stop current preview
                        StopCurrentPreview();
                        UpdateInputFeedback($"🎮 Preview stopped - X/Y to preview {fileName}");
                    }
                    else
                    {
                        // Start new preview
                        PlayPreviewFile(filePath, fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error previewing file");
                UpdateInputFeedback("❌ Preview error");
            }
        }

        /// <summary>
        /// Play preview file
        /// </summary>
        private void PlayPreviewFile(string filePath, string fileName)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    UpdateInputFeedback("❌ File not found");
                    return;
                }

                // Stop any current preview
                StopCurrentPreview();

                // Pause game music for clear preview
                PauseGameMusicForPreview();

                // Play preview
                _currentlyPreviewing = filePath;
                _previewPlayer = new System.Windows.Media.MediaPlayer();
                
                _previewPlayer.MediaEnded += (s, e) => 
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateInputFeedback($"🎮 Preview ended - X/Y to play {fileName} again");
                        StopCurrentPreview();
                    }));
                };
                _previewPlayer.MediaFailed += (s, e) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateInputFeedback($"❌ Preview failed: {e.ErrorException?.Message ?? "Unknown error"}");
                        StopCurrentPreview();
                    }));
                };
                
                _previewPlayer.Volume = 0.7;
                _previewPlayer.Open(new Uri(filePath));
                _previewPlayer.Play();
                
                UpdateInputFeedback($"🔊 Playing preview: {fileName} - X/Y to stop (Game music paused)");
                LogDebug($"Started preview for: {fileName}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error playing preview");
                UpdateInputFeedback($"❌ Preview error: {ex.Message}");
                RestoreGameMusic();
            }
        }

        /// <summary>
        /// Stop current preview and restore game music
        /// </summary>
        private void StopCurrentPreview()
        {
            try
            {
                if (_previewPlayer != null)
                {
                    _previewPlayer.Stop();
                    _previewPlayer.Close();
                    _previewPlayer = null;
                }

                _currentlyPreviewing = null;
                RestoreGameMusic();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error stopping preview");
            }
        }

        /// <summary>
        /// Pause game music for preview
        /// </summary>
        private void PauseGameMusicForPreview()
        {
            try
            {
                if (_playbackService != null && _playbackService.IsPlaying)
                {
                    _wasGameMusicPlaying = true;
                    _playbackService.Pause();
                    LogDebug("Paused game music for preview");
                }
                else
                {
                    _wasGameMusicPlaying = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error pausing game music");
                _wasGameMusicPlaying = false;
            }
        }

        /// <summary>
        /// Restore game music after preview
        /// </summary>
        private void RestoreGameMusic()
        {
            try
            {
                if (_wasGameMusicPlaying && _playbackService != null)
                {
                    _playbackService.Resume();
                    _wasGameMusicPlaying = false;
                    LogDebug("Resumed game music after preview");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error resuming game music");
                _wasGameMusicPlaying = false;
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handle keyboard input as fallback
        /// </summary>
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    ConfirmButton_Click(null, null);
                    e.Handled = true;
                    break;
                    
                case Key.Escape:
                    CancelButton_Click(null, null);
                    e.Handled = true;
                    break;
                    
                case Key.F1:
                    PreviewSelectedFile();
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// Handle confirm button click
        /// </summary>
        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItem = FilesListBox.SelectedItem as ListBoxItem;
                if (selectedItem?.Tag is string selectedFilePath)
                {
                    switch (_mode)
                    {
                        case DialogMode.SetPrimary:
                            SetPrimarySong(selectedFilePath);
                            break;

                        case DialogMode.RemovePrimary:
                            RemovePrimarySong();
                            break;

                        case DialogMode.NormalizeIndividual:
                            NormalizeIndividualSong(selectedFilePath);
                            break;

                        case DialogMode.TrimIndividual:
                            TrimIndividualSong(selectedFilePath);
                            break;

                        case DialogMode.RepairIndividual:
                            RepairIndividualSong(selectedFilePath);
                            break;
                    }
                }
                else
                {
                    UpdateInputFeedback("❌ Please select a file");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in confirm action");
                UpdateInputFeedback("❌ Error processing selection");
            }
        }

        /// <summary>
        /// Handle cancel button click
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseDialog(false);
        }

        /// <summary>
        /// Handle remove button click
        /// </summary>
        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            RemovePrimarySong();
        }

        #endregion

        #region Primary Song Management

        /// <summary>
        /// Set the selected file as primary song
        /// </summary>
        private void SetPrimarySong(string filePath)
        {
            try
            {
                _fileService?.SetPrimarySong(_currentGame, filePath);
                
                var fileName = Path.GetFileName(filePath);
                _playniteApi?.Dialogs?.ShowMessage(
                    $"Primary song set to:\n{fileName}",
                    "UniPlaySong");
                
                Logger.Info($"Set primary song for {_currentGame?.Name}: {fileName}");
                CloseDialog(true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error setting primary song");
                UpdateInputFeedback("❌ Error setting primary song");
            }
        }

        /// <summary>
        /// Remove the current primary song
        /// </summary>
        private void RemovePrimarySong()
        {
            try
            {
                _fileService?.RemovePrimarySong(_currentGame);

                _playniteApi?.Dialogs?.ShowMessage(
                    "Primary song removed. The game will use randomized selection.",
                    "UniPlaySong");

                Logger.Info($"Removed primary song for {_currentGame?.Name}");
                CloseDialog(true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error removing primary song");
                UpdateInputFeedback("❌ Error removing primary song");
            }
        }

        /// <summary>
        /// Normalize the selected individual song
        /// </summary>
        private void NormalizeIndividualSong(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                Logger.Info($"Starting normalization for individual song: {fileName}");

                // Close dialog first, then trigger normalization
                CloseDialog(true);

                // Use dispatcher to show progress dialog after this dialog closes
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Get the UniPlaySong plugin instance to access normalization
                        var plugin = Application.Current?.Properties["UniPlaySongPlugin"] as UniPlaySong;
                        plugin?.NormalizeSingleFile(_currentGame, filePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error triggering normalization");
                        _playniteApi?.Dialogs?.ShowErrorMessage($"Error normalizing file: {ex.Message}", "UniPlaySong");
                    }
                }));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting normalization");
                UpdateInputFeedback("❌ Error starting normalization");
            }
        }

        /// <summary>
        /// Trim the selected individual song
        /// </summary>
        private void TrimIndividualSong(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                Logger.Info($"Starting trim for individual song: {fileName}");

                // Close dialog first, then trigger trim
                CloseDialog(true);

                // Use dispatcher to show progress dialog after this dialog closes
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Get the UniPlaySong plugin instance to access trim
                        var plugin = Application.Current?.Properties["UniPlaySongPlugin"] as UniPlaySong;
                        plugin?.TrimSingleFile(_currentGame, filePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error triggering trim");
                        _playniteApi?.Dialogs?.ShowErrorMessage($"Error trimming file: {ex.Message}", "UniPlaySong");
                    }
                }));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting trim");
                UpdateInputFeedback("❌ Error starting trim");
            }
        }

        /// <summary>
        /// Repair the selected individual song
        /// </summary>
        private void RepairIndividualSong(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                Logger.Info($"Starting repair for individual song: {fileName}");

                // Close dialog first, then trigger repair
                CloseDialog(true);

                // Use dispatcher to show progress dialog after this dialog closes
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Get the UniPlaySong plugin instance to access repair
                        var plugin = Application.Current?.Properties["UniPlaySongPlugin"] as UniPlaySong;
                        plugin?.RepairSingleFile(_currentGame, filePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error triggering repair");
                        _playniteApi?.Dialogs?.ShowErrorMessage($"Error repairing file: {ex.Message}", "UniPlaySong");
                    }
                }));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting repair");
                UpdateInputFeedback("❌ Error starting repair");
            }
        }

        #endregion

        /// <summary>
        /// Close the dialog with the specified result
        /// </summary>
        private void CloseDialog(bool success)
        {
            try
            {
                // Stop any preview and restore game music
                StopCurrentPreview();
                
                var window = Window.GetWindow(this);
                if (window != null)
                {
                    // Return focus to Playnite
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
                        LogDebug(focusEx, "Error returning focus to main window");
                    }
                    
                    window.DialogResult = success;
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error closing file picker dialog");
            }
        }
    }
}