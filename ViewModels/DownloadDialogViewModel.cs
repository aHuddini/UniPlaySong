using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Downloaders;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.ViewModels
{
    /// <summary>
    /// View model for the download dialog (album/song selection)
    /// </summary>
    public class DownloadDialogViewModel : ObservableObject
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

        private readonly IPlayniteAPI _playniteApi;
        private readonly IDownloadManager _downloadManager;
        private readonly IMusicPlaybackService _playbackService;
        private readonly GameMusicFileService _fileService;
        private readonly ErrorHandlerService _errorHandler;
        private readonly Game _game;
        private readonly Source _source;
        private readonly bool _isSongSelection;
        private readonly Album _album; // Only set when selecting songs

        private string _title;
        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                OnPropertyChanged();
            }
        }

        private string _searchTerm = string.Empty;
        public string SearchTerm
        {
            get => _searchTerm;
            set
            {
                _searchTerm = value ?? string.Empty;
                OnPropertyChanged();
                // Update can execute for search command
                if (SearchCommand is Common.RelayCommand cmd)
                {
                    cmd.RaiseCanExecuteChanged();
                }
            }
        }

        private ObservableCollection<DownloadItemViewModel> _searchResults = new ObservableCollection<DownloadItemViewModel>();
        public ObservableCollection<DownloadItemViewModel> SearchResults
        {
            get => _searchResults;
            set
            {
                _searchResults = value;
                OnPropertyChanged();
            }
        }

        private List<DownloadItemViewModel> _selectedItems = new List<DownloadItemViewModel>();
        public List<DownloadItemViewModel> SelectedItems
        {
            get => _selectedItems;
            set
            {
                _selectedItems = value ?? new List<DownloadItemViewModel>();
                OnPropertyChanged();
                if (ConfirmCommand is Common.RelayCommand cmd)
                {
                    cmd.RaiseCanExecuteChanged();
                }
            }
        }

        private System.Windows.Controls.SelectionMode _selectionMode = System.Windows.Controls.SelectionMode.Single;
        public System.Windows.Controls.SelectionMode SelectionMode
        {
            get => _selectionMode;
            set
            {
                _selectionMode = value;
                OnPropertyChanged();
            }
        }

        private bool _showCheckboxes = false;
        public bool ShowCheckboxes
        {
            get => _showCheckboxes;
            set
            {
                _showCheckboxes = value;
                OnPropertyChanged();
            }
        }

        private bool _showPreview = true;
        public bool ShowPreview
        {
            get => _showPreview;
            set
            {
                _showPreview = value;
                OnPropertyChanged();
            }
        }

        private bool _isSearching = false;
        public bool IsSearching
        {
            get => _isSearching;
            set
            {
                _isSearching = value;
                OnPropertyChanged();
            }
        }

        private bool _isDownloading = false;
        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                _isDownloading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowProgress));
            }
        }

        // Combined property for progress bar visibility (searching OR downloading)
        public bool ShowProgress
        {
            get => IsSearching || IsDownloading;
        }

        private string _confirmButtonText = "CONFIRM";
        public string ConfirmButtonText
        {
            get => _confirmButtonText;
            set
            {
                _confirmButtonText = value;
                OnPropertyChanged();
            }
        }

        private bool _isIndeterminate = true;
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set
            {
                _isIndeterminate = value;
                OnPropertyChanged();
            }
        }

        private double _progressValue = 0;
        public double ProgressValue
        {
            get => _progressValue;
            set
            {
                _progressValue = value;
                OnPropertyChanged();
            }
        }

        private double _progressMax = 100;
        public double ProgressMax
        {
            get => _progressMax;
            set
            {
                _progressMax = value;
                OnPropertyChanged();
            }
        }

        private string _progressText = string.Empty;
        public string ProgressText
        {
            get => _progressText;
            set
            {
                _progressText = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        // Cancellation token is now provided by GlobalProgressActionArgs in SearchForResults

        public ICommand SearchCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand ConfirmCommand { get; set; }
        public ICommand CancelCommand { get; set; }
        public ICommand BackCommand { get; set; }
        
        // Action to call when download completes (set by dialog service)
        public Action<bool> OnDownloadComplete { get; set; }
        
        private bool _showBackButton = false;
        public bool ShowBackButton
        {
            get => _showBackButton;
            set
            {
                _showBackButton = value;
                OnPropertyChanged();
            }
        }

        public DownloadDialogViewModel(
            IPlayniteAPI playniteApi,
            IDownloadManager downloadManager,
            IMusicPlaybackService playbackService,
            Game game,
            Source source,
            bool isSongSelection = false,
            Album album = null,
            GameMusicFileService fileService = null,
            ErrorHandlerService errorHandler = null)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _downloadManager = downloadManager ?? throw new ArgumentNullException(nameof(downloadManager));
            _playbackService = playbackService;
            _fileService = fileService;
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _game = game; // Can be null for source selection
            _source = source;
            _isSongSelection = isSongSelection;
            _album = album;

            // Set title based on context
            if (game != null)
            {
                Title = isSongSelection 
                    ? $"Select Songs from {album?.Name ?? "Album"}"
                    : $"Select Album for {game.Name}";
            }
            else
            {
                Title = "Select Download Source";
            }

            SelectionMode = isSongSelection 
                ? System.Windows.Controls.SelectionMode.Multiple 
                : System.Windows.Controls.SelectionMode.Single;
            
            ShowCheckboxes = isSongSelection;
            ShowPreview = isSongSelection; // Only show preview for songs, not albums

            // Set confirm button text based on context
            ConfirmButtonText = isSongSelection ? "DOWNLOAD" : "CONFIRM";

            SearchCommand = new Common.RelayCommand(() => PerformSearch());
            PreviewCommand = new Common.RelayCommand<DownloadItemViewModel>(PreviewItem);
            // ConfirmCommand and CancelCommand will be set by the dialog service

            // Don't auto-search in constructor - let the window appear first
            // Search will be triggered when window loads (similar to PNS pattern)
            // This prevents blocking the UI thread and ensures window is responsive
        }

        /// <summary>
        /// Sets source options for source selection dialog
        /// </summary>
        public void SetSourceOptions(List<Playnite.SDK.GenericItemOption> options)
        {
            SearchResults.Clear();
            foreach (var option in options)
            {
                SearchResults.Add(new DownloadItemViewModel
                {
                    Name = option.Name,
                    Description = option.Description,
                    Item = option
                });
            }
            ShowCheckboxes = false;
            ShowPreview = false;
            Title = "Select Download Source";
        }

        public void PerformSearch()
        {
            // Use inline progress bar instead of overlay
            // Use SearchTerm if provided, otherwise use game name
            // For default music, SearchTerm should be provided by user
            var searchKeyword = !string.IsNullOrWhiteSpace(SearchTerm) ? SearchTerm : (_game?.Name ?? string.Empty);
            
            // Don't search if keyword is empty (user needs to type something)
            if (string.IsNullOrWhiteSpace(searchKeyword) && !_isSongSelection)
            {
                LogDebug("PerformSearch called but no search term provided - waiting for user input");
                return;
            }
            
            var searchText = _isSongSelection ? "Loading songs..." : $"Searching albums for '{searchKeyword}'...";
            
            // Show inline progress bar
            IsSearching = true;
            IsIndeterminate = true;
            ProgressText = searchText;
            ProgressValue = 0;
            ProgressMax = 100;
            
            // Run search asynchronously to avoid blocking UI
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var cancellationTokenSource = new System.Threading.CancellationTokenSource();
                    var results = SearchForResults(searchKeyword, cancellationTokenSource.Token);
                    
                    // Update UI on dispatcher thread
                    var app = System.Windows.Application.Current;
                    if (app?.Dispatcher != null)
                    {
                        app.Dispatcher.Invoke(() =>
                        {
                            SearchResults = results;
                            IsSearching = false;
                            ProgressText = string.Empty;
                        });
                    }
                }
                catch (Exception ex)
                {
                    _errorHandler.HandleError(
                        ex,
                        context: "performing search",
                        showUserMessage: false
                    );
                    
                    var app = System.Windows.Application.Current;
                    if (app?.Dispatcher != null)
                    {
                        app.Dispatcher.Invoke(() =>
                        {
                            IsSearching = false;
                            ProgressText = $"Error: {ex.Message}";
                        });
                    }
                }
            });
        }

        private ObservableCollection<DownloadItemViewModel> SearchForResults(string keyword, CancellationToken cancellationToken)
        {
            List<DownloadItemViewModel> results;
            try
            {
                if (_isSongSelection && _album != null)
                {
                    // Load songs from album
                    LogDebug($"Loading songs from album: {_album.Name}");
                    var songs = _downloadManager.GetSongsFromAlbum(_album, cancellationToken);
                    results = songs.Select(s => new DownloadItemViewModel
                    {
                        Name = s.Name,
                        Description = FormatSongDescription(s),
                        Item = s,
                        Source = s.Source
                    }).ToList();
                    LogDebug($"Found {results.Count} songs from album");
                }
                else
                {
                    // Search for albums
                    // Use keyword (SearchTerm) if provided, otherwise use game name
                    // For default music downloads, game name might be empty, so keyword is required
                    var gameName = string.IsNullOrWhiteSpace(keyword) 
                        ? (_game?.Name ?? string.Empty) 
                        : keyword;
                    
                    if (string.IsNullOrWhiteSpace(gameName))
                    {
                        Logger.Warn("Cannot search for albums: no game name or search term provided");
                        results = new List<DownloadItemViewModel>();
                        return new ObservableCollection<DownloadItemViewModel>(results);
                    }
                    
                    LogDebug($"Searching for albums: Game='{gameName}', Source={_source}");

                    // Manual search mode: auto=false means no whitelist filtering
                    var albums = _downloadManager.GetAlbumsForGame(gameName, _source, cancellationToken, auto: false);

                    // IEnumerable is never null, but can be empty
                    var albumsList = albums?.ToList() ?? new List<Album>();
                    LogDebug($"Found {albumsList.Count} albums for '{gameName}' from {_source}");
                    
                    results = albumsList.Select(a => new DownloadItemViewModel
                    {
                        Name = a.Name,
                        Description = _source == Source.All
                            ? $"[{a.Source}] {a.Type} • {a.Year} • {a.Count} songs"
                            : $"{a.Type} • {a.Year} • {a.Count} songs",
                        Item = a,
                        Source = a.Source
                    }).ToList();
                    
                    if (results.Count == 0)
                    {
                        Logger.Warn($"No albums found for '{gameName}' from {_source}. This might indicate a search error.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Search was cancelled, ignore
                LogDebug("Search was cancelled");
                results = new List<DownloadItemViewModel>();
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(
                    ex,
                    context: $"searching for results (keyword: '{keyword}', isSongSelection: {_isSongSelection})",
                    showUserMessage: false // Don't show error dialog here - ActivateGlobalProgress handles it
                );
                results = new List<DownloadItemViewModel>();
            }

            var collection = new ObservableCollection<DownloadItemViewModel>(results ?? new List<DownloadItemViewModel>());
            
            // Update progress text after search completes
            if (!_isSongSelection && results != null)
            {
                var gameName = string.IsNullOrWhiteSpace(keyword) ? (_game?.Name ?? "Unknown") : keyword;
                if (results.Count == 0)
                {
                    ProgressText = $"No albums found for '{gameName}'";
                }
                else
                {
                    ProgressText = $"Found {results.Count} album(s)";
                }
            }
            
            return collection;
        }

        private void PreviewItem(DownloadItemViewModel item)
        {
            if (item?.Item is Song song)
            {
                PreviewSong(song);
            }
        }

        private string _currentlyPreviewing = null;
        private IMusicPlayer _previewPlayer;
        private readonly List<string> _previewFiles = new List<string>();
        private DateTime _lastPreviewRequestTime = DateTime.MinValue;
        private const int MinPreviewIntervalMs = 2000; // Minimum 2 seconds between preview requests to avoid rate limiting

        private void PreviewSong(Song song)
        {
            try
            {
                // Rate limiting: Prevent rapid preview requests to avoid server rate limits
                var timeSinceLastRequest = (DateTime.Now - _lastPreviewRequestTime).TotalMilliseconds;
                if (timeSinceLastRequest < MinPreviewIntervalMs)
                {
                    LogDebug($"Preview request rate limited - {MinPreviewIntervalMs - timeSinceLastRequest:F0}ms remaining");
                    return;
                }
                _lastPreviewRequestTime = DateTime.Now;

                // Stop any current preview
                StopPreview();

                // Get temp path for preview
                var tempPath = GetTempPathForPreview(song);

                // If already previewing this song, toggle it off
                if (tempPath == _currentlyPreviewing)
                {
                    StopPreview();
                    return;
                }

                // Download to temp if needed
                if (!System.IO.File.Exists(tempPath))
                {
                    IsSearching = true;
                    IsIndeterminate = true;
                    ProgressText = $"Downloading preview: {song.Name}...";

                    // Use inline progress bar for preview download
                    bool downloaded = false;
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            var cancellationTokenSource = new System.Threading.CancellationTokenSource();
                            // Pass isPreview=true to optimize download speed
                            downloaded = _downloadManager.DownloadSong(song, tempPath, cancellationTokenSource.Token, isPreview: true);
                            
                            var app = System.Windows.Application.Current;
                            if (app?.Dispatcher != null)
                            {
                                app.Dispatcher.Invoke(() =>
                                {
                                    IsSearching = false;
                                    ProgressText = string.Empty;
                                    
                                    if (downloaded && System.IO.File.Exists(tempPath))
                                    {
                                        // Play preview after download completes
                                        PlayPreviewFile(tempPath);
                                    }
                                    else
                                    {
                                        Logger.Error($"Preview download failed for {song.Name} - file does not exist at {tempPath}");
                                        _playniteApi.Dialogs.ShowErrorMessage($"Failed to download preview for {song.Name}. Check logs for details.", "UniPlaySong");
                                    }
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            _errorHandler.HandleError(
                                ex,
                                context: $"downloading preview for song '{song.Name}'",
                                showUserMessage: true
                            );
                            
                            var app = System.Windows.Application.Current;
                            if (app?.Dispatcher != null)
                            {
                                app.Dispatcher.Invoke(() =>
                                {
                                    IsSearching = false;
                                    ProgressText = string.Empty;
                                });
                            }
                        }
                    });
                    
                    // Wait for download to complete before continuing
                    return;
                }

                // Play preview if file already exists
                PlayPreviewFile(tempPath);
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(
                    ex,
                    context: "previewing song",
                    showUserMessage: true
                );
                StopPreview();
            }
        }

        private void PlayPreviewFile(string tempPath)
        {
            try
            {
                if (!System.IO.File.Exists(tempPath))
                {
                    return;
                }

                // Track this preview file for cleanup
                if (!_previewFiles.Contains(tempPath))
                {
                    _previewFiles.Add(tempPath);
                }

                // Play preview
                _currentlyPreviewing = tempPath;
                _previewPlayer = new Services.MusicPlayer();
                _previewPlayer.MediaEnded += (s, e) => StopPreview();
                _previewPlayer.MediaFailed += (s, e) =>
                {
                    _playniteApi.Dialogs.ShowErrorMessage($"Preview playback failed: {e.ErrorException?.Message ?? "Unknown error"}", "UniPlaySong");
                    StopPreview();
                };
                _previewPlayer.Volume = 0.7; // Preview at 70% volume
                _previewPlayer.Load(tempPath);
                _previewPlayer.Play();
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(
                    ex,
                    context: "playing preview",
                    showUserMessage: true
                );
                StopPreview();
            }
        }

        public void StopPreview()
        {
            try
            {
                if (_previewPlayer != null)
                {
                    _previewPlayer.Stop();
                    _previewPlayer.Close();
                    _previewPlayer = null;
                }
                
                // Delete the current preview file if it exists
                if (!string.IsNullOrEmpty(_currentlyPreviewing))
                {
                    try
                    {
                        if (System.IO.File.Exists(_currentlyPreviewing))
                        {
                            System.IO.File.Delete(_currentlyPreviewing);
                        }
                        _previewFiles.Remove(_currentlyPreviewing);
                    }
                    catch
                    {
                        // File may be locked or already deleted - ignore
                    }
                }
                
                _currentlyPreviewing = null;
            }
            catch { }
        }

        private string GetTempPathForPreview(Song song)
        {
            var tempDir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "UniPlaySong",
                "Preview");
            System.IO.Directory.CreateDirectory(tempDir);

            // Create hash from song ID and source to ensure unique temporary filenames
            // Note: SHA256 is used here ONLY for filename generation (not for security/encryption)
            // It provides deterministic, collision-resistant filenames from song metadata
            var hashInput = $"{song.Source}:{song.Id}";
            var hash = BitConverter.ToString(
                System.Security.Cryptography.SHA256.Create()
                    .ComputeHash(System.Text.Encoding.UTF8.GetBytes(hashInput)))
                .Replace("-", "");

            // For KHInsider, song.Id is a relative path without extension
            // Default to .mp3 and let the downloader determine the actual format
            var extension = System.IO.Path.GetExtension(song.Id);
            if (string.IsNullOrEmpty(extension))
            {
                // Default to .mp3 for previews (most common format)
                // The actual file extension will be determined during download
                extension = ".mp3";
            }

            return System.IO.Path.Combine(tempDir, hash + extension);
        }

        /// <summary>
        /// Cleans up all tracked preview files. Should be called when dialog closes.
        /// </summary>
        public void CleanupPreviewFiles()
        {
            try
            {
                // Stop any active preview first
                StopPreview();
                
                // Delete all tracked preview files
                foreach (var filePath in _previewFiles.ToList())
                {
                    try
                    {
                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                        }
                    }
                    catch
                    {
                        // File may be locked or already deleted - ignore
                    }
                }
                
                _previewFiles.Clear();
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }

        public List<object> GetSelectedItems()
        {
            if (SelectedItems == null || SelectedItems.Count == 0)
                return new List<object>();

            return SelectedItems.Select(vm => vm.Item).ToList();
        }

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

        /// <summary>
        /// Handles double-click on an item (opens album tracks if it's an album)
        /// </summary>
        public void HandleDoubleClick(DownloadItemViewModel item)
        {
            if (item == null)
                return;

            if (!_isSongSelection && item.Item is Album album)
            {
                // Double-clicked an album - open song selection
                // This will be handled by the dialog service
                OnAlbumDoubleClicked?.Invoke(album);
            }
            else if (_isSongSelection && item.Item is Song song)
            {
                // Double-click on song - toggle selection
                if (SelectedItems.Contains(item))
                {
                    SelectedItems.Remove(item);
                }
                else
                {
                    SelectedItems.Add(item);
                }
                OnPropertyChanged(nameof(SelectedItems));
            }
        }

        /// <summary>
        /// Event raised when an album is double-clicked
        /// </summary>
        public event Action<Album> OnAlbumDoubleClicked;

        /// <summary>
        /// Downloads selected songs inline, showing progress in the status bar
        /// </summary>
        public void DownloadSelectedSongs()
        {
            if (_fileService == null || _game == null || !_isSongSelection)
            {
                return;
            }

            var selected = GetSelectedItems().OfType<Song>().ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var musicDir = _fileService.GetGameMusicDirectory(_game);
            System.IO.Directory.CreateDirectory(musicDir);

            var total = selected.Count;
            var downloaded = 0;
            var failed = 0;

            // Show download progress
            IsDownloading = true;
            IsIndeterminate = false;
            ProgressMax = total;
            ProgressValue = 0;
            ProgressText = $"Downloading {total} song(s)...";
            
            // Disable confirm button during download
            if (ConfirmCommand is Common.RelayCommand cmd)
            {
                cmd.RaiseCanExecuteChanged();
            }

            // Run download asynchronously
            System.Threading.Tasks.Task.Run(() =>
            {
                var app = System.Windows.Application.Current;
                try
                {
                    var cancellationTokenSource = new System.Threading.CancellationTokenSource();

                    foreach (var song in selected)
                    {
                        if (cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            break;
                        }

                        if (app?.Dispatcher != null)
                        {
                            app.Dispatcher.Invoke(() =>
                            {
                                ProgressText = $"Downloading: {song.Name} ({downloaded + 1}/{total})";
                            });
                        }

                        // Sanitize filename
                        var sanitizedName = song.Name;
                        foreach (var invalidChar in System.IO.Path.GetInvalidFileNameChars())
                        {
                            sanitizedName = sanitizedName.Replace(invalidChar, '_');
                        }
                        sanitizedName = sanitizedName.Replace("..", "_").Trim();

                        var fileName = $"{sanitizedName}.mp3";
                        var filePath = System.IO.Path.Combine(musicDir, fileName);

                        var success = _downloadManager.DownloadSong(song, filePath, cancellationTokenSource.Token);
                        
                        if (success && System.IO.File.Exists(filePath))
                        {
                            downloaded++;
                        }
                        else
                        {
                            failed++;
                        }

                        // Update progress
                        if (app?.Dispatcher != null)
                        {
                            app.Dispatcher.Invoke(() =>
                            {
                                ProgressValue = downloaded;
                            });
                        }
                    }

                    // Show completion status
                    if (app?.Dispatcher != null)
                    {
                        app.Dispatcher.Invoke(() =>
                        {
                            if (downloaded == total)
                            {
                                ProgressText = $"✓ Successfully downloaded all {downloaded} song(s)!";
                            }
                            else if (downloaded > 0)
                            {
                                ProgressText = $"Downloaded {downloaded}/{total} song(s) ({failed} failed)";
                            }
                            else
                            {
                                ProgressText = $"Download failed for all {total} song(s)";
                            }

                            // Hide progress after a delay, then notify completion
                            System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                            {
                                if (app?.Dispatcher != null)
                                {
                                    app.Dispatcher.Invoke(() =>
                                    {
                                        IsDownloading = false;
                                        ProgressText = string.Empty;
                                        
                                        // Re-enable confirm button
                                        if (ConfirmCommand is Common.RelayCommand confirmCmd)
                                        {
                                            confirmCmd.RaiseCanExecuteChanged();
                                        }
                                        
                                        OnDownloadComplete?.Invoke(downloaded > 0);
                                    });
                                }
                            });
                        });
                    }
                }
                catch (Exception ex)
                {
                    _errorHandler.HandleError(
                        ex,
                        context: "downloading songs",
                        showUserMessage: false // Progress dialog handles user messages
                    );

                    if (app?.Dispatcher != null)
                    {
                        app.Dispatcher.Invoke(() =>
                        {
                            IsDownloading = false;
                            ProgressText = $"Error: {ex.Message}";
                            
                            // Re-enable confirm button
                            if (ConfirmCommand is Common.RelayCommand confirmCmd)
                            {
                                confirmCmd.RaiseCanExecuteChanged();
                            }
                            
                            OnDownloadComplete?.Invoke(false);
                        });
                    }
                }
            });
        }
    }

    /// <summary>
    /// View model for individual download items (albums/songs) in the list
    /// </summary>
    public class DownloadItemViewModel : ObservableObject
    {
        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        private string _description;
        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged();
            }
        }

        public object Item { get; set; }
        public Source Source { get; set; }
    }
}

