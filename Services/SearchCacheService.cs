using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Playnite.SDK;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Cache entry for search results
    /// </summary>
    public class SearchCacheEntry
    {
        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }
        
        [JsonProperty("expires")]
        public DateTime Expires { get; set; }
        
        [JsonProperty("album_count")]
        public int AlbumCount { get; set; }
        
        [JsonProperty("albums")]
        public List<CachedAlbum> Albums { get; set; }
        
        public SearchCacheEntry()
        {
            Albums = new List<CachedAlbum>();
        }
    }
    
    /// <summary>
    /// Minimal album data for caching - only essential fields needed for auto-download.
    /// Reduces cache file size significantly by excluding display-only metadata.
    /// </summary>
    public class CachedAlbum
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("year")]
        public string Year { get; set; }

        /// <summary>
        /// Convert cached album back to full Album object
        /// </summary>
        public Album ToAlbum()
        {
            Source sourceEnum;
            Enum.TryParse(Source, out sourceEnum);

            return new Album
            {
                Id = Id,
                Name = Name,
                Source = sourceEnum,
                Year = Year
            };
        }

        /// <summary>
        /// Create cached album from full Album object (only stores essential fields)
        /// </summary>
        public static CachedAlbum FromAlbum(Album album)
        {
            return new CachedAlbum
            {
                Id = album.Id,
                Name = album.Name,
                Source = album.Source.ToString(),
                Year = album.Year
            };
        }
    }
    
    /// <summary>
    /// Game search cache data structure
    /// </summary>
    public class GameSearchCache
    {
        [JsonProperty("khinsider")]
        public SearchCacheEntry KHInsider { get; set; }
        
        [JsonProperty("youtube")]
        public SearchCacheEntry YouTube { get; set; }
    }
    
    /// <summary>
    /// Root cache structure
    /// </summary>
    public class SearchCacheData
    {
        [JsonProperty("version")]
        public string Version { get; set; }
        
        [JsonProperty("last_cleanup")]
        public DateTime LastCleanup { get; set; }
        
        [JsonProperty("entries")]
        public Dictionary<string, GameSearchCache> Entries { get; set; }
        
        public SearchCacheData()
        {
            Version = "2.0"; // v2.0: Minimal cache format (id, name, source only)
            LastCleanup = DateTime.UtcNow;
            Entries = new Dictionary<string, GameSearchCache>(StringComparer.OrdinalIgnoreCase);
        }
    }
    
    /// <summary>
    /// Service for caching search results to optimize KHInsider → YouTube fallback
    /// </summary>
    public class SearchCacheService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly string _cacheFilePath;
        private readonly object _cacheLock = new object();
        private SearchCacheData _cache;
        private bool _enabled;
        private int _cacheDurationDays;

        // Limit cached albums per source to keep cache file small
        private const int MaxCachedAlbumsPerSource = 10;
        
        public SearchCacheService(string extensionDataPath, bool enabled = true, int cacheDurationDays = 7)
        {
            _cacheFilePath = Path.Combine(extensionDataPath, "search_cache.json");
            _enabled = enabled;
            _cacheDurationDays = cacheDurationDays;
            
            LoadCache();
            CleanupExpiredEntries();
        }
        
        /// <summary>
        /// Enable or disable caching
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }
        
        /// <summary>
        /// Cache duration in days
        /// </summary>
        public int CacheDurationDays
        {
            get => _cacheDurationDays;
            set
            {
                if (value > 0)
                {
                    _cacheDurationDays = value;
                }
            }
        }
        
        /// <summary>
        /// Try to get cached albums for a game and source
        /// </summary>
        public bool TryGetCachedAlbums(string gameName, Source source, out List<Album> albums)
        {
            albums = null;
            
            if (!_enabled)
            {
                return false;
            }
            
            lock (_cacheLock)
            {
                var normalizedName = NormalizeGameName(gameName);
                
                if (!_cache.Entries.TryGetValue(normalizedName, out var gameCache))
                {
                    return false;
                }
                
                SearchCacheEntry entry = null;
                if (source == Source.KHInsider)
                {
                    entry = gameCache.KHInsider;
                }
                else if (source == Source.YouTube)
                {
                    entry = gameCache.YouTube;
                }
                
                if (entry == null)
                {
                    return false;
                }
                
                // Check if expired
                if (DateTime.UtcNow > entry.Expires)
                {
                    return false;
                }
                
                // Cache hit
                albums = entry.Albums.Select(ca => ca.ToAlbum()).ToList();
                return true;
            }
        }
        
        /// <summary>
        /// Cache search results for a game and source.
        /// IMPORTANT: Empty results are NOT cached to avoid persisting temporary failures.
        /// </summary>
        public void CacheSearchResult(string gameName, Source source, List<Album> albums)
        {
            if (!_enabled)
            {
                return;
            }

            // Don't cache empty results - they might be due to temporary network issues,
            // rate limiting, or other transient failures. We only want to cache positive results.
            if (albums == null || albums.Count == 0)
            {
                return;
            }

            lock (_cacheLock)
            {
                var normalizedName = NormalizeGameName(gameName);

                if (!_cache.Entries.ContainsKey(normalizedName))
                {
                    _cache.Entries[normalizedName] = new GameSearchCache();
                }

                var gameCache = _cache.Entries[normalizedName];
                var now = DateTime.UtcNow;
                var expires = now.AddDays(_cacheDurationDays);

                // Only cache top N albums to keep cache file small
                var albumsToCache = albums.Take(MaxCachedAlbumsPerSource).ToList();

                var entry = new SearchCacheEntry
                {
                    Timestamp = now,
                    Expires = expires,
                    AlbumCount = albumsToCache.Count,
                    Albums = albumsToCache.Select(a => CachedAlbum.FromAlbum(a)).ToList()
                };

                if (source == Source.KHInsider)
                {
                    gameCache.KHInsider = entry;
                }
                else if (source == Source.YouTube)
                {
                    gameCache.YouTube = entry;
                }

                SaveCache();
            }
        }
        
        /// <summary>
        /// Clear all cached entries
        /// </summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cache = new SearchCacheData();
                SaveCache();
            }
        }
        
        /// <summary>
        /// Remove expired entries and empty entries from cache.
        /// Empty entries should no longer be created, but this cleans up any existing ones.
        /// </summary>
        public int CleanupExpiredEntries()
        {
            lock (_cacheLock)
            {
                var now = DateTime.UtcNow;
                var removedCount = 0;
                var gamesToRemove = new List<string>();

                foreach (var kvp in _cache.Entries)
                {
                    var gameCache = kvp.Value;
                    bool hasValidEntry = false;

                    // Check KHInsider entry - remove if expired OR empty
                    if (gameCache.KHInsider != null)
                    {
                        bool isExpired = gameCache.KHInsider.Expires <= now;
                        bool isEmpty = gameCache.KHInsider.AlbumCount == 0 ||
                                       gameCache.KHInsider.Albums == null ||
                                       gameCache.KHInsider.Albums.Count == 0;

                        if (isExpired || isEmpty)
                        {
                            gameCache.KHInsider = null;
                            removedCount++;
                        }
                        else
                        {
                            hasValidEntry = true;
                        }
                    }

                    // Check YouTube entry - remove if expired OR empty
                    if (gameCache.YouTube != null)
                    {
                        bool isExpired = gameCache.YouTube.Expires <= now;
                        bool isEmpty = gameCache.YouTube.AlbumCount == 0 ||
                                       gameCache.YouTube.Albums == null ||
                                       gameCache.YouTube.Albums.Count == 0;

                        if (isExpired || isEmpty)
                        {
                            gameCache.YouTube = null;
                            removedCount++;
                        }
                        else
                        {
                            hasValidEntry = true;
                        }
                    }

                    // If no valid entries remain, mark game for removal
                    if (!hasValidEntry)
                    {
                        gamesToRemove.Add(kvp.Key);
                    }
                }

                // Remove games with no valid entries
                foreach (var gameName in gamesToRemove)
                {
                    _cache.Entries.Remove(gameName);
                }

                if (removedCount > 0 || gamesToRemove.Count > 0)
                {
                    _cache.LastCleanup = now;
                    SaveCache();
                }

                return removedCount;
            }
        }
        
        /// <summary>
        /// Get cache statistics
        /// </summary>
        public CacheStats GetCacheStats()
        {
            lock (_cacheLock)
            {
                var gameCount = _cache.Entries.Count;
                var entryCount = 0;
                
                foreach (var gameCache in _cache.Entries.Values)
                {
                    if (gameCache.KHInsider != null) entryCount++;
                    if (gameCache.YouTube != null) entryCount++;
                }
                
                long sizeBytes = 0;
                if (File.Exists(_cacheFilePath))
                {
                    sizeBytes = new FileInfo(_cacheFilePath).Length;
                }
                
                return new CacheStats
                {
                    GameCount = gameCount,
                    EntryCount = entryCount,
                    SizeBytes = sizeBytes
                };
            }
        }
        
        /// <summary>
        /// Normalize game name for cache key (lowercase, trim)
        /// </summary>
        private string NormalizeGameName(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return string.Empty;
            
            return gameName.Trim().ToLowerInvariant();
        }
        
        /// <summary>
        /// Load cache from disk
        /// </summary>
        private void LoadCache()
        {
            const string CurrentVersion = "2.0";

            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var json = File.ReadAllText(_cacheFilePath);
                    _cache = JsonConvert.DeserializeObject<SearchCacheData>(json);

                    if (_cache == null || _cache.Entries == null)
                    {
                        Logger.Warn("[Cache] Invalid cache file, creating new cache");
                        _cache = new SearchCacheData();
                    }
                    else if (_cache.Version != CurrentVersion)
                    {
                        // Cache format changed - clear old cache to use new minimal format
                        _cache = new SearchCacheData();
                        SaveCache();
                    }
                }
                else
                {
                    _cache = new SearchCacheData();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[Cache] Error loading cache: {ex.Message}");
                _cache = new SearchCacheData();
            }
        }
        
        /// <summary>
        /// Save cache to disk (atomic write)
        /// </summary>
        private void SaveCache()
        {
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(_cacheFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Write to temp file first (atomic write)
                var tempPath = _cacheFilePath + ".tmp";
                var json = JsonConvert.SerializeObject(_cache, Formatting.Indented);
                File.WriteAllText(tempPath, json);
                
                // Move temp file to actual cache file
                if (File.Exists(_cacheFilePath))
                {
                    File.Delete(_cacheFilePath);
                }
                File.Move(tempPath, _cacheFilePath);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[Cache] Error saving cache: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Cache statistics
    /// </summary>
    public class CacheStats
    {
        public int GameCount { get; set; }
        public int EntryCount { get; set; }
        public long SizeBytes { get; set; }
    }
}
