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
        /// Notes about why this hint was added (for documentation)
        /// </summary>
        [JsonProperty("notes")]
        public string Notes { get; set; }
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
                    Logger.Info($"[SearchHints] Created AutoSearchDatabase folder: {_autoSearchDatabasePath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[SearchHints] Failed to create AutoSearchDatabase folder: {_autoSearchDatabasePath}");
            }
        }

        /// <summary>
        /// Gets search hint for a game if one exists
        /// </summary>
        public SearchHint GetHint(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return null;

            // Reload if file might have changed
            ReloadIfNeeded();

            // Try exact match first
            if (_hints.TryGetValue(gameName, out var hint))
            {
                Logger.Info($"[SearchHints] Exact match for '{gameName}'");
                return hint;
            }

            // Try base name (stripped of edition suffixes)
            var baseName = StringHelper.ExtractBaseGameName(gameName);
            Logger.Info($"[SearchHints] Trying base name: '{gameName}' → '{baseName}'");
            if (!string.IsNullOrWhiteSpace(baseName) && _hints.TryGetValue(baseName, out hint))
            {
                Logger.Info($"[SearchHints] Base name match for '{baseName}'");
                return hint;
            }

            // Try normalized exact match
            var normalized = StringHelper.NormalizeForComparison(gameName);
            foreach (var kvp in _hints)
            {
                if (StringHelper.NormalizeForComparison(kvp.Key) == normalized)
                {
                    Logger.Info($"[SearchHints] Normalized match: '{gameName}' → '{kvp.Key}'");
                    return kvp.Value;
                }
            }

            // Try prefix matching - hint key should be prefix of game name
            // e.g., "The Coma 2" matches "The Coma 2: Vicious Sisters"
            // Also handles cases like "Deus Ex 2" matching "Deus Ex 2: Invisible War"
            foreach (var kvp in _hints)
            {
                var hintKeyNorm = StringHelper.NormalizeForComparison(kvp.Key);
                // Check if game name starts with hint key (with word boundary)
                if (normalized.StartsWith(hintKeyNorm) &&
                    (normalized.Length == hintKeyNorm.Length ||
                     normalized[hintKeyNorm.Length] == ' '))
                {
                    Logger.Info($"[SearchHints] Prefix match: '{gameName}' starts with '{kvp.Key}'");
                    return kvp.Value;
                }
            }

            // Try prefix matching on base name as well
            var baseNormalized = StringHelper.NormalizeForComparison(baseName);
            if (baseNormalized != normalized)
            {
                foreach (var kvp in _hints)
                {
                    var hintKeyNorm = StringHelper.NormalizeForComparison(kvp.Key);
                    if (baseNormalized.StartsWith(hintKeyNorm) &&
                        (baseNormalized.Length == hintKeyNorm.Length ||
                         baseNormalized[hintKeyNorm.Length] == ' '))
                    {
                        Logger.Info($"[SearchHints] Base prefix match: '{baseName}' starts with '{kvp.Key}'");
                        return kvp.Value;
                    }
                }
            }

            Logger.Info($"[SearchHints] No hint found for '{gameName}'");
            return null;
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

            Logger.Info($"[SearchHints] Added YouTube playlist hint for '{key}': {playlistId}");
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

            Logger.Info($"[SearchHints] Added search terms hint for '{key}': [{string.Join(", ", searchTerms)}]");
        }

        private void LoadHints()
        {
            _hints = new Dictionary<string, SearchHint>(StringComparer.OrdinalIgnoreCase);

            // Priority: Downloaded hints (from GitHub) > Bundled hints
            // If downloaded hints exist, use them; otherwise fall back to bundled
            if (File.Exists(_downloadedHintsPath))
            {
                Logger.Info($"[SearchHints] Using downloaded hints from AutoSearchDatabase");
                LoadHintsFromFile(_downloadedHintsPath, "downloaded");
            }
            else
            {
                // Load bundled hints (read-only, ships with plugin)
                LoadHintsFromFile(_bundledHintsPath, "bundled");
            }

            // Load user hints on top (overwrites for same keys)
            if (File.Exists(_userHintsPath))
            {
                LoadHintsFromFile(_userHintsPath, "user");
            }

            _lastLoadTime = DateTime.Now;
            Logger.Info($"[SearchHints] Loaded {_hints.Count} total hints");
        }

        private void LoadHintsFromFile(string path, string source)
        {
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
                            _hints[prop.Name] = hint;
                            count++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[SearchHints] Failed to parse hint '{prop.Name}': {ex.Message}");
                    }
                }
                Logger.Info($"[SearchHints] Loaded {count} {source} hints from {path}");
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
                Logger.Info($"[SearchHints] Downloading hints from GitHub: {GitHubHintsUrl}");

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

                    Logger.Info($"[SearchHints] Downloaded {entryCount} hints from GitHub");

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
                    Logger.Info("[SearchHints] Deleted downloaded hints file");
                }

                if (File.Exists(_metadataPath))
                {
                    File.Delete(_metadataPath);
                    Logger.Info("[SearchHints] Deleted hints metadata file");
                }

                // Reload hints (will fall back to bundled)
                LoadHints();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[SearchHints] Error deleting downloaded hints");
            }
        }
    }
}
