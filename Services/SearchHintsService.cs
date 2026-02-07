using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Search hint for a specific game - provides alternative search terms or direct playlist URLs
    /// </summary>
    public class SearchHint
    {
        /// <summary>
        /// Alternative search terms to use instead of the game name
        /// </summary>
        [JsonProperty("searchTerms")]
        public List<string> SearchTerms { get; set; }

        /// <summary>
        /// Direct YouTube playlist ID to use (bypasses search entirely)
        /// </summary>
        [JsonProperty("youtubePlaylistId")]
        public string YouTubePlaylistId { get; set; }

        /// <summary>
        /// KHInsider album ID/URL to use directly
        /// </summary>
        [JsonProperty("khinsiderAlbum")]
        public string KHInsiderAlbum { get; set; }

        /// <summary>
        /// SoundCloud track or playlist URL path (e.g., "artist/track-name" or "artist/sets/playlist-name")
        /// </summary>
        [JsonProperty("soundcloudUrl")]
        public string SoundCloudUrl { get; set; }

        /// <summary>
        /// Notes about why this hint was added (for documentation)
        /// </summary>
        [JsonProperty("notes")]
        public string Notes { get; set; }

        /// <summary>
        /// Checks if this hint has any direct links (YouTubePlaylistId, KHInsiderAlbum, or SoundCloudUrl)
        /// </summary>
        public bool HasDirectLinks()
        {
            return !string.IsNullOrWhiteSpace(YouTubePlaylistId) ||
                   !string.IsNullOrWhiteSpace(KHInsiderAlbum) ||
                   !string.IsNullOrWhiteSpace(SoundCloudUrl);
        }
    }

    /// <summary>
    /// Metadata about the downloaded hints file
    /// </summary>
    public class HintsMetadata
    {
        [JsonProperty("lastDownloadDate")]
        public DateTime LastDownloadDate { get; set; }

        [JsonProperty("entryCount")]
        public int EntryCount { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }
    }

    /// <summary>
    /// Service for loading and providing search hints for problematic game names.
    /// Priority order:
    /// 1. Downloaded hints from GitHub (AutoSearchDatabase folder) - if exists
    /// 2. Bundled hints (ships with plugin) - fallback
    /// User hints are saved separately and merged on top.
    /// </summary>
    public class SearchHintsService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly string _bundledHintsPath;
        private readonly string _userHintsPath;
        private readonly string _autoSearchDatabasePath;
        private readonly string _downloadedHintsPath;
        private readonly string _metadataPath;
        private Dictionary<string, SearchHint> _hints;
        private Dictionary<string, SearchHint> _bundledHints;  // Keep bundled hints for fallback
        private DateTime _lastLoadTime;
        private const int ReloadIntervalMinutes = 5;

        // GitHub raw URL for the search_hints.json file
        private const string GitHubHintsUrl = "https://raw.githubusercontent.com/aHuddini/UniPlaySong/main/AutoSearchDatabase/search_hints.json";

        public SearchHintsService(string pluginInstallPath, string extensionDataPath)
        {
            _bundledHintsPath = Path.Combine(pluginInstallPath, "AutoSearchDatabase", "search_hints.json");
            _userHintsPath = Path.Combine(extensionDataPath, "search_hints_user.json");

            // AutoSearchDatabase folder for downloaded hints
            _autoSearchDatabasePath = Path.Combine(extensionDataPath, "AutoSearchDatabase");
            _downloadedHintsPath = Path.Combine(_autoSearchDatabasePath, "search_hints.json");
            _metadataPath = Path.Combine(_autoSearchDatabasePath, "metadata.json");

            // Ensure AutoSearchDatabase folder exists
            EnsureAutoSearchDatabaseFolder();

            _hints = new Dictionary<string, SearchHint>(StringComparer.OrdinalIgnoreCase);
            LoadHints();
        }

        /// <summary>
        /// Ensures the AutoSearchDatabase folder exists
        /// </summary>
        private void EnsureAutoSearchDatabaseFolder()
        {
            try
            {
                if (!Directory.Exists(_autoSearchDatabasePath))
                {
                    Directory.CreateDirectory(_autoSearchDatabasePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[SearchHints] Failed to create AutoSearchDatabase folder: {_autoSearchDatabasePath}");
            }
        }

        /// <summary>
        /// Gets search hint for a game if one exists.
        /// If the hint from downloaded/primary source has no direct links, falls back to bundled hints.
        /// </summary>
        public SearchHint GetHint(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return null;

            // Reload if file might have changed
            ReloadIfNeeded();

            // Find hint using matching logic, then check for fallback
            var (hint, matchedKey) = FindHintWithKey(gameName, _hints);

            if (hint != null)
            {
                // Check if hint has direct links - if not, try bundled fallback
                if (!hint.HasDirectLinks() && _bundledHints != null && _bundledHints.Count > 0)
                {
                    var (bundledHint, _) = FindHintWithKey(gameName, _bundledHints);

                    if (bundledHint != null && bundledHint.HasDirectLinks())
                    {
                        return bundledHint;
                    }
                }

                return hint;
            }

            return null;
        }

        /// <summary>
        /// Finds a hint in the specified dictionary using various matching strategies
        /// </summary>
        private (SearchHint hint, string matchedKey) FindHintWithKey(string gameName, Dictionary<string, SearchHint> hints)
        {
            if (hints == null || hints.Count == 0)
                return (null, null);

            // Try exact match first
            if (hints.TryGetValue(gameName, out var hint))
            {
                return (hint, gameName);
            }

            // Try base name (stripped of edition suffixes)
            var baseName = StringHelper.ExtractBaseGameName(gameName);
            if (!string.IsNullOrWhiteSpace(baseName) && hints.TryGetValue(baseName, out hint))
            {
                return (hint, baseName);
            }

            // Try normalized exact match
            var normalized = StringHelper.NormalizeForComparison(gameName);
            foreach (var kvp in hints)
            {
                if (StringHelper.NormalizeForComparison(kvp.Key) == normalized)
                {
                    return (kvp.Value, kvp.Key);
                }
            }

            // Try prefix matching - hint key should be prefix of game name
            // e.g., "The Coma 2" matches "The Coma 2: Vicious Sisters"
            foreach (var kvp in hints)
            {
                var hintKeyNorm = StringHelper.NormalizeForComparison(kvp.Key);
                // Check if game name starts with hint key (with word boundary)
                if (normalized.StartsWith(hintKeyNorm) &&
                    (normalized.Length == hintKeyNorm.Length ||
                     normalized[hintKeyNorm.Length] == ' '))
                {
                    return (kvp.Value, kvp.Key);
                }
            }

            // Try prefix matching on base name as well
            var baseNormalized = StringHelper.NormalizeForComparison(baseName);
            if (baseNormalized != normalized)
            {
                foreach (var kvp in hints)
                {
                    var hintKeyNorm = StringHelper.NormalizeForComparison(kvp.Key);
                    if (baseNormalized.StartsWith(hintKeyNorm) &&
                        (baseNormalized.Length == hintKeyNorm.Length ||
                         baseNormalized[hintKeyNorm.Length] == ' '))
                    {
                        return (kvp.Value, kvp.Key);
                    }
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Adds or updates a search hint for a game
        /// </summary>
        public void SetHint(string gameName, SearchHint hint)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return;

            var baseName = StringHelper.ExtractBaseGameName(gameName);
            var key = string.IsNullOrWhiteSpace(baseName) ? gameName : baseName;

            _hints[key] = hint;
            SaveHints();
        }

        /// <summary>
        /// Adds a YouTube playlist hint for a game (convenience method)
        /// </summary>
        public void AddYouTubePlaylistHint(string gameName, string playlistId, string notes = null)
        {
            var baseName = StringHelper.ExtractBaseGameName(gameName);
            var key = string.IsNullOrWhiteSpace(baseName) ? gameName : baseName;

            if (!_hints.TryGetValue(key, out var hint))
            {
                hint = new SearchHint();
            }

            hint.YouTubePlaylistId = playlistId;
            if (!string.IsNullOrWhiteSpace(notes))
                hint.Notes = notes;

            _hints[key] = hint;
            SaveHints();
        }

        /// <summary>
        /// Adds search terms hint for a game (convenience method)
        /// </summary>
        public void AddSearchTermsHint(string gameName, List<string> searchTerms, string notes = null)
        {
            var baseName = StringHelper.ExtractBaseGameName(gameName);
            var key = string.IsNullOrWhiteSpace(baseName) ? gameName : baseName;

            if (!_hints.TryGetValue(key, out var hint))
            {
                hint = new SearchHint();
            }

            hint.SearchTerms = searchTerms;
            if (!string.IsNullOrWhiteSpace(notes))
                hint.Notes = notes;

            _hints[key] = hint;
            SaveHints();
        }

        private void LoadHints()
        {
            _hints = new Dictionary<string, SearchHint>(StringComparer.OrdinalIgnoreCase);
            _bundledHints = new Dictionary<string, SearchHint>(StringComparer.OrdinalIgnoreCase);

            // Always load bundled hints first (for fallback when downloaded hints lack direct links)
            LoadHintsFromFile(_bundledHintsPath, "bundled", _bundledHints);

            // Priority: Downloaded hints (from GitHub) > Bundled hints
            // If downloaded hints exist, use them as primary; otherwise use bundled
            if (File.Exists(_downloadedHintsPath))
            {
                LoadHintsFromFile(_downloadedHintsPath, "downloaded", _hints);
            }
            else
            {
                // No downloaded hints - copy bundled hints to main dictionary
                foreach (var kvp in _bundledHints)
                {
                    _hints[kvp.Key] = kvp.Value;
                }
            }

            // Load user hints on top (overwrites for same keys)
            if (File.Exists(_userHintsPath))
            {
                LoadHintsFromFile(_userHintsPath, "user", _hints);
            }

            _lastLoadTime = DateTime.Now;
        }

        private void LoadHintsFromFile(string path, string source, Dictionary<string, SearchHint> targetDictionary = null)
        {
            // Use provided dictionary or fall back to _hints
            var target = targetDictionary ?? _hints;

            try
            {
                if (!File.Exists(path))
                {
                    Logger.Warn($"[SearchHints] {source} hints file not found: {path}");
                    return;
                }

                var json = File.ReadAllText(path);

                // Parse as JObject first to handle mixed types (like _comment being a string)
                var jObject = Newtonsoft.Json.Linq.JObject.Parse(json);
                if (jObject == null) return;

                var count = 0;
                foreach (var prop in jObject.Properties())
                {
                    // Skip special keys like _comment
                    if (prop.Name.StartsWith("_")) continue;

                    // Only process objects, skip strings or other types
                    if (prop.Value.Type != Newtonsoft.Json.Linq.JTokenType.Object) continue;

                    try
                    {
                        var hint = prop.Value.ToObject<SearchHint>();
                        if (hint != null)
                        {
                            target[prop.Name] = hint;
                            count++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[SearchHints] Failed to parse hint '{prop.Name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[SearchHints] Error loading {source} hints from {path}");
            }
        }

        private void ReloadIfNeeded()
        {
            if ((DateTime.Now - _lastLoadTime).TotalMinutes >= ReloadIntervalMinutes)
            {
                LoadHints();
            }
        }

        private void SaveHints()
        {
            // Only save user-added hints (not bundled ones)
            // We need to filter to only include hints that differ from bundled
            try
            {
                var dir = Path.GetDirectoryName(_userHintsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Load bundled hints to compare
                var bundled = new Dictionary<string, SearchHint>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(_bundledHintsPath))
                {
                    var bundledJson = File.ReadAllText(_bundledHintsPath);
                    bundled = JsonConvert.DeserializeObject<Dictionary<string, SearchHint>>(bundledJson)
                            ?? new Dictionary<string, SearchHint>(StringComparer.OrdinalIgnoreCase);
                }

                // Save only hints that are new or different from bundled
                var userHints = new Dictionary<string, SearchHint>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in _hints)
                {
                    if (!bundled.ContainsKey(kvp.Key))
                    {
                        userHints[kvp.Key] = kvp.Value;
                    }
                }

                if (userHints.Count > 0)
                {
                    var json = JsonConvert.SerializeObject(userHints, Formatting.Indented);
                    File.WriteAllText(_userHintsPath, json);
                }
                _lastLoadTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[SearchHints] Error saving user hints file");
            }
        }

        /// <summary>
        /// Gets the path to the user hints file (for user reference)
        /// </summary>
        public string GetUserHintsFilePath() => _userHintsPath;

        /// <summary>
        /// Gets the path to the bundled hints file (for reference)
        /// </summary>
        public string GetBundledHintsFilePath() => _bundledHintsPath;

        /// <summary>
        /// Gets the path to the AutoSearchDatabase folder
        /// </summary>
        public string GetAutoSearchDatabasePath() => _autoSearchDatabasePath;

        /// <summary>
        /// Gets the path to the downloaded hints file
        /// </summary>
        public string GetDownloadedHintsFilePath() => _downloadedHintsPath;

        /// <summary>
        /// Downloads the latest search_hints.json from GitHub
        /// </summary>
        /// <returns>True if download was successful</returns>
        public bool DownloadHintsFromGitHub()
        {
            try
            {
                using (var client = new System.Net.WebClient())
                {
                    // Add user agent to avoid GitHub blocking the request
                    client.Headers.Add("User-Agent", "UniPlaySong/1.0");

                    var json = client.DownloadString(GitHubHintsUrl);

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        Logger.Error("[SearchHints] Downloaded empty content from GitHub");
                        return false;
                    }

                    // Validate JSON by parsing it
                    var jObject = Newtonsoft.Json.Linq.JObject.Parse(json);
                    if (jObject == null)
                    {
                        Logger.Error("[SearchHints] Failed to parse downloaded JSON");
                        return false;
                    }

                    // Count entries (excluding _comment and other special keys)
                    int entryCount = 0;
                    foreach (var prop in jObject.Properties())
                    {
                        if (!prop.Name.StartsWith("_") && prop.Value.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                        {
                            entryCount++;
                        }
                    }

                    // Ensure directory exists
                    EnsureAutoSearchDatabaseFolder();

                    // Save the downloaded file
                    File.WriteAllText(_downloadedHintsPath, json);

                    // Save metadata
                    var metadata = new HintsMetadata
                    {
                        LastDownloadDate = DateTime.Now,
                        EntryCount = entryCount,
                        Source = GitHubHintsUrl
                    };
                    var metadataJson = JsonConvert.SerializeObject(metadata, Formatting.Indented);
                    File.WriteAllText(_metadataPath, metadataJson);

                    // Reload hints to pick up new file
                    LoadHints();

                    return true;
                }
            }
            catch (System.Net.WebException ex)
            {
                Logger.Error(ex, $"[SearchHints] Network error downloading hints from GitHub: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[SearchHints] Error downloading hints from GitHub: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets metadata about the downloaded hints file
        /// </summary>
        /// <returns>Metadata if available, null otherwise</returns>
        public HintsMetadata GetDownloadedHintsMetadata()
        {
            try
            {
                if (!File.Exists(_metadataPath))
                {
                    return null;
                }

                var json = File.ReadAllText(_metadataPath);
                return JsonConvert.DeserializeObject<HintsMetadata>(json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[SearchHints] Error reading hints metadata: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Checks if downloaded hints exist
        /// </summary>
        public bool HasDownloadedHints()
        {
            return File.Exists(_downloadedHintsPath);
        }

        /// <summary>
        /// Gets a status string for the downloaded hints
        /// </summary>
        public string GetDownloadedHintsStatus()
        {
            var metadata = GetDownloadedHintsMetadata();
            if (metadata == null)
            {
                return "Not downloaded";
            }

            var age = DateTime.Now - metadata.LastDownloadDate;
            string ageText;
            if (age.TotalDays >= 1)
            {
                ageText = $"{(int)age.TotalDays} day(s) ago";
            }
            else if (age.TotalHours >= 1)
            {
                ageText = $"{(int)age.TotalHours} hour(s) ago";
            }
            else
            {
                ageText = "Just now";
            }

            return $"{metadata.EntryCount} entries, last updated {ageText}";
        }

        /// <summary>
        /// Deletes downloaded hints, reverting to bundled hints
        /// </summary>
        public void DeleteDownloadedHints()
        {
            try
            {
                if (File.Exists(_downloadedHintsPath))
                {
                    File.Delete(_downloadedHintsPath);
                }

                if (File.Exists(_metadataPath))
                {
                    File.Delete(_metadataPath);
                }

                // Reload hints (will fall back to bundled)
                LoadHints();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[SearchHints] Error deleting downloaded hints");
            }
        }

        /// <summary>
        /// Gets the entry count from the bundled hints file
        /// </summary>
        public int GetBundledHintsEntryCount()
        {
            return _bundledHints?.Count ?? 0;
        }

        /// <summary>
        /// Checks if the GitHub hints database has more entries than the currently loaded hints.
        /// This performs a lightweight check by fetching the GitHub file and comparing entry counts.
        /// </summary>
        /// <returns>
        /// Tuple of (hasUpdate, gitHubEntryCount, currentEntryCount) where hasUpdate is true if GitHub has more entries.
        /// Returns (false, 0, currentCount) if the check fails.
        /// </returns>
        public (bool hasUpdate, int gitHubEntryCount, int currentEntryCount) CheckForHintsUpdates()
        {
            var currentCount = _hints?.Count ?? 0;

            try
            {
                using (var client = new System.Net.WebClient())
                {
                    // Add user agent to avoid GitHub blocking the request
                    client.Headers.Add("User-Agent", "UniPlaySong/1.0");

                    var json = client.DownloadString(GitHubHintsUrl);

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        Logger.Warn("[SearchHints] GitHub returned empty content during update check");
                        return (false, 0, currentCount);
                    }

                    // Parse to count entries
                    var jObject = Newtonsoft.Json.Linq.JObject.Parse(json);
                    if (jObject == null)
                    {
                        Logger.Warn("[SearchHints] Failed to parse GitHub JSON during update check");
                        return (false, 0, currentCount);
                    }

                    // Count entries (excluding _comment and other special keys)
                    int gitHubEntryCount = 0;
                    foreach (var prop in jObject.Properties())
                    {
                        if (!prop.Name.StartsWith("_") && prop.Value.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                        {
                            gitHubEntryCount++;
                        }
                    }

                    // Consider it an update if GitHub has more entries
                    bool hasUpdate = gitHubEntryCount > currentCount;

                    return (hasUpdate, gitHubEntryCount, currentCount);
                }
            }
            catch (System.Net.WebException ex)
            {
                Logger.Warn($"[SearchHints] Network error checking for hints updates: {ex.Message}");
                return (false, 0, currentCount);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[SearchHints] Error checking for hints updates: {ex.Message}");
                return (false, 0, currentCount);
            }
        }
    }
}
