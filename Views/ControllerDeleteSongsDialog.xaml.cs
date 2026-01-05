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
    /// Controller-friendly dialog for deleting songs from game music folder
    /// </summary>
    public partial class ControllerDeleteSongsDialog : UserControl
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "DeleteSongs";

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
        
        // Deletion state management
        private bool _isDeletionInProgress = false;
        private bool _isShowingConfirmation = false;

        public ControllerDeleteSongsDialog()
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
        /// Initialize the dialog for a specific game
        /// </summary>
        public void InitializeForGame(Game game, IPlayniteAPI playniteApi, GameMusicFileService fileService, IMusicPlaybackService playbackService)
        {
            try
            {
                _currentGame = game;
                _playniteApi = playniteApi;
                _fileService = fileService;
                _playbackService = playbackService;

                Logger.DebugIf(LogPrefix, $"Initialized delete songs dialog for game: {game?.Name}");

                // Load music files
                LoadMusicFiles();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing delete songs dialog");
                UpdateInputFeedback("❌ Failed to initialize dialog");
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

                                // Populate file list
                                PopulateFilesList();

                                UpdateInputFeedback($"🎵 Found {_musicFiles.Count} music files - Select songs to delete");
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
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = "This is the current primary song"
                    };
                    stackPanel.Children.Add(primaryIcon);
                }

                // Delete icon
                var deleteIcon = new TextBlock
                {
                    Text = "🗑️ ",
                    FontSize = 14,
                    Foreground = System.Windows.Media.Brushes.Red,
                    VerticalAlignment = VerticalAlignment.Center
                };
                stackPanel.Children.Add(deleteIcon);

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

                // File size info
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var sizeText = new TextBlock
                    {
                        Text = $" ({FormatFileSize(fileInfo.Length)})",
                        FontSize = 12,
                        Foreground = System.Windows.Media.Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    stackPanel.Children.Add(sizeText);
                }
                catch (Exception ex)
                {
                    Logger.DebugIf(LogPrefix, ex, $"Error getting file size for {filePath}");
                }

                listItem.Content = stackPanel;
                listItem.Tag = filePath;
                listItem.HorizontalContentAlignment = HorizontalAlignment.Stretch;

                FilesListBox.Items.Add(listItem);
            }

            // Select first item
            if (FilesListBox.Items.Count > 0)
            {
                FilesListBox.SelectedIndex = 0;
                FilesListBox.Focus();
            }
        }

        /// <summary>
        /// Format file size for display
        /// </summary>
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
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
            Logger.DebugIf(LogPrefix, "Started controller monitoring for delete songs dialog");
        }

        /// <summary>
        /// Stop monitoring Xbox controller input
        /// </summary>
        private void StopControllerMonitoring()
        {
            if (!_isMonitoring) return;

            _isMonitoring = false;
            _controllerMonitoringCancellation?.Cancel();
            Logger.DebugIf(LogPrefix, "Stopped controller monitoring for delete songs dialog");
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
                    Logger.DebugIf(LogPrefix, ex, "Error in controller monitoring");
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
                // Ignore input during deletion process or confirmation
                if (_isDeletionInProgress || _isShowingConfirmation)
                {
                    return;
                }

                // A button - Delete selected song
                if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_A) != 0)
                {
                    DeleteButton_Click(null, null);
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
                Logger.DebugIf(LogPrefix, $"Started preview for: {fileName}");
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
                    Logger.DebugIf(LogPrefix, "Paused game music for preview");
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
                    Logger.DebugIf(LogPrefix, "Resumed game music after preview");
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
                    DeleteButton_Click(null, null);
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
                    
                case Key.Delete:
                    DeleteButton_Click(null, null);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// Handle delete button click
        /// </summary>
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Prevent multiple simultaneous deletions or confirmations
                if (_isDeletionInProgress || _isShowingConfirmation)
                {
                    UpdateInputFeedback("⏳ Please wait...");
                    return;
                }

                var selectedItem = FilesListBox.SelectedItem as ListBoxItem;
                if (selectedItem?.Tag is string selectedFilePath)
                {
                    ConfirmAndDeleteSong(selectedFilePath);
                }
                else
                {
                    UpdateInputFeedback("❌ Please select a file to delete");
                ResetDeletionState();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in delete action");
                UpdateInputFeedback("❌ Error processing deletion");
                ResetDeletionState();
            }
        }

        /// <summary>
        /// Handle cancel button click
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseDialog(false);
        }

        #endregion

        #region Song Deletion

        /// <summary>
        /// Confirm and delete the selected song
        /// </summary>
        private void ConfirmAndDeleteSong(string filePath)
        {
            try
            {
                // Set flags to prevent multiple operations
                _isDeletionInProgress = true;
                _isShowingConfirmation = true;
                DeleteButton.IsEnabled = false;
                UpdateInputFeedback("⏳ Preparing deletion...");

                var fileName = Path.GetFileName(filePath);
                var isPrimarySong = filePath == _currentPrimarySong;
                
                // Verify file still exists
                if (!File.Exists(filePath))
                {
                    UpdateInputFeedback("❌ File no longer exists");
                    ResetDeletionState();
                    return;
                }

                // Stop any music that might be using this file
                StopAllMusicPlayback(fileName);
                
                // Build confirmation message
                var message = $"Are you sure you want to delete this song?\n\n" +
                             $"File: {fileName}\n" +
                             $"Path: {filePath}";
                
                if (isPrimarySong)
                {
                    message += "\n\n⚠️ WARNING: This is your current primary song!";
                }
                
                message += "\n\nThis action cannot be undone!";

                Logger.DebugIf(LogPrefix, $"Showing confirmation dialog for: {fileName}");

                // Show confirmation dialog
                var result = _playniteApi?.Dialogs?.ShowMessage(
                    message,
                    "Confirm Song Deletion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                // Clear confirmation flag immediately after dialog closes
                _isShowingConfirmation = false;
                
                Logger.DebugIf(LogPrefix, $"Confirmation result: {result}");

                if (result == MessageBoxResult.Yes)
                {
                    UpdateInputFeedback("🗑️ Deleting file...");
                    DeleteSongFile(filePath, isPrimarySong);
                }
                else
                {
                    UpdateInputFeedback("❌ Deletion cancelled");
                    ResetDeletionState();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error confirming song deletion");
                UpdateInputFeedback("❌ Error confirming deletion");
                ResetDeletionState();
            }
        }

        /// <summary>
        /// Reset deletion state flags and re-enable controls
        /// </summary>
        private void ResetDeletionState()
        {
            _isDeletionInProgress = false;
            _isShowingConfirmation = false;
            DeleteButton.IsEnabled = true;
        }

        /// <summary>
        /// Stop all music playback to free up the file for deletion
        /// </summary>
        private void StopAllMusicPlayback(string fileName)
        {
            try
            {
                Logger.DebugIf(LogPrefix, $"Stopping all music playback before deleting: {fileName}");
                
                // Stop any preview that might be playing
                StopCurrentPreview();
                
                // Stop game music playback to free up any files
                if (_playbackService != null)
                {
                    if (_playbackService.IsPlaying)
                    {
                        _playbackService.Stop();
                        Logger.DebugIf(LogPrefix, "Stopped game music playback for file deletion");
                        
                        // Give a moment for the file to be released
                        System.Threading.Thread.Sleep(100);
                    }
                }
                
                // Force garbage collection to help release file handles
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                UpdateInputFeedback($"🔇 Stopped music playback for {fileName}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error stopping music playback");
                UpdateInputFeedback("⚠️ Could not stop music, deletion may fail");
            }
        }

        /// <summary>
        /// Delete the song file
        /// </summary>
        private void DeleteSongFile(string filePath, bool isPrimarySong)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                Logger.Info($"Starting deletion of file: {filePath}");
                
                // Verify file still exists before attempting deletion
                if (!File.Exists(filePath))
                {
                    UpdateInputFeedback("❌ File no longer exists");
                    ResetDeletionState();
                    return;
                }
                
                // Stop preview if this file is currently playing
                if (_currentlyPreviewing == filePath)
                {
                    StopCurrentPreview();
                    Logger.DebugIf(LogPrefix, "Stopped preview for file being deleted");
                }

                // Clear primary song if this is the primary
                if (isPrimarySong)
                {
                    _fileService?.RemovePrimarySong(_currentGame);
                    Logger.Info($"Cleared primary song before deletion: {fileName}");
                }

                // Wait a moment to ensure file handles are released
                System.Threading.Thread.Sleep(200);
                
                // Delete the file
                Logger.DebugIf(LogPrefix, $"Attempting to delete file: {filePath}");
                File.Delete(filePath);
                Logger.Info($"File deleted successfully: {filePath}");
                
                // Verify deletion was successful
                if (File.Exists(filePath))
                {
                    throw new IOException("File still exists after deletion attempt");
                }
                
                // Remove from our list
                _musicFiles.Remove(filePath);
                Logger.DebugIf(LogPrefix, $"Removed from music files list: {fileName}");
                
                // Update UI
                PopulateFilesList();
                
                var successMessage = $"Successfully deleted: {fileName}";
                if (isPrimarySong)
                {
                    successMessage += "\nPrimary song setting was also cleared.";
                }
                
                UpdateInputFeedback($"✅ {successMessage}");
                
                // Show success message
                _playniteApi?.Dialogs?.ShowMessage(successMessage, "Song Deleted");
                
                Logger.Info($"Deletion completed successfully: {fileName}");
                
                // Close dialog if no more files
                if (_musicFiles.Count == 0)
                {
                    _playniteApi?.Dialogs?.ShowMessage(
                        "All music files have been deleted for this game.",
                        "No Music Files Remaining");
                    CloseDialog(true);
                }
            }
            catch (UnauthorizedAccessException)
            {
                var errorMsg = "Access denied. The file may be in use or you may not have permission to delete it.";
                UpdateInputFeedback($"❌ {errorMsg}");
                _playniteApi?.Dialogs?.ShowErrorMessage(errorMsg, "Delete Failed");
                Logger.Error($"Access denied deleting file: {filePath}");
            }
            catch (IOException ioEx)
            {
                var errorMsg = $"File operation failed: {ioEx.Message}";
                UpdateInputFeedback($"❌ {errorMsg}");
                _playniteApi?.Dialogs?.ShowErrorMessage(errorMsg, "Delete Failed");
                Logger.Error(ioEx, $"IO error deleting file: {filePath}");
            }
            catch (Exception ex)
            {
                var errorMsg = $"Unexpected error: {ex.Message}";
                UpdateInputFeedback($"❌ {errorMsg}");
                _playniteApi?.Dialogs?.ShowErrorMessage(errorMsg, "Delete Failed");
                Logger.Error(ex, $"Error deleting file: {filePath}");
            }
            finally
            {
                // Always reset deletion state
                ResetDeletionState();
                Logger.DebugIf(LogPrefix, "Deletion process completed, state reset");
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
                        Logger.DebugIf(LogPrefix, focusEx, "Error returning focus to main window");
                    }
                    
                    window.DialogResult = success;
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error closing delete songs dialog");
            }
        }
    }
}