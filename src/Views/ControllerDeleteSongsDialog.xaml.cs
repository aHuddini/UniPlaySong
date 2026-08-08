using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Services;
using UniPlaySong.Services.Controller;

namespace UniPlaySong.Views
{
    // Controller-friendly dialog for deleting songs from game music folder
    public partial class ControllerDeleteSongsDialog : UserControl, IControllerInputReceiver
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();
        private const string LogPrefix = "DeleteSongs";

        // D-pad debouncing - prevents double-input from both controller and WPF processing
        private DateTime _lastDpadNavigationTime = DateTime.MinValue;
        private const int DpadDebounceMs = 150; // Minimum ms between D-pad navigations (prevents keyboard+controller double-input)

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

        // Cooldown timestamp - blocks ALL button processing for a period after modal dialogs close
        // This prevents the race condition where a button press that closed the modal is
        // detected as a "new press" by the background polling loop
        private DateTime _modalCooldownUntil = DateTime.MinValue;
        private const int ModalCooldownMs = 350; // Block input for 350ms after modal closes

        public ControllerDeleteSongsDialog()
        {
            InitializeComponent();
            
            // Focus on the list box when loaded
            Loaded += (s, e) =>
            {
                FilesListBox.Focus();
                if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                {
                    var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                    plugin?.GetControllerEventRouter()?.Register(this);
                }
            };

            Unloaded += (s, e) =>
            {
                if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                {
                    var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                    plugin?.GetControllerEventRouter()?.Unregister(this);
                }
                StopCurrentPreview();
            };

            // Handle keyboard input as fallback
            // Use PreviewKeyDown to intercept keyboard events BEFORE they reach child controls
            // This prevents WPF's ListBox from also processing arrow keys alongside our handler
            PreviewKeyDown += OnKeyDown;
        }

        // Initialize the dialog for a specific game
        public void InitializeForGame(Game game, IPlayniteAPI playniteApi, GameMusicFileService fileService, IMusicPlaybackService playbackService)
        {
            try
            {
                _currentGame = game;
                _playniteApi = playniteApi;
                _fileService = fileService;
                _playbackService = playbackService;

                // Load music files
                LoadMusicFiles();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing delete songs dialog");
                UpdateInputFeedback("❌ Failed to initialize dialog");
            }
        }

        // Load music files for the current game
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

        // Populate the files list with music files
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
                catch (Exception)
                {
                    // Error getting file size - continue without it
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

        // Format file size for display
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

        // Show message when no files are found
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

        // Update input feedback text
        private void UpdateInputFeedback(string message)
        {
            if (InputFeedback != null)
            {
                InputFeedback.Text = message;
            }
        }

        #region Controller Support

        public void OnControllerButtonPressed(ControllerInput button)
        {
            try
            {
                // Block input during cooldown period after modal dialogs close
                if (DateTime.Now < _modalCooldownUntil)
                {
                    return;
                }

                // Ignore input during deletion process or confirmation
                if (_isDeletionInProgress || _isShowingConfirmation)
                {
                    return;
                }

                switch (button)
                {
                    case ControllerInput.A:
                        DeleteButton_Click(null, null);
                        break;
                    case ControllerInput.B:
                        // Unregister immediately to prevent any further input during close
                        if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                        {
                            var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                            plugin?.GetControllerEventRouter()?.Unregister(this);
                        }
                        _modalCooldownUntil = DateTime.Now.AddMilliseconds(500);
                        CancelButton_Click(null, null);
                        break;
                    case ControllerInput.X:
                    case ControllerInput.Y:
                        PreviewSelectedFile();
                        break;
                    case ControllerInput.DPadUp:
                    case ControllerInput.DPadLeft:
                        if (TryDpadNavigation()) NavigateList(-1);
                        break;
                    case ControllerInput.DPadDown:
                    case ControllerInput.DPadRight:
                        if (TryDpadNavigation()) NavigateList(1);
                        break;
                    case ControllerInput.LeftShoulder:
                        NavigateList(-5);
                        break;
                    case ControllerInput.RightShoulder:
                        NavigateList(5);
                        break;
                    case ControllerInput.TriggerLeft:
                        JumpToItem(0);
                        break;
                    case ControllerInput.TriggerRight:
                        JumpToItem(FilesListBox.Items.Count - 1);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling controller input");
            }
        }

        public void OnControllerButtonReleased(ControllerInput button) { }

        // Check if enough time has passed since last D-pad navigation (debouncing)
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

        // Navigate the list by a specified offset
        private void NavigateList(int offset)
        {
            if (FilesListBox.Items.Count == 0) return;

            int newIndex = Math.Max(0, Math.Min(FilesListBox.Items.Count - 1, FilesListBox.SelectedIndex + offset));
            FilesListBox.SelectedIndex = newIndex;
            FilesListBox.ScrollIntoView(FilesListBox.SelectedItem);
        }

        // Jump to a specific item index
        private void JumpToItem(int index)
        {
            if (FilesListBox.Items.Count == 0) return;

            int targetIndex = Math.Max(0, Math.Min(FilesListBox.Items.Count - 1, index));
            FilesListBox.SelectedIndex = targetIndex;
            FilesListBox.ScrollIntoView(FilesListBox.SelectedItem);
        }

        #endregion

        #region Preview Support

        // Preview the currently selected file
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

        // Play preview file
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
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error playing preview");
                UpdateInputFeedback($"❌ Preview error: {ex.Message}");
                RestoreGameMusic();
            }
        }

        // Stop current preview and restore game music
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

        // Pause game music for preview
        private void PauseGameMusicForPreview()
        {
            try
            {
                if (_playbackService != null && _playbackService.IsPlaying)
                {
                    _wasGameMusicPlaying = true;
                    _playbackService.Pause();
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

        // Restore game music after preview
        private void RestoreGameMusic()
        {
            try
            {
                if (_wasGameMusicPlaying && _playbackService != null)
                {
                    _playbackService.Resume();
                    _wasGameMusicPlaying = false;
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

        // Handle keyboard input as fallback
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // Ignore all input during deletion or confirmation dialogs
            if (_isDeletionInProgress || _isShowingConfirmation)
            {
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                // D-pad Up/Down mapped to arrow keys - use debounce to prevent double-input
                case Key.Up:
                    if (TryDpadNavigation())
                        NavigateList(-1);
                    e.Handled = true;
                    break;

                case Key.Down:
                    if (TryDpadNavigation())
                        NavigateList(1);
                    e.Handled = true;
                    break;

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

        // Handle delete button click
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

        // Handle cancel button click
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseDialog(false);
        }

        #endregion

        #region Song Deletion

        // Confirm and delete the selected song
        private async void ConfirmAndDeleteSong(string filePath)
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
                await StopAllMusicPlaybackAsync(fileName);

                // Build confirmation message
                var message = $"Are you sure you want to delete this song?\n\n" +
                             $"File: {fileName}\n" +
                             $"Path: {filePath}";

                if (isPrimarySong)
                {
                    message += "\n\n⚠️ WARNING: This is your current primary song!";
                }

                message += "\n\nThis action cannot be undone!";

                // Show controller-friendly confirmation dialog (larger text for TV/fullscreen)
                var confirmed = DialogHelper.ShowControllerConfirmation(
                    _playniteApi,
                    message,
                    "Confirm Song Deletion");

                // After dialog closes, refresh controller state to prevent detecting
                // the A button that was used to open this dialog as a new press
                RefreshControllerStateWithCooldown();

                // Small delay to ensure controller state is fully updated
                await Task.Delay(100);

                // Clear confirmation flag after controller state refresh
                _isShowingConfirmation = false;

                if (confirmed)
                {
                    UpdateInputFeedback("🗑️ Deleting file...");
                    await DeleteSongFileAsync(filePath, isPrimarySong);
                }
                else
                {
                    UpdateInputFeedback("❌ Deletion cancelled - select a file to delete");
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

        // Activate cooldown to block input processing after modal dialogs close
        private void RefreshControllerStateWithCooldown()
        {
            _modalCooldownUntil = DateTime.Now.AddMilliseconds(ModalCooldownMs);
        }

        // Reset deletion state flags and re-enable controls
        private void ResetDeletionState()
        {
            _isDeletionInProgress = false;
            _isShowingConfirmation = false;
            DeleteButton.IsEnabled = true;
        }

        // Stop all music playback to free up the file for deletion
        private async Task StopAllMusicPlaybackAsync(string fileName)
        {
            try
            {
                // Stop any preview that might be playing
                StopCurrentPreview();
                
                // Stop game music playback to free up any files
                if (_playbackService != null)
                {
                    if (_playbackService.IsPlaying)
                    {
                        _playbackService.Stop();

                        // Give a moment for the file to be released
                        await Task.Delay(100);
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

        // Delete the song file
        private async Task DeleteSongFileAsync(string filePath, bool isPrimarySong)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);

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
                }

                // Clear primary song if this is the primary
                if (isPrimarySong)
                {
                    _fileService?.RemovePrimarySong(_currentGame);
                }

                // Wait a moment to ensure file handles are released
                await Task.Delay(200);

                // Delete the file
                File.Delete(filePath);

                // Verify deletion was successful
                if (File.Exists(filePath))
                {
                    throw new IOException("File still exists after deletion attempt");
                }

                // Remove from our list
                _musicFiles.Remove(filePath);

                // Invalidate song cache since we deleted a file
                _fileService?.InvalidateCacheForGame(_currentGame);

                // If this was the last audio file, clean up the empty directory
                var musicDir = Path.GetDirectoryName(filePath);
                _fileService?.CleanupEmptyDirectory(musicDir);

                // Update UI
                PopulateFilesList();
                
                var successMessage = $"Successfully deleted: {fileName}";
                if (isPrimarySong)
                {
                    successMessage += "\nPrimary song setting was also cleared.";
                }
                
                UpdateInputFeedback($"✅ {successMessage}");

                // Use auto-closing toast popup instead of modal dialog
                // This avoids the XInput double-press issues entirely and works in fullscreen
                DialogHelper.ShowSuccessToast(_playniteApi, successMessage, "Song Deleted");

                // Close dialog if no more files
                if (_musicFiles.Count == 0)
                {
                    // Use auto-closing toast popup
                    DialogHelper.ShowSuccessToast(
                        _playniteApi,
                        "All music files have been deleted for this game.",
                        "All Files Deleted");

                    CloseDialog(true);
                }
            }
            catch (UnauthorizedAccessException)
            {
                var errorMsg = "Access denied. The file may be in use or you may not have permission to delete it.";
                UpdateInputFeedback($"❌ {errorMsg}");
                DialogHelper.ShowErrorToast(_playniteApi, errorMsg, "Delete Failed");
                Logger.Error($"Access denied deleting file: {filePath}");
            }
            catch (IOException ioEx)
            {
                var errorMsg = $"File operation failed: {ioEx.Message}";
                UpdateInputFeedback($"❌ {errorMsg}");
                DialogHelper.ShowErrorToast(_playniteApi, errorMsg, "Delete Failed");
                Logger.Error(ioEx, $"IO error deleting file: {filePath}");
            }
            catch (Exception ex)
            {
                var errorMsg = $"Unexpected error: {ex.Message}";
                UpdateInputFeedback($"❌ {errorMsg}");
                DialogHelper.ShowErrorToast(_playniteApi, errorMsg, "Delete Failed");
                Logger.Error(ex, $"Error deleting file: {filePath}");
            }
            finally
            {
                // Always reset deletion state
                ResetDeletionState();
            }
        }

        #endregion

        // Close the dialog with the specified result
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
                    catch (Exception)
                    {
                        // Error returning focus to main window - ignore
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
