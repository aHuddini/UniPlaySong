using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Downloaders;
using UniPlaySong.Models;
using UniPlaySong.Services;
using UniPlaySong.ViewModels;

namespace UniPlaySong.Views
{
    /// <summary>
    /// Controller-optimized download dialog that provides full download functionality
    /// Runs completely separate from the main dialog with seamless fullscreen navigation
    /// </summary>
    public partial class SimpleControllerDialog : UserControl
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "ControllerDownload";

        private CancellationTokenSource _controllerMonitoringCancellation;
        private bool _isMonitoring = false;

        // D-pad debouncing - prevents double-input from both XInput and WPF processing
        private DateTime _lastDpadNavigationTime = DateTime.MinValue;
        private const int DpadDebounceMs = 300; // Minimum ms between D-pad navigations (300ms for reliable single-item navigation)

        // Download functionality dependencies
        private Game _currentGame;
        private DownloadDialogService _dialogService;
        private IPlayniteAPI _playniteApi;
        private IDownloadManager _downloadManager;
        private GameMusicFileService _fileService;
        private DialogStep _currentStep = DialogStep.SourceSelection;
        private Source? _selectedSource;
        private Album _selectedAlbum;

        // Flag to block input during modal dialogs (e.g., download success message)
        private volatile bool _isShowingModalDialog = false;

        // Cooldown timestamp - blocks ALL button processing for a period after modal dialogs close
        // This prevents the race condition where a button press that closed the modal is
        // detected as a "new press" by the background polling loop
        private DateTime _modalCooldownUntil = DateTime.MinValue;
        private const int ModalCooldownMs = 350; // Block input for 350ms after modal closes

        // Dialog state
        private List<GenericItemOption> _sourceOptions;
        private List<DownloadItemViewModel> _currentResults;
        
        // Preview functionality
        private IMusicPlaybackService _playbackService;
        private string _currentlyPreviewing = null;
        private System.Windows.Media.MediaPlayer _previewPlayer;
        private bool _wasGameMusicPlaying = false;

        // Preview rate limiting
        private DateTime _lastPreviewRequestTime = DateTime.MinValue;
        private const int MinPreviewIntervalMs = 2000; // 2 seconds between preview requests
        
        public enum DialogStep
        {
            SourceSelection,
            AlbumSelection, 
            SongSelection,
            Downloading
        }

        public SimpleControllerDialog()
        {
            InitializeComponent();
            Logger.DebugIf(LogPrefix, "Controller download dialog initialized");
            
            // Focus the first item for controller navigation
            Loaded += (s, e) => 
            {
                ResultsListBox.Focus();
                if (ResultsListBox.Items.Count > 0)
                {
                    ResultsListBox.SelectedIndex = 0;
                }
            };
            
            // Add keyboard/controller input handling
            // Use PreviewKeyDown to intercept keyboard events BEFORE they reach child controls
            // This prevents WPF's ListBox from also processing arrow keys alongside our handler
            PreviewKeyDown += OnKeyDown;
            Focusable = true;
            
            // Start monitoring Xbox controller
            Loaded += (s, e) => StartControllerMonitoring();
            Unloaded += (s, e) => 
            {
                StopControllerMonitoring();
                StopCurrentPreview(); // Stop any playing preview and restore game music
            };
        }

        /// <summary>
        /// Initialize the dialog for downloading music for a specific game
        /// </summary>
        public void InitializeForGame(Game game, DownloadDialogService dialogService, IPlayniteAPI playniteApi, IDownloadManager downloadManager, IMusicPlaybackService playbackService, GameMusicFileService fileService)
        {
            try
            {
                _currentGame = game;
                _dialogService = dialogService;
                _playniteApi = playniteApi;
                _downloadManager = downloadManager;
                _playbackService = playbackService;
                _fileService = fileService;
                _currentStep = DialogStep.SourceSelection;
                
                Logger.DebugIf(LogPrefix, $"Initialized controller dialog for game: {game?.Name}");
                
                // Load source selection
                LoadSourceSelection();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing controller dialog for game");
                ShowError("Failed to initialize download dialog");
            }
        }

        /// <summary>
        /// Load the source selection step (KHInsider vs YouTube)
        /// </summary>
        private void LoadSourceSelection()
        {
            try
            {
                _currentStep = DialogStep.SourceSelection;
                
                // Create source options (same logic as DownloadDialogService)
                var youtubeConfigured = CheckYouTubeConfiguration();
                
                _sourceOptions = new List<GenericItemOption>
                {
                    new GenericItemOption("KHInsider", "Download from KHInsider (Game soundtracks)"),
                    new GenericItemOption("YouTube", 
                        youtubeConfigured 
                            ? "Download from YouTube (Playlists and videos)" 
                            : "Download from YouTube (Playlists and videos) - yt-dlp/ffmpeg required")
                };

                // Update UI
                UpdateUIForSourceSelection();
                PopulateResultsList(_sourceOptions.Select(CreateSourceItem).ToList());
                
                Logger.DebugIf(LogPrefix, "Loaded source selection options");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error loading source selection");
                ShowError("Failed to load download sources");
            }
        }

        /// <summary>
        /// Check if YouTube is properly configured
        /// </summary>
        private bool CheckYouTubeConfiguration()
        {
            try
            {
                // For now, return true - we'll improve this when we have access to settings
                // The user will see the YouTube option and get an error if it's not configured
                return true;
            }
            catch (Exception ex)
            {
                Logger.DebugIf(LogPrefix, ex, "Error checking YouTube configuration");
                return false;
            }
        }

        /// <summary>
        /// Create a display item for a source option
        /// </summary>
        private DownloadItemViewModel CreateSourceItem(GenericItemOption option)
        {
            return new DownloadItemViewModel
            {
                Name = option.Name,
                Description = option.Description,
                Item = option
            };
        }

        /// <summary>
        /// Update UI elements for source selection step
        /// </summary>
        private void UpdateUIForSourceSelection()
        {
            try
            {
                // Update title and instructions
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateInputFeedback("🎮 Select download source: A to confirm, B to cancel, X/Y to preview");
                }));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating UI for source selection");
            }
        }

        /// <summary>
        /// Populate the results list with items
        /// </summary>
        private void PopulateResultsList(List<DownloadItemViewModel> items)
        {
            try
            {
                _currentResults = items;
                
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ResultsListBox.Items.Clear();
                    
                    foreach (var item in items)
                    {
                        var listItem = new ListBoxItem
                        {
                            Tag = item
                        };
                        
                        // Create a proper content layout for better display
                        var stackPanel = new StackPanel
                        {
                            Margin = new Thickness(0),
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };

                        // Use gold color for hint items, default for others
                        var isHint = item.IsFromHint;
                        var nameColor = isHint
                            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)) // Gold #FFD700
                            : System.Windows.Media.Brushes.White;
                        var descColor = isHint
                            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00)) // Orange #FFA500
                            : null; // Use default

                        var nameBlock = new TextBlock
                        {
                            Text = isHint ? $"★ {item.Name}" : $"🎵 {item.Name}",
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 4),
                            Foreground = nameColor
                        };

                        stackPanel.Children.Add(nameBlock);

                        if (!string.IsNullOrEmpty(item.Description))
                        {
                            var descBlock = new TextBlock
                            {
                                Text = item.Description,
                                FontSize = 13,
                                Opacity = isHint ? 0.95 : 0.85,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(16, 0, 0, 0), // Indent description
                                LineHeight = 16
                            };
                            if (descColor != null)
                            {
                                descBlock.Foreground = descColor;
                            }
                            stackPanel.Children.Add(descBlock);
                        }

                        listItem.Content = stackPanel;
                        listItem.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                        ResultsListBox.Items.Add(listItem);
                    }
                    
                    if (ResultsListBox.Items.Count > 0)
                    {
                        ResultsListBox.SelectedIndex = 0;
                        ResultsListBox.Focus();
                    }
                }));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error populating results list");
            }
        }

        /// <summary>
        /// Show an error message in a controller-friendly way using non-blocking notifications
        /// </summary>
        private void ShowError(string message)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateInputFeedback($"❌ Error: {message}");

                    // Use auto-closing toast popup instead of modal dialog
                    // This avoids the XInput double-press issues entirely and works in fullscreen
                    DialogHelper.ShowErrorToast(_playniteApi, message, "Download Error");
                }));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error showing error message");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HandleCancelAction();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling cancel action");
            }
        }

        /// <summary>
        /// Handle the cancel/back action based on current dialog step
        /// </summary>
        private void HandleCancelAction()
        {
            try
            {
                Logger.DebugIf(LogPrefix, $"HandleCancelAction called - Current step: {_currentStep}");
                
                switch (_currentStep)
                {
                    case DialogStep.SourceSelection:
                        // Close the dialog
                        Logger.DebugIf(LogPrefix, "Closing controller dialog from source selection");
                        CloseDialog(false);
                        break;
                        
                    case DialogStep.AlbumSelection:
                        // Go back to source selection
                        Logger.DebugIf(LogPrefix, "Going back to source selection from album selection");
                        LoadSourceSelection();
                        break;
                        
                    case DialogStep.SongSelection:
                        // Go back to album selection
                        Logger.DebugIf(LogPrefix, $"Going back to album selection from song selection - Selected source: {_selectedSource}");
                        try
                        {
                            if (_selectedSource.HasValue)
                            {
                                Logger.DebugIf(LogPrefix, $"Loading album selection for source: {_selectedSource.Value}");
                                LoadAlbumSelection(_selectedSource.Value);
                            }
                            else
                            {
                                Logger.DebugIf(LogPrefix, "No selected source, going back to source selection");
                                LoadSourceSelection();
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Error going back to album selection");
                            UpdateInputFeedback("❌ Error going back - returning to source selection");
                            LoadSourceSelection(); // Fallback to source selection
                        }
                        break;
                        
                    case DialogStep.Downloading:
                        // Cancel download
                        Logger.DebugIf(LogPrefix, "Cancelling download");
                        UpdateInputFeedback("❌ Download cancelled");
                        CloseDialog(false);
                        break;
                        
                    default:
                        Logger.DebugIf(LogPrefix, $"Unknown step {_currentStep}, closing dialog");
                        CloseDialog(false);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling cancel action");
                UpdateInputFeedback("❌ Navigation error - closing dialog");
                CloseDialog(false);
            }
        }

        /// <summary>
        /// Close the dialog with the specified result
        /// </summary>
        private void CloseDialog(bool success)
        {
            try
            {
                Logger.DebugIf(LogPrefix, $"CloseDialog called with success: {success}");
                
                // Stop any preview playback and restore game music
                StopCurrentPreview();
                
                var window = Window.GetWindow(this);
                if (window != null)
                {
                    Logger.DebugIf(LogPrefix, "Closing dialog window");
                    
                    // Force focus return to Playnite main window
                    try
                    {
                        var mainWindow = _playniteApi?.Dialogs?.GetCurrentAppWindow();
                        if (mainWindow != null)
                        {
                            Logger.DebugIf(LogPrefix, "Forcing focus return to main window");
                            
                            // Multiple attempts to ensure focus returns
                            mainWindow.Activate();
                            mainWindow.Focus();
                            
                            // Small delay to ensure focus transfer
                            Task.Delay(100).ContinueWith(_ =>
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try
                                    {
                                        mainWindow.Activate();
                                        mainWindow.Topmost = true;
                                        mainWindow.Topmost = false;
                                    }
                                    catch (Exception delayedFocusEx)
                                    {
                                        Logger.DebugIf(LogPrefix, delayedFocusEx, "Error in delayed focus return");
                                    }
                                }));
                            });
                        }
                    }
                    catch (Exception focusEx)
                    {
                        Logger.DebugIf(LogPrefix, focusEx, "Error returning focus to main window");
                    }
                    
                    window.DialogResult = success;
                    window.Close();
                }
                else
                {
                    Logger.DebugIf(LogPrefix, "No parent window found to close");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error closing dialog");
            }
        }

        /// <summary>
        /// Handle preview action (X/Y button)
        /// </summary>
        private void HandlePreviewAction()
        {
            try
            {
                var selectedItem = ResultsListBox.SelectedItem as ListBoxItem;
                if (selectedItem?.Tag == null)
                {
                    UpdateInputFeedback("❌ No item selected for preview");
                    return;
                }

                switch (_currentStep)
                {
                    case DialogStep.SourceSelection:
                        PreviewSource(selectedItem);
                        break;
                        
                    case DialogStep.AlbumSelection:
                        PreviewAlbum(selectedItem);
                        break;
                        
                    case DialogStep.SongSelection:
                        PreviewSong(selectedItem);
                        break;
                        
                    default:
                        UpdateInputFeedback("Preview not available at this step");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling preview action");
                ShowError("Error previewing item");
            }
        }

        /// <summary>
        /// Preview source information
        /// </summary>
        private void PreviewSource(ListBoxItem selectedItem)
        {
            try
            {
                var sourceItem = selectedItem.Tag as DownloadItemViewModel;
                var sourceOption = sourceItem?.Item as GenericItemOption;
                
                if (sourceOption == null)
                {
                    UpdateInputFeedback("❌ Invalid source for preview");
                    return;
                }

                string previewInfo;
                switch (sourceOption.Name)
                {
                    case "KHInsider":
                        previewInfo = "🎵 KHInsider: Specializes in video game soundtracks\n• High-quality game music\n• Official soundtracks\n• Direct downloads";
                        break;
                    case "YouTube":
                        previewInfo = "🎵 YouTube: Music videos and playlists\n• Wide variety of content\n• User-uploaded music\n• Requires yt-dlp/ffmpeg";
                        break;
                    default:
                        previewInfo = $"🎵 {sourceOption.Name}: {sourceOption.Description}";
                        break;
                }

                UpdateInputFeedback($"📋 {previewInfo}");
                // Note: Preview info is now shown inline in the feedback text
                // No modal dialog needed - this prevents XInput issues
                
                Logger.DebugIf(LogPrefix, $"Previewed source: {sourceOption.Name}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error previewing source");
                UpdateInputFeedback("❌ Error previewing source");
            }
        }

        /// <summary>
        /// Preview album information
        /// </summary>
        private void PreviewAlbum(ListBoxItem selectedItem)
        {
            try
            {
                var albumItem = selectedItem.Tag as DownloadItemViewModel;
                var album = albumItem?.Item as Album;
                
                if (album == null)
                {
                    UpdateInputFeedback("❌ Invalid album for preview");
                    return;
                }

                string previewInfo = $"🎵 Album: {album.Name}\n" +
                                   $"📁 Source: {album.Source}\n" +
                                   $"🎮 Game: {_currentGame?.Name ?? "Unknown"}\n\n" +
                                   $"This album contains the soundtrack for {_currentGame?.Name}.\n" +
                                   $"Select to view individual songs for download.";

                // Show short preview info in the feedback line
                UpdateInputFeedback($"📋 Album: {album.Name} • Source: {album.Source}");
                // Note: Preview info is now shown inline in the feedback text
                // No modal dialog needed - this prevents XInput issues
                
                Logger.DebugIf(LogPrefix, $"Previewed album: {album.Name}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error previewing album");
                UpdateInputFeedback("❌ Error previewing album");
            }
        }

        /// <summary>
        /// Preview and play song audio
        /// </summary>
        private void PreviewSong(ListBoxItem selectedItem)
        {
            try
            {
                var songItem = selectedItem.Tag as DownloadItemViewModel;
                var song = songItem?.Item as Song;

                if (song == null)
                {
                    UpdateInputFeedback("❌ Invalid song for preview");
                    return;
                }

                // Rate limiting: Prevent rapid preview requests to avoid hammering the server
                var timeSinceLastRequest = (DateTime.Now - _lastPreviewRequestTime).TotalMilliseconds;
                if (timeSinceLastRequest < MinPreviewIntervalMs)
                {
                    var remainingMs = MinPreviewIntervalMs - timeSinceLastRequest;
                    Logger.DebugIf(LogPrefix, $"Preview request rate limited - {remainingMs:F0}ms remaining");
                    UpdateInputFeedback($"⏳ Please wait {remainingMs / 1000.0:F1}s before previewing again");
                    return;
                }
                _lastPreviewRequestTime = DateTime.Now;

                UpdateInputFeedback($"🎵 Previewing: {song.Name}...");
                
                // Start audio preview in background
                Task.Run(() =>
                {
                    try
                    {
                        // Stop any current preview
                        StopCurrentPreview();
                        
                        // Get temp path for preview
                        var tempPath = GetTempPathForPreview(song);
                        
                        // If already previewing this song, toggle it off
                        if (tempPath == _currentlyPreviewing)
                        {
                            StopCurrentPreview();
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                UpdateInputFeedback("🎮 Preview stopped - Game music resumed - X/Y to preview again");
                            }));
                            return;
                        }

                        // Download preview if needed
                        if (!System.IO.File.Exists(tempPath))
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                UpdateInputFeedback($"⬇️ Downloading preview: {song.Name}...");
                            }));
                            
                            Logger.DebugIf(LogPrefix, $"Preview file doesn't exist, downloading to: {tempPath}");
                            
                            var cancellationToken = new CancellationTokenSource().Token;
                            var downloaded = _downloadManager?.DownloadSong(song, tempPath, cancellationToken, isPreview: true) ?? false;
                            
                            Logger.DebugIf(LogPrefix, $"Download result: {downloaded}, File exists after download: {System.IO.File.Exists(tempPath)}");
                            
                            if (!downloaded || !System.IO.File.Exists(tempPath))
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    UpdateInputFeedback("❌ Preview download failed - check logs for details");
                                }));
                                Logger.Error($"Preview download failed for {song.Name}. Downloaded: {downloaded}, File exists: {System.IO.File.Exists(tempPath)}");
                                return;
                            }
                            
                            Logger.DebugIf(LogPrefix, $"Preview download successful, file size: {new System.IO.FileInfo(tempPath).Length} bytes");
                        }
                        else
                        {
                            Logger.DebugIf(LogPrefix, $"Preview file already exists: {tempPath}");
                        }

                        // Play the preview on the UI thread
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            PlayPreviewFile(tempPath, song.Name);
                            UpdateInputFeedback($"🔊 Playing preview: {song.Name} - X/Y to stop (Game music paused)");
                        }));
                        
                        Logger.DebugIf(LogPrefix, $"Started audio preview for song: {song.Name}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error during song preview");
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            UpdateInputFeedback($"❌ Preview error: {ex.Message}");
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error previewing song");
                UpdateInputFeedback("❌ Error previewing song");
            }
        }

        /// <summary>
        /// Stop current audio preview and restore game music
        /// </summary>
        private void StopCurrentPreview()
        {
            // Ensure we're on the UI thread since MediaPlayer is a WPF object
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => StopCurrentPreview());
                return;
            }

            try
            {
                if (_previewPlayer != null)
                {
                    _previewPlayer.Stop();
                    _previewPlayer.Close();
                    _previewPlayer = null;
                    Logger.DebugIf(LogPrefix, "Stopped preview player");
                }

                _currentlyPreviewing = null;

                // Resume game music if it was playing before preview
                RestoreGameMusic();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error stopping preview");
            }
        }

        /// <summary>
        /// Pause game music before starting preview
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
                Logger.Error(ex, "Error pausing game music for preview");
                _wasGameMusicPlaying = false;
            }
        }

        /// <summary>
        /// Resume game music after preview ends
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
                Logger.Error(ex, "Error resuming game music after preview");
                _wasGameMusicPlaying = false;
            }
        }

        /// <summary>
        /// Play preview file using simple MediaPlayer
        /// </summary>
        private void PlayPreviewFile(string tempPath, string songName)
        {
            try
            {
                Logger.DebugIf(LogPrefix, $"PlayPreviewFile called for: {songName}, path: {tempPath}");
                
                if (!System.IO.File.Exists(tempPath))
                {
                    Logger.Error($"Preview file does not exist: {tempPath}");
                    UpdateInputFeedback("❌ Preview file not found");
                    return;
                }

                // Stop any current preview
                StopCurrentPreview();

                // Pause game music for clear preview audio
                PauseGameMusicForPreview();

                // Use simple MediaPlayer for reliable preview playback
                _currentlyPreviewing = tempPath;
                _previewPlayer = new System.Windows.Media.MediaPlayer();
                
                _previewPlayer.MediaEnded += (s, e) => 
                {
                    Logger.DebugIf(LogPrefix, "Preview MediaEnded event fired");
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateInputFeedback("🎮 Preview ended - Game music resumed - X/Y to play again");
                        StopCurrentPreview();
                    }));
                };
                _previewPlayer.MediaFailed += (s, e) =>
                {
                    Logger.Error($"Preview MediaFailed: {e.ErrorException?.Message ?? "Unknown error"}");
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateInputFeedback($"❌ Preview failed: {e.ErrorException?.Message ?? "Unknown error"}");
                        StopCurrentPreview();
                    }));
                };
                
                _previewPlayer.Volume = 0.7;
                _previewPlayer.Open(new Uri(tempPath));
                _previewPlayer.Play();
                
                Logger.DebugIf(LogPrefix, $"Preview started for: {songName}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error playing preview file: {tempPath}");
                UpdateInputFeedback($"❌ Preview error: {ex.Message}");
                RestoreGameMusic();
            }
        }

        /// <summary>
        /// Get temp path for song preview
        /// </summary>
        private string GetTempPathForPreview(Song song)
        {
            try
            {
                var tempDir = System.IO.Path.Combine(
                    _playniteApi.Paths.ConfigurationPath,
                    "ExtraMetadata", "UniPlaySong", "temp");
                
                if (!System.IO.Directory.Exists(tempDir))
                {
                    System.IO.Directory.CreateDirectory(tempDir);
                }
                
                var sanitizedName = song.Name;
                foreach (var invalidChar in System.IO.Path.GetInvalidFileNameChars())
                {
                    sanitizedName = sanitizedName.Replace(invalidChar, '_');
                }
                
                return System.IO.Path.Combine(tempDir, $"preview_{sanitizedName}.mp3");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting temp path for preview");
                return null;
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HandleConfirmAction();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error confirming controller dialog");
                ShowError("Error processing confirmation");
            }
        }

        /// <summary>
        /// Handle the confirm action based on current dialog step
        /// </summary>
        private void HandleConfirmAction()
        {
            try
            {
                // Block input if a modal dialog is being shown
                if (_isShowingModalDialog)
                {
                    Logger.DebugIf(LogPrefix, "Ignoring confirm action - modal dialog is showing");
                    return;
                }

                // Block input during cooldown period after modal dialogs close
                // This prevents the button press that closed the modal from being detected as a new press
                if (DateTime.Now < _modalCooldownUntil)
                {
                    Logger.DebugIf(LogPrefix, "Ignoring confirm action - modal cooldown active");
                    return;
                }

                var selectedItem = ResultsListBox.SelectedItem as ListBoxItem;
                if (selectedItem?.Tag == null)
                {
                    UpdateInputFeedback("❌ No item selected");
                    return;
                }

                switch (_currentStep)
                {
                    case DialogStep.SourceSelection:
                        HandleSourceSelection(selectedItem);
                        break;
                        
                    case DialogStep.AlbumSelection:
                        HandleAlbumSelection(selectedItem);
                        break;
                        
                    case DialogStep.SongSelection:
                        HandleSongSelection(selectedItem);
                        break;
                        
                    default:
                        Logger.DebugIf(LogPrefix, $"Unhandled dialog step: {_currentStep}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling confirm action");
                ShowError("Error processing selection");
            }
        }

        /// <summary>
        /// Handle source selection (KHInsider vs YouTube)
        /// </summary>
        private void HandleSourceSelection(ListBoxItem selectedItem)
        {
            try
            {
                var sourceItem = selectedItem.Tag as DownloadItemViewModel;
                var sourceOption = sourceItem?.Item as GenericItemOption;
                
                if (sourceOption == null)
                {
                    ShowError("Invalid source selection");
                    return;
                }

                // Convert source name to enum
                if (Enum.TryParse<Source>(sourceOption.Name, out var source))
                {
                    _selectedSource = source;
                    UpdateInputFeedback($"✅ Selected: {sourceOption.Name}");
                    
                    // Move to album selection
                    LoadAlbumSelection(source);
                }
                else
                {
                    ShowError($"Unknown source: {sourceOption.Name}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling source selection");
                ShowError("Error selecting source");
            }
        }

        /// <summary>
        /// Load album selection for the chosen source
        /// </summary>
        private void LoadAlbumSelection(Source source)
        {
            try
            {
                _currentStep = DialogStep.AlbumSelection;
                UpdateInputFeedback($"🔍 Searching {source} for: {_currentGame?.Name}...");
                
                // Perform real search using the download manager
                Task.Run(() =>
                {
                    try
                    {
                        var cancellationToken = new CancellationTokenSource().Token;
                        var gameName = _currentGame?.Name ?? "Unknown Game";

                        Logger.DebugIf(LogPrefix, $"Searching for albums: Game='{gameName}', Source={source}");

                        // First, get hint albums for this game (prioritized at top)
                        var hintAlbums = _downloadManager?.GetHintAlbums(gameName);
                        var hintViewModels = new List<DownloadItemViewModel>();

                        if (hintAlbums != null && hintAlbums.Count > 0)
                        {
                            // Filter hints by current source
                            var sourceHints = hintAlbums.Where(h => h.Source == source).ToList();
                            foreach (var hint in sourceHints)
                            {
                                hintViewModels.Add(new DownloadItemViewModel
                                {
                                    Name = hint.Name,
                                    Description = $"★ UPS Hint [{hint.Source}]",
                                    Item = hint,
                                    Source = hint.Source,
                                    IsFromHint = true
                                });
                            }
                            Logger.DebugIf(LogPrefix, $"Found {sourceHints.Count} hint album(s) for '{gameName}' from {source}");
                        }

                        // Search for real albums using the download manager
                        var albums = _downloadManager?.GetAlbumsForGame(gameName, source, cancellationToken, auto: false);
                        var albumsList = albums?.ToList() ?? new List<Album>();

                        Logger.DebugIf(LogPrefix, $"Found {albumsList.Count} albums for '{gameName}' from {source}");

                        // Convert to view models
                        var albumViewModels = albumsList.Select(a => new DownloadItemViewModel
                        {
                            Name = a.Name,
                            Description = $"{a.Type} • {a.Year} • {a.Count} songs",
                            Item = a,
                            IsFromHint = false
                        }).ToList();

                        // Prepend hint albums at the top
                        if (hintViewModels.Count > 0)
                        {
                            albumViewModels.InsertRange(0, hintViewModels);
                        }

                        // Update UI on main thread
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (albumViewModels.Count > 0)
                                {
                                    PopulateResultsList(albumViewModels);
                                    UpdateInputFeedback($"🎵 Found {albumViewModels.Count} albums for {gameName} - A to select, B to go back, X/Y to preview");
                                }
                                else
                                {
                                    // Show "no results" with option to try different source
                                    var noResultsItem = new List<DownloadItemViewModel>
                                    {
                                        new DownloadItemViewModel
                                        {
                                            Name = "No albums found",
                                            Description = $"No albums found for '{gameName}' on {source}. Try a different source or check the game name.",
                                            Item = null
                                        }
                                    };
                                    PopulateResultsList(noResultsItem);
                                    UpdateInputFeedback($"❌ No albums found for {gameName} on {source} - B to try different source");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, "Error updating UI with album results");
                                ShowError("Error displaying search results");
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error searching for albums");
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                // Show error but don't close dialog - allow user to go back
                                var errorItem = new List<DownloadItemViewModel>
                                {
                                    new DownloadItemViewModel
                                    {
                                        Name = "Search Error",
                                        Description = $"Error searching {source}: {ex.Message}. Press B to go back and try again.",
                                        Item = null
                                    }
                                };
                                PopulateResultsList(errorItem);
                                UpdateInputFeedback($"❌ Search error - B to go back and try again");
                            }
                            catch (Exception uiEx)
                            {
                                Logger.Error(uiEx, "Error handling search error in UI");
                                ShowError("Search failed");
                            }
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error loading album selection");
                ShowError("Error loading albums");
            }
        }

        /// <summary>
        /// Handle album selection
        /// </summary>
        private void HandleAlbumSelection(ListBoxItem selectedItem)
        {
            try
            {
                var albumItem = selectedItem.Tag as DownloadItemViewModel;
                _selectedAlbum = albumItem?.Item as Album;
                
                if (_selectedAlbum == null)
                {
                    ShowError("Invalid album selection");
                    return;
                }

                UpdateInputFeedback($"✅ Selected: {_selectedAlbum.Name}");
                
                // Move to song selection
                LoadSongSelection(_selectedAlbum);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling album selection");
                ShowError("Error selecting album");
            }
        }

        /// <summary>
        /// Load song selection for the chosen album
        /// </summary>
        private void LoadSongSelection(Album album)
        {
            try
            {
                _currentStep = DialogStep.SongSelection;
                UpdateInputFeedback($"🎵 Loading songs from: {album.Name}...");
                
                // Load real songs using the download manager
                Task.Run(() =>
                {
                    try
                    {
                        var cancellationToken = new CancellationTokenSource().Token;
                        
                        Logger.DebugIf(LogPrefix, $"Loading songs from album: {album.Name}");
                        
                        // Load real songs using the download manager
                        var songs = _downloadManager?.GetSongsFromAlbum(album, cancellationToken);
                        var songsList = songs?.ToList() ?? new List<Song>();
                        
                        Logger.DebugIf(LogPrefix, $"Found {songsList.Count} songs from album '{album.Name}'");
                        
                        // Convert to view models
                        var songViewModels = songsList.Select(s => new DownloadItemViewModel
                        {
                            Name = s.Name,
                            Description = FormatSongDescription(s),
                            Item = s
                        }).ToList();

                        // Update UI on main thread
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (songViewModels.Count > 0)
                                {
                                    PopulateResultsList(songViewModels);
                                    UpdateInputFeedback($"🎵 Found {songViewModels.Count} songs - A to download, B to go back, X/Y to preview");
                                }
                                else
                                {
                                    // Show "no songs found"
                                    var noSongsItem = new List<DownloadItemViewModel>
                                    {
                                        new DownloadItemViewModel
                                        {
                                            Name = "No songs found",
                                            Description = $"No songs found in album '{album.Name}'. The album may be empty or unavailable.",
                                            Item = null
                                        }
                                    };
                                    PopulateResultsList(noSongsItem);
                                    UpdateInputFeedback($"❌ No songs found in album - B to go back to albums");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, "Error updating UI with song results");
                                ShowError("Error displaying songs");
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error loading songs from album");
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                // Show error but don't close dialog - allow user to go back
                                var errorItem = new List<DownloadItemViewModel>
                                {
                                    new DownloadItemViewModel
                                    {
                                        Name = "Loading Error",
                                        Description = $"Error loading songs from album: {ex.Message}. Press B to go back and try a different album.",
                                        Item = null
                                    }
                                };
                                PopulateResultsList(errorItem);
                                UpdateInputFeedback($"❌ Loading error - B to go back and try different album");
                            }
                            catch (Exception uiEx)
                            {
                                Logger.Error(uiEx, "Error handling song loading error in UI");
                                ShowError("Failed to load songs");
                            }
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error loading song selection");
                ShowError("Error loading songs");
            }
        }

        /// <summary>
        /// Handle song selection and start download
        /// </summary>
        private void HandleSongSelection(ListBoxItem selectedItem)
        {
            try
            {
                var songItem = selectedItem.Tag as DownloadItemViewModel;
                var selectedSong = songItem?.Item as Song;
                
                if (selectedSong == null)
                {
                    ShowError("Invalid song selection");
                    return;
                }

                _currentStep = DialogStep.Downloading;
                UpdateInputFeedback($"⬇️ Starting download: {selectedSong.Name}...");
                
                // Start real download using the download manager
                Task.Run(() =>
                {
                    try
                    {
                        Logger.DebugIf(LogPrefix, $"Starting download for song: {selectedSong.Name}");
                        
                        // Use the same path logic as regular dialog
                        var musicDir = _fileService.GetGameMusicDirectory(_currentGame);
                        System.IO.Directory.CreateDirectory(musicDir);
                        
                        // Sanitize filename
                        var sanitizedName = selectedSong.Name;
                        foreach (var invalidChar in System.IO.Path.GetInvalidFileNameChars())
                        {
                            sanitizedName = sanitizedName.Replace(invalidChar, '_');
                        }
                        sanitizedName = sanitizedName.Replace("..", "_").Trim();
                        
                        var fileName = $"{sanitizedName}.mp3";
                        var filePath = System.IO.Path.Combine(musicDir, fileName);
                        
                        // Use the download manager to download the song
                        var cancellationToken = new CancellationTokenSource().Token;
                        var success = _downloadManager?.DownloadSong(selectedSong, filePath, cancellationToken) ?? false;
                        
                        // Update UI on main thread
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (success && System.IO.File.Exists(filePath))
                                {
                                    UpdateInputFeedback($"✅ Download completed: {selectedSong.Name}");
                                    Logger.DebugIf(LogPrefix, $"Successfully downloaded: {selectedSong.Name} to {filePath}");

                                    // Invalidate cache so new file is detected
                                    _fileService?.InvalidateCacheForGame(_currentGame);

                                    // Use auto-closing toast popup instead of modal dialog
                                    // This avoids the XInput double-press issues entirely and works in fullscreen
                                    DialogHelper.ShowSuccessToast(
                                        _playniteApi,
                                        $"Downloaded: {selectedSong.Name}",
                                        "Download Complete");

                                    // Trigger auto-normalize if enabled (after download message dismissed)
                                    try
                                    {
                                        if (_dialogService != null)
                                        {
                                            var downloadedFiles = new List<string> { filePath };
                                            _dialogService.AutoNormalizeDownloadedFiles(downloadedFiles);
                                        }
                                    }
                                    catch (Exception normalizeEx)
                                    {
                                        Logger.DebugIf(LogPrefix, normalizeEx, "Error during auto-normalize after download");
                                    }

                                    // Trigger music refresh so music plays immediately after download/normalize
                                    try
                                    {
                                        if (_playbackService != null && _currentGame != null)
                                        {
                                            // IMPORTANT: Stop any preview first to prevent overlap
                                            // This clears _currentlyPreviewing and _previewPlayer, and resets _wasGameMusicPlaying
                                            // so that RestoreGameMusic() won't interfere with our PlayGameMusic call
                                            StopCurrentPreview();

                                            // Reset the flag since we're about to start fresh game music
                                            // (StopCurrentPreview would have tried to Resume, but we want a fresh start)
                                            _wasGameMusicPlaying = false;

                                            Logger.DebugIf(LogPrefix, $"Download complete - triggering music refresh for game: {_currentGame.Name}");
                                            // Use forceReload: true to ensure newly downloaded song plays
                                            // Pass null settings - playback service uses its cached _currentSettings
                                            _playbackService.PlayGameMusic(_currentGame, null, forceReload: true);
                                        }
                                    }
                                    catch (Exception refreshEx)
                                    {
                                        Logger.DebugIf(LogPrefix, refreshEx, "Error refreshing music after download");
                                    }
                                    // Return to song selection (same album) so user can download more songs
                                    Logger.DebugIf(LogPrefix, "Returning to song selection for more downloads");
                                    _currentStep = DialogStep.SongSelection;
                                    UpdateInputFeedback("✅ Downloaded! Select another song or B to go back");
                                }
                                else
                                {
                                    UpdateInputFeedback($"❌ Download failed: {selectedSong.Name}");
                                    ShowError($"Failed to download: {selectedSong.Name}");

                                    // Return to song selection on failure so user can retry
                                    _currentStep = DialogStep.SongSelection;
                                    UpdateInputFeedback("🎵 Download failed - A to retry, B to go back");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, "Error handling download result");
                                ShowError("Error processing download result");
                                CloseDialog(false);
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error during download");
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            ShowError($"Download failed: {ex.Message}");
                            _currentStep = DialogStep.SongSelection; // Return to song selection
                            UpdateInputFeedback("🎵 Download failed - A to retry, B to go back");
                        }));
                    }
                });
                
                Logger.DebugIf(LogPrefix, $"Initiated download for song: {selectedSong.Name}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling song selection");
                ShowError("Error starting download");
            }
        }

        /// <summary>
        /// Handle keyboard/controller input
        /// Xbox controller buttons are typically mapped to these keys by Windows or controller software
        /// </summary>
        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                switch (e.Key)
                {
                    // A button (Enter/Space) - Confirm
                    case System.Windows.Input.Key.Enter:
                    case System.Windows.Input.Key.Space:
                        ConfirmButton_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        break;
                        
                    // B button (Escape/Backspace) - Cancel
                    case System.Windows.Input.Key.Escape:
                    case System.Windows.Input.Key.Back:
                        CancelButton_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        break;
                        
                    // Y button (F1) - Preview
                    case System.Windows.Input.Key.F1:
                        HandlePreviewAction();
                        e.Handled = true;
                        break;
                        
                    // D-Pad Up/Down (Arrow keys) - Navigate list
                    // Use same debounce as XInput D-pad to prevent double navigation
                    // when both keyboard and XInput fire for the same D-pad press
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
                        
                    // Left/Right shoulder buttons (Page Up/Down) - Page through list
                    case System.Windows.Input.Key.PageUp:
                        NavigateList(-5);
                        e.Handled = true;
                        break;
                        
                    case System.Windows.Input.Key.PageDown:
                        NavigateList(5);
                        e.Handled = true;
                        break;
                        
                    // Left/Right triggers (Home/End) - Jump to top/bottom
                    case System.Windows.Input.Key.Home:
                        JumpToItem(0);
                        e.Handled = true;
                        break;
                        
                    case System.Windows.Input.Key.End:
                        JumpToItem(-1);
                        e.Handled = true;
                        break;
                }
                
                // Update feedback text to show input was received
                UpdateInputFeedback($"Last input: {e.Key} ✓");
                
                Logger.DebugIf(LogPrefix, $"Controller input: {e.Key}");
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
        /// Navigate the list by a number of items
        /// </summary>
        private void NavigateList(int offset)
        {
            try
            {
                if (ResultsListBox?.Items.Count > 0)
                {
                    int currentIndex = ResultsListBox.SelectedIndex;
                    int newIndex = Math.Max(0, Math.Min(ResultsListBox.Items.Count - 1, currentIndex + offset));

                    ResultsListBox.SelectedIndex = newIndex;
                    ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);

                    // Ensure the ListBox has focus for visual feedback
                    ResultsListBox.Focus();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error navigating list");
            }
        }

        /// <summary>
        /// Jump to specific item (0 = first, -1 = last)
        /// </summary>
        private void JumpToItem(int index)
        {
            try
            {
                if (ResultsListBox?.Items.Count > 0)
                {
                    if (index < 0)
                        index = ResultsListBox.Items.Count - 1;
                    
                    index = Math.Max(0, Math.Min(ResultsListBox.Items.Count - 1, index));
                    
                    ResultsListBox.SelectedIndex = index;
                    ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                    ResultsListBox.Focus();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error jumping to item");
            }
        }

        /// <summary>
        /// Update the input feedback text to show controller input is working
        /// </summary>
        private void UpdateInputFeedback(string message)
        {
            try
            {
                if (InputFeedback != null)
                {
                    InputFeedback.Text = message;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating input feedback");
            }
        }

        #region Xbox Controller Support

        private XInputWrapper.XINPUT_STATE _lastControllerState;
        private bool _hasLastState = false;

        /// <summary>
        /// Start monitoring Xbox controller input
        /// </summary>
        private void StartControllerMonitoring()
        {
            if (_isMonitoring) return;

            try
            {
                _isMonitoring = true;
                _controllerMonitoringCancellation = new CancellationTokenSource();

                // Initialize _lastControllerState with current controller state to prevent
                // detecting held buttons from previous dialogs as new presses
                try
                {
                    XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                    if (XInputWrapper.XInputGetState(0, ref state) == 0)
                    {
                        _lastControllerState = state;
                        _hasLastState = true;
                        Logger.DebugIf(LogPrefix, $"Initialized controller state: buttons={state.Gamepad.wButtons}");
                    }
                }
                catch { /* Ignore initialization errors */ }

                Task.Run(async () =>
                {
                    Logger.DebugIf(LogPrefix, "Starting Xbox controller monitoring");

                    // Small delay to let any held buttons be released
                    await Task.Delay(150);
                    
                    while (!_controllerMonitoringCancellation.Token.IsCancellationRequested)
                    {
                        try
                        {
                            // Check controller 0 (first controller)
                            XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                            int result = XInputWrapper.XInputGetState(0, ref state);
                            
                            if (result == 0) // Success
                            {
                                // Check for button presses (only on state change)
                                if (_hasLastState && state.dwPacketNumber != _lastControllerState.dwPacketNumber)
                                {
                                    CheckButtonPresses(state.Gamepad, _lastControllerState.Gamepad);
                                }
                                
                                _lastControllerState = state;
                                _hasLastState = true;
                            }
                            
                            await Task.Delay(33, _controllerMonitoringCancellation.Token); // Check every 33ms (~30Hz) for smoother polling
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            Logger.DebugIf(LogPrefix, ex, "Error in controller monitoring loop");
                            await Task.Delay(1000, _controllerMonitoringCancellation.Token); // Wait longer on error
                        }
                    }
                    
                    Logger.DebugIf(LogPrefix, "Xbox controller monitoring stopped");
                }, _controllerMonitoringCancellation.Token);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to start controller monitoring");
                _isMonitoring = false;
            }
        }

        /// <summary>
        /// Stop monitoring Xbox controller input
        /// </summary>
        private void StopControllerMonitoring()
        {
            try
            {
                _isMonitoring = false;
                _controllerMonitoringCancellation?.Cancel();
                _controllerMonitoringCancellation?.Dispose();
                _controllerMonitoringCancellation = null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error stopping controller monitoring");
            }
        }

        /// <summary>
        /// Refresh controller state and activate cooldown to prevent stale button states from triggering actions.
        /// Call this after modal dialogs close to re-sync the controller state and block immediate re-detection.
        /// </summary>
        private void RefreshControllerStateWithCooldown()
        {
            try
            {
                XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                if (XInputWrapper.XInputGetState(0, ref state) == 0)
                {
                    _lastControllerState = state;
                    _hasLastState = true;
                    Logger.DebugIf(LogPrefix, $"Refreshed controller state after dialog: buttons={state.Gamepad.wButtons}");
                }

                // Set cooldown to block any input processing for ModalCooldownMs
                // This is the key defense against race conditions
                _modalCooldownUntil = DateTime.Now.AddMilliseconds(ModalCooldownMs);
                Logger.DebugIf(LogPrefix, $"Modal cooldown active until {_modalCooldownUntil:HH:mm:ss.fff}");
            }
            catch (Exception ex)
            {
                Logger.DebugIf(LogPrefix, ex, "Error refreshing controller state");
                // Still set cooldown even on error
                _modalCooldownUntil = DateTime.Now.AddMilliseconds(ModalCooldownMs);
            }
        }

        /// <summary>
        /// Check for button presses and handle them
        /// </summary>
        private void CheckButtonPresses(XInputWrapper.XINPUT_GAMEPAD currentState, XInputWrapper.XINPUT_GAMEPAD lastState)
        {
            try
            {
                // Get newly pressed buttons (buttons that are pressed now but weren't before)
                ushort newlyPressed = (ushort)(currentState.wButtons & ~lastState.wButtons);

                if (newlyPressed == 0) return; // No new button presses

                // Dispatch to UI thread
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if ((newlyPressed & XInputWrapper.XINPUT_GAMEPAD_A) != 0)
                        {
                            UpdateInputFeedback("Xbox A Button ✓");
                            ConfirmButton_Click(this, new RoutedEventArgs());
                        }
                        else if ((newlyPressed & XInputWrapper.XINPUT_GAMEPAD_B) != 0)
                        {
                            UpdateInputFeedback("Xbox B Button ✓");
                            CancelButton_Click(this, new RoutedEventArgs());
                        }
                        else if ((newlyPressed & XInputWrapper.XINPUT_GAMEPAD_X) != 0)
                        {
                            UpdateInputFeedback("Xbox X Button ✓");
                            HandlePreviewAction();
                        }
                        else if ((newlyPressed & XInputWrapper.XINPUT_GAMEPAD_Y) != 0)
                        {
                            UpdateInputFeedback("Xbox Y Button ✓");
                            HandlePreviewAction();
                        }
                        else if ((newlyPressed & XInputWrapper.XINPUT_GAMEPAD_DPAD_UP) != 0)
                        {
                            if (TryDpadNavigation())
                            {
                                UpdateInputFeedback("Xbox D-Pad Up ✓");
                                NavigateList(-1);
                            }
                        }
                        else if ((newlyPressed & XInputWrapper.XINPUT_GAMEPAD_DPAD_DOWN) != 0)
                        {
                            if (TryDpadNavigation())
                            {
                                UpdateInputFeedback("Xbox D-Pad Down ✓");
                                NavigateList(1);
                            }
                        }
                        else if ((newlyPressed & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0)
                        {
                            UpdateInputFeedback("Xbox Left Bumper ✓");
                            NavigateList(-5);
                        }
                        else if ((newlyPressed & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0)
                        {
                            UpdateInputFeedback("Xbox Right Bumper ✓");
                            NavigateList(5);
                        }
                        
                        // Check triggers (they're analog, so we check if they're pressed above threshold)
                        if (currentState.bLeftTrigger > 128 && lastState.bLeftTrigger <= 128)
                        {
                            UpdateInputFeedback("Xbox Left Trigger ✓");
                            JumpToItem(0);
                        }
                        else if (currentState.bRightTrigger > 128 && lastState.bRightTrigger <= 128)
                        {
                            UpdateInputFeedback("Xbox Right Trigger ✓");
                            JumpToItem(-1);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error handling controller button press");
                    }
                }));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error checking button presses");
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Formats the song description, avoiding duplicate "MB" suffix
        /// </summary>
        private static string FormatSongDescription(Song song)
        {
            var lengthPart = song.Length.HasValue ? song.Length.Value.ToString() : "";
            var sizePart = song.SizeInMb ?? "";

            // SizeInMb from KHInsider already contains "MB", so don't append it again
            // For YouTube, SizeInMb is typically empty, so we skip the size part
            if (!string.IsNullOrWhiteSpace(sizePart))
            {
                // Strip existing "MB" suffix if present (case-insensitive) and re-add consistently
                var sizeValue = sizePart.Trim();
                if (sizeValue.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
                {
                    sizeValue = sizeValue.Substring(0, sizeValue.Length - 2).Trim();
                }

                // Try to parse as number for consistent formatting
                if (double.TryParse(sizeValue, out double sizeNum))
                {
                    sizePart = $"{sizeNum:F2} MB";
                }
                else
                {
                    // Keep original if not parseable, but ensure single MB suffix
                    sizePart = $"{sizeValue} MB";
                }
            }

            if (!string.IsNullOrWhiteSpace(lengthPart) && !string.IsNullOrWhiteSpace(sizePart))
            {
                return $"{lengthPart} • {sizePart}";
            }
            else if (!string.IsNullOrWhiteSpace(lengthPart))
            {
                return lengthPart;
            }
            else if (!string.IsNullOrWhiteSpace(sizePart))
            {
                return sizePart;
            }

            return "";
        }

        #endregion
    }
}