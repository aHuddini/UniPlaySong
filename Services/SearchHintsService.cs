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
    /// Service for loading and providing search hints for problematic game names.
    /// Loads from both bundled hints (ships with plugin) and user hints (editable).
    /// User hints take priority over bundled hints.
    /// </summary>
    public class SearchHintsService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly string _bundledHintsPath;
        private readonly string _userHintsPath;
        private Dictionary<string, SearchHint> _hints;
        private DateTime _lastLoadTime;
        private const int ReloadIntervalMinutes = 5;

        public SearchHintsService(string pluginInstallPath, string extensionDataPath)
        {
            _bundledHintsPath = Path.Combine(pluginInstallPath, "search_hints.json");
            _userHintsPath = Path.Combine(extensionDataPath, "search_hints_user.json");
            _hints = new Dictionary<string, SearchHint>(StringComparer.OrdinalIgnoreCase);
            LoadHints();
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

            // Load bundled hints first (read-only, ships with plugin)
            LoadHintsFromFile(_bundledHintsPath, "bundled");

            // Load user hints second (overwrites bundled for same keys)
            if (File.Exists(_userHintsPath))
            {
                LoadHintsFromFile(_userHintsPath, "user");
            }

            _lastLoadTime = DateTime.Now;
            Logger.Info($"[SearchHints] Loaded {_hints.Count} total hints: [{string.Join(", ", _hints.Keys)}]");
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
    }
}
