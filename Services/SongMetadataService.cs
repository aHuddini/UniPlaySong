using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using TagLib;
using UniPlaySong.Common;
using UniPlaySong.DeskMediaControl;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Service for reading and caching song metadata (title, artist, duration).
    /// Uses TagLib# for reading ID3 tags and other audio metadata formats.
    /// Reads metadata asynchronously to avoid blocking playback.
    /// </summary>
    public class SongMetadataService
    {
        private readonly FileLogger _fileLogger;
        private IMusicPlaybackService _playbackService;

        // LRU-style cache for metadata (keyed by file path)
        private static readonly ConcurrentDictionary<string, SongInfo> _metadataCache =
            new ConcurrentDictionary<string, SongInfo>(StringComparer.OrdinalIgnoreCase);
        private const int MaxCacheSize = 100;

        // Current song info
        private string _currentFilePath;
        private SongInfo _currentSongInfo;

        /// <summary>
        /// Event fired when song info changes (new song loaded).
        /// </summary>
        public event Action<SongInfo> OnSongInfoChanged;

        public SongMetadataService(IMusicPlaybackService playbackService, FileLogger fileLogger = null)
        {
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
            _fileLogger = fileLogger;

            // Subscribe to song changes
            _playbackService.OnSongChanged += OnSongChangedAsync;
            _playbackService.OnMusicStopped += _ => ClearCurrentSongInfo();
        }

        /// <summary>
        /// Resubscribe to a new playback service (e.g., after Live Effects toggle).
        /// </summary>
        public void ResubscribeToService(IMusicPlaybackService newPlaybackService)
        {
            if (newPlaybackService == null) return;

            // Update the service reference so IsPlayingDefaultMusic check works correctly
            _playbackService = newPlaybackService;

            newPlaybackService.OnSongChanged += OnSongChangedAsync;
            newPlaybackService.OnMusicStopped += _ => ClearCurrentSongInfo();

            // Update cached info if there's a current song
            var currentPath = newPlaybackService.CurrentSongPath;
            if (!string.IsNullOrEmpty(currentPath))
            {
                OnSongChangedAsync(currentPath);
            }
        }

        private void OnSongChangedAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                ClearCurrentSongInfo();
                return;
            }

            // Don't show song info for default/fallback music - keep ticker blank
            // Exception: bundled presets are real songs with proper metadata, so show them
            if (_playbackService.IsPlayingDefaultMusic && !_playbackService.IsPlayingBundledPreset)
            {
                ClearCurrentSongInfo();
                return;
            }

            // Skip if same file (already current)
            if (string.Equals(_currentFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentFilePath = filePath;

            // Check cache first (fast path - no blocking)
            if (_metadataCache.TryGetValue(filePath, out var cachedInfo))
            {
                _currentSongInfo = cachedInfo;
                OnSongInfoChanged?.Invoke(cachedInfo);
                return;
            }

            // Show filename immediately while loading full metadata
            SongTitleCleaner.ParseFilename(filePath, out string quickTitle, out string quickArtist);
            var quickInfo = new SongInfo(filePath, quickTitle, quickArtist, TimeSpan.Zero);
            _currentSongInfo = quickInfo;
            OnSongInfoChanged?.Invoke(quickInfo);

            // Read full metadata in background (non-blocking)
            Task.Run(() =>
            {
                try
                {
                    var fullInfo = ReadMetadataInternal(filePath);

                    // Only update if this is still the current song
                    if (string.Equals(_currentFilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentSongInfo = fullInfo;

                        // Cache the result
                        AddToCache(filePath, fullInfo);

                        // Update UI on dispatcher thread
                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(
                            new Action(() => OnSongInfoChanged?.Invoke(fullInfo)));
                    }
                }
                catch (Exception ex)
                {
                    _fileLogger?.Warn($"[SongMetadata] Background read failed: {ex.Message}");
                }
            });
        }

        private void AddToCache(string filePath, SongInfo info)
        {
            // Simple cache eviction if too large
            if (_metadataCache.Count >= MaxCacheSize)
            {
                // Remove a random entry (simple eviction)
                foreach (var key in _metadataCache.Keys)
                {
                    _metadataCache.TryRemove(key, out _);
                    break;
                }
            }
            _metadataCache[filePath] = info;
        }

        private void ClearCurrentSongInfo()
        {
            _currentFilePath = null;
            _currentSongInfo = SongInfo.Empty;
            OnSongInfoChanged?.Invoke(SongInfo.Empty);
        }

        /// <summary>
        /// Gets the current song info (cached).
        /// </summary>
        public SongInfo CurrentSongInfo => _currentSongInfo ?? SongInfo.Empty;

        /// <summary>
        /// Reads metadata from an audio file synchronously.
        /// Used for initial load and background reading.
        /// Falls back to cleaned filename if no embedded metadata.
        /// </summary>
        public SongInfo ReadMetadata(string filePath)
        {
            // Check cache first
            if (_metadataCache.TryGetValue(filePath, out var cached))
            {
                return cached;
            }

            var info = ReadMetadataInternal(filePath);
            AddToCache(filePath, info);
            return info;
        }

        /// <summary>
        /// Internal method to read metadata from file (does actual I/O).
        /// </summary>
        private SongInfo ReadMetadataInternal(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                return SongInfo.Empty;
            }

            try
            {
                using (var file = TagLib.File.Create(filePath))
                {
                    // Get title from metadata
                    string title = file.Tag?.Title;
                    string artist = null;

                    // Get artist from metadata (first performer)
                    if (file.Tag?.Performers != null && file.Tag.Performers.Length > 0)
                    {
                        artist = file.Tag.Performers[0];
                    }

                    // If no embedded title, parse from filename
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        SongTitleCleaner.ParseFilename(filePath, out title, out string filenameArtist);

                        // Use filename-extracted artist if no embedded artist
                        if (string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(filenameArtist))
                        {
                            artist = filenameArtist;
                        }
                    }

                    // Get duration
                    TimeSpan duration = file.Properties?.Duration ?? TimeSpan.Zero;

                    return new SongInfo(filePath, title, artist, duration);
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"[SongMetadata] Failed to read metadata from {Path.GetFileName(filePath)}: {ex.Message}");

                // Fall back to parsed filename with no duration
                SongTitleCleaner.ParseFilename(filePath, out string fallbackTitle, out string fallbackArtist);
                return new SongInfo(
                    filePath,
                    fallbackTitle,
                    fallbackArtist,
                    TimeSpan.Zero
                );
            }
        }
    }
}
