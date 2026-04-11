using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    // Controller-friendly file browser for adding music files to a game.
    // Two-screen flow: folder selection → file selection within chosen folder.
    public partial class ControllerAddMusicDialog : UserControl, IControllerInputReceiver
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private DateTime _lastDpadNavigationTime = DateTime.MinValue;
        private const int DpadDebounceMs = 150;

        private Game _currentGame;
        private IPlayniteAPI _playniteApi;
        private GameMusicFileService _fileService;
        private IMusicPlaybackService _playbackService;

        // State
        private bool _isBrowsingFiles = false;
        private string _currentFolder = null;
        private string _selectedFilePath = null;
        private string _lastUsedFolder = null;

        // Predefined folder locations
        private List<FolderOption> _folderOptions;

        public string SelectedFilePath => _selectedFilePath;

        public ControllerAddMusicDialog()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                try
                {
                    ItemsListBox.Focus();
                    ShowFolderSelection();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error initializing add music dialog");
                }
            };

            ItemsListBox.PreviewKeyDown += OnListBoxKeyDown;
        }

        public void Initialize(Game game, IPlayniteAPI api, GameMusicFileService fileService, IMusicPlaybackService playbackService)
        {
            _currentGame = game;
            _playniteApi = api;
            _fileService = fileService;
            _playbackService = playbackService;

            BuildFolderOptions();
        }

        private void BuildFolderOptions()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _folderOptions = new List<FolderOption>();

            AddFolderOption("📁 Downloads", Path.Combine(userProfile, "Downloads"));
            AddFolderOption("🎵 Music", Path.Combine(userProfile, "Music"));
            AddFolderOption("🖥️ Desktop", Path.Combine(userProfile, "Desktop"));
            AddFolderOption("📄 Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

            // Last used folder (if saved and still exists)
            if (!string.IsNullOrEmpty(_lastUsedFolder) && Directory.Exists(_lastUsedFolder))
            {
                AddFolderOption($"🕐 Last Used: {Path.GetFileName(_lastUsedFolder)}", _lastUsedFolder);
            }

            // Game's existing music folder (if it exists and has files)
            if (_currentGame != null)
            {
                var gameMusicDir = _fileService?.GetGameMusicDirectory(_currentGame);
                if (!string.IsNullOrEmpty(gameMusicDir) && Directory.Exists(gameMusicDir))
                {
                    AddFolderOption("🎮 Current Game Music Folder", gameMusicDir);
                }
            }
        }

        private void AddFolderOption(string label, string path)
        {
            if (Directory.Exists(path))
            {
                _folderOptions.Add(new FolderOption { Label = label, Path = path });
            }
        }

        #region Screen Management

        private void ShowFolderSelection()
        {
            _isBrowsingFiles = false;
            _currentFolder = null;

            DialogTitle.Text = "🎮 Add Music File";
            CurrentPathText.Text = $"Select a folder to browse — Adding to: {_currentGame?.Name ?? "Unknown"}";
            InputFeedback.Text = "Choose a folder location";
            StatusText.Text = "";
            BackButton.Visibility = Visibility.Collapsed;

            ItemsListBox.Items.Clear();
            foreach (var folder in _folderOptions)
            {
                ItemsListBox.Items.Add(new ListBoxItem
                {
                    Content = $"{folder.Label}\n{folder.Path}",
                    Tag = folder.Path,
                    FontSize = 14
                });
            }

            if (ItemsListBox.Items.Count > 0)
            {
                ItemsListBox.SelectedIndex = 0;
                ItemsListBox.ScrollIntoView(ItemsListBox.Items[0]);
            }
        }

        private void ShowFilesInFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                InputFeedback.Text = "❌ Folder not found";
                return;
            }

            _isBrowsingFiles = true;
            _currentFolder = folderPath;
            _lastUsedFolder = folderPath;

            DialogTitle.Text = "🎮 Select a Music File";
            CurrentPathText.Text = folderPath;
            BackButton.Visibility = Visibility.Visible;

            ItemsListBox.Items.Clear();

            // Add subfolders first
            try
            {
                var subDirs = Directory.GetDirectories(folderPath)
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var dir in subDirs)
                {
                    var dirName = Path.GetFileName(dir);
                    ItemsListBox.Items.Add(new ListBoxItem
                    {
                        Content = $"📁 {dirName}",
                        Tag = dir,
                        FontSize = 14
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip folders we can't access
            }

            // Add audio files
            var audioExtensions = Constants.SupportedAudioExtensionsLowercase;
            try
            {
                var files = Directory.GetFiles(folderPath)
                    .Where(f => audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var fileInfo = new FileInfo(file);
                    var sizeMb = fileInfo.Length / (1024.0 * 1024.0);
                    ItemsListBox.Items.Add(new ListBoxItem
                    {
                        Content = $"🎵 {fileName}  ({sizeMb:F1} MB)",
                        Tag = file,
                        FontSize = 14
                    });
                }

                var fileCount = files.Count;
                var dirCount = ItemsListBox.Items.Count - fileCount;
                StatusText.Text = $"{fileCount} audio file(s), {dirCount} folder(s)";
                InputFeedback.Text = fileCount > 0
                    ? "A: Select file or enter folder • B: Back"
                    : "No audio files found — navigate to a subfolder or go back";
            }
            catch (UnauthorizedAccessException)
            {
                InputFeedback.Text = "❌ Access denied to this folder";
            }

            if (ItemsListBox.Items.Count > 0)
            {
                ItemsListBox.SelectedIndex = 0;
                ItemsListBox.ScrollIntoView(ItemsListBox.Items[0]);
            }
        }

        #endregion

        #region Selection Logic

        private void ConfirmSelection()
        {
            var selectedItem = ItemsListBox.SelectedItem as ListBoxItem;
            if (selectedItem?.Tag == null) return;

            var path = selectedItem.Tag.ToString();

            if (!_isBrowsingFiles)
            {
                // Folder screen — enter the selected folder
                ShowFilesInFolder(path);
                return;
            }

            // File browser screen
            if (Directory.Exists(path))
            {
                // Selected a subfolder — navigate into it
                ShowFilesInFolder(path);
                return;
            }

            if (File.Exists(path))
            {
                // Selected an audio file — copy it
                CopyFileToGame(path);
            }
        }

        private void CopyFileToGame(string sourcePath)
        {
            try
            {
                var destDir = _fileService.EnsureGameMusicDirectory(_currentGame);
                if (string.IsNullOrEmpty(destDir)) return;

                var fileName = Path.GetFileName(sourcePath);
                var destPath = Path.Combine(destDir, fileName);

                // Handle duplicate file names
                if (File.Exists(destPath))
                {
                    var baseName = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    var counter = 1;
                    while (File.Exists(destPath))
                    {
                        destPath = Path.Combine(destDir, $"{baseName} ({counter}){ext}");
                        counter++;
                    }
                }

                File.Copy(sourcePath, destPath);
                _fileService.InvalidateCacheForGame(_currentGame);
                _selectedFilePath = destPath;

                InputFeedback.Text = $"✓ Added: {Path.GetFileName(destPath)}";
                Logger.Info($"Added music file for {_currentGame?.Name}: {Path.GetFileName(destPath)}");

                // Close the dialog
                var window = Window.GetWindow(this);
                window?.Close();
            }
            catch (Exception ex)
            {
                InputFeedback.Text = $"❌ Failed to copy: {ex.Message}";
                Logger.Error(ex, $"Failed to copy music file to game {_currentGame?.Name}");
            }
        }

        private void GoBack()
        {
            if (!_isBrowsingFiles)
            {
                // Already on folder screen — close dialog
                CloseDialog();
                return;
            }

            // Check if we can go up one directory
            if (_currentFolder != null)
            {
                var parent = Directory.GetParent(_currentFolder)?.FullName;
                // If parent exists and isn't a root drive, navigate up
                if (parent != null && parent != _currentFolder)
                {
                    ShowFilesInFolder(parent);
                    return;
                }
            }

            // Go back to folder selection
            ShowFolderSelection();
        }

        #endregion

        #region Controller Input

        public void OnControllerButtonPressed(ControllerInput button)
        {
            switch (button)
            {
                case ControllerInput.A:
                    ConfirmSelection();
                    break;
                case ControllerInput.B:
                    GoBack();
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
                    NavigateList(-ItemsListBox.Items.Count);
                    break;
                case ControllerInput.TriggerRight:
                    NavigateList(ItemsListBox.Items.Count);
                    break;
            }
        }

        public void OnControllerButtonReleased(ControllerInput button) { }

        private bool TryDpadNavigation()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastDpadNavigationTime).TotalMilliseconds < DpadDebounceMs)
                return false;
            _lastDpadNavigationTime = now;
            return true;
        }

        private void NavigateList(int delta)
        {
            if (ItemsListBox.Items.Count == 0) return;

            var newIndex = ItemsListBox.SelectedIndex + delta;
            newIndex = Math.Max(0, Math.Min(newIndex, ItemsListBox.Items.Count - 1));
            ItemsListBox.SelectedIndex = newIndex;
            ItemsListBox.ScrollIntoView(ItemsListBox.Items[newIndex]);
        }

        #endregion

        #region Keyboard Input

        private void OnListBoxKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    ConfirmSelection();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    GoBack();
                    e.Handled = true;
                    break;
                case Key.Back:
                case Key.BrowserBack:
                    GoBack();
                    e.Handled = true;
                    break;
            }
        }

        #endregion

        #region Button Handlers

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            GoBack();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseDialog();
        }

        private void CloseDialog()
        {
            try
            {
                var window = Window.GetWindow(this);
                window?.Close();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error closing add music dialog");
            }
        }

        #endregion

        private class FolderOption
        {
            public string Label { get; set; }
            public string Path { get; set; }
        }
    }
}
