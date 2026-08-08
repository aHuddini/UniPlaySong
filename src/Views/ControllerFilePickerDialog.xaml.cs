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
    // Controller-friendly file picker dialog for setting/removing primary songs
    public partial class ControllerFilePickerDialog : UserControl, IControllerInputReceiver
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();
        private const string LogPrefix = "FilePicker";

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

        // Initialize the dialog for a specific game and mode
        public void InitializeForGame(Game game, IPlayniteAPI playniteApi, GameMusicFileService fileService, IMusicPlaybackService playbackService, DialogMode mode)
        {
            try
            {
                _currentGame = game;
                _playniteApi = playniteApi;
                _fileService = fileService;
                _playbackService = playbackService;
                _mode = mode;

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

        // Update UI elements based on dialog mode
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

        // Update the current primary song display
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
                switch (button)
                {
                    case ControllerInput.A:
                        ConfirmButton_Click(null, null);
                        break;
                    case ControllerInput.B:
                        CancelButton_Click(null, null);
                        break;
                    case ControllerInput.X:
                    case ControllerInput.Y:
                        PreviewSelectedFile();
                        break;
                    case ControllerInput.DPadUp:
                        if (TryDpadNavigation()) NavigateList(-1);
                        break;
                    case ControllerInput.DPadDown:
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

        // Handle confirm button click
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

        // Handle cancel button click
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseDialog(false);
        }

        // Handle remove button click
        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            RemovePrimarySong();
        }

        #endregion

        #region Primary Song Management

        // Set the selected file as primary song
        private void SetPrimarySong(string filePath)
        {
            try
            {
                _fileService?.SetPrimarySong(_currentGame, filePath);
                
                var fileName = Path.GetFileName(filePath);
                _playniteApi?.Dialogs?.ShowMessage(
                    $"Primary song set to:\n{fileName}",
                    "UniPlaySong");

                CloseDialog(true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error setting primary song");
                UpdateInputFeedback("❌ Error setting primary song");
            }
        }

        // Remove the current primary song
        private void RemovePrimarySong()
        {
            try
            {
                _fileService?.RemovePrimarySong(_currentGame);

                _playniteApi?.Dialogs?.ShowMessage(
                    "Primary song removed. The game will use randomized selection.",
                    "UniPlaySong");

                CloseDialog(true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error removing primary song");
                UpdateInputFeedback("❌ Error removing primary song");
            }
        }

        // Normalize the selected individual song
        private void NormalizeIndividualSong(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);

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

        // Trim the selected individual song
        private void TrimIndividualSong(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);

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

        // Repair the selected individual song
        private void RepairIndividualSong(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);

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

        // Close the dialog with the specified result
        private void CloseDialog(bool success)
        {
            try
            {
                // Unregister from controller router to prevent stale input during close
                if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                {
                    var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                    plugin?.GetControllerEventRouter()?.Unregister(this);
                }

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
                Logger.Error(ex, "Error closing file picker dialog");
            }
        }
    }
}
