using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using FuzzySharp;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Downloaders
{
    /// <summary>
    /// Manages music downloads from various sources
    /// </summary>
    public class DownloadManager : IDownloadManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private static DownloaderLogger DLog => DownloaderLogger.Instance;
        private const string LogPrefix = "DownloadManager";
        private static readonly TimeSpan MaxSongLength = new TimeSpan(0, Constants.MaxPreviewSongLengthMinutes, 0);
        private static readonly TimeSpan MaxAllowedDuration = new TimeSpan(0, Constants.MaxAllowedSongDurationMinutes, 0);
        private static readonly List<string> PreferredSongEndings = Constants.PreferredSongEndings;

        private readonly IDownloader _khDownloader;
        private readonly IDownloader _zopharDownloader;
        private readonly IDownloader _ytDownloader;
        private readonly IDownloader _soundCloudDownloader;
        private readonly string _tempPath;
        private readonly ErrorHandlerService _errorHandler;
        private readonly SearchCacheService _cacheService;
        private readonly SearchHintsService _hintsService;
        private readonly UniPlaySongSettings _settings;

        public DownloadManager(HttpClient httpClient, HtmlAgilityPack.HtmlWeb htmlWeb, string tempPath,
            string ytDlpPath = null, string ffmpegPath = null, ErrorHandlerService errorHandler = null,
            SearchCacheService cacheService = null, SearchHintsService hintsService = null, UniPlaySongSettings settings = null)
        {
            _tempPath = tempPath ?? throw new ArgumentNullException(nameof(tempPath));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _cacheService = cacheService;
            _hintsService = hintsService;
            _settings = settings;

            _khDownloader = new KHInsiderDownloader(httpClient, htmlWeb, errorHandler);
            _zopharDownloader = new ZopharDownloader(httpClient, htmlWeb, errorHandler);
            var useFirefoxCookies = settings?.UseFirefoxCookies ?? false;
            _ytDownloader = new YouTubeDownloader(httpClient, ytDlpPath, ffmpegPath, useFirefoxCookies, errorHandler);
            _soundCloudDownloader = new SoundCloudDownloader(ytDlpPath, ffmpegPath, errorHandler);

            Cleanup();
        }

        public IEnumerable<Album> GetAlbumsForGame(string gameName, Source source, CancellationToken cancellationToken, bool auto = false, bool skipCache = false)
        {
            if (_errorHandler != null)
            {
                return _errorHandler.Try(
                    () => GetAlbumsForGameInternal(gameName, source, cancellationToken, auto, skipCache),
                    defaultValue: Enumerable.Empty<Album>(),
                    context: $"getting albums for '{gameName}' from {source}"
                );
            }
            else
            {
                try
                {
                    return GetAlbumsForGameInternal(gameName, source, cancellationToken, auto, skipCache);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error getting albums for '{gameName}' from {source}: {ex.Message}");
                    return Enumerable.Empty<Album>();
                }
            }
        }

        private IEnumerable<Album> GetAlbumsForGameInternal(string gameName, Source source, CancellationToken cancellationToken, bool auto, bool skipCache = false)
        {
            // For Source.All: Fetch from all sources (KHInsider → Zophar → YouTube), let BestAlbumPick decide
            if (source == Source.All)
            {
                // Detailed logging goes to downloader.log
                DLog.Debug($"Search '{gameName}' (auto={auto})");

                var allAlbums = new List<Album>();

                // === PRIORITY 0: Hint albums (SoundCloud, YouTube playlist, KHInsider from search_hints.json) ===
                // These are added first with Type="Hint" so BestAlbumPick selects them as highest priority
                var hintAlbums = GetHintAlbums(gameName);
                if (hintAlbums != null && hintAlbums.Count > 0)
                {
                    DLog.Debug($"  Hints: {hintAlbums.Count} (from search_hints.json)");
                    allAlbums.AddRange(hintAlbums);
                }

                // === PRIORITY 1: KHInsider (best quality, curated VGM archive) ===
                List<Album> khAlbums = null;

                // Check cache only if not skipping
                if (!skipCache && _cacheService != null && _cacheService.TryGetCachedAlbums(gameName, Source.KHInsider, out khAlbums))
                {
                    DLog.Debug($"  KHInsider: {khAlbums.Count} (cached)");
                }
                else
                {
                    khAlbums = GetKHInsiderAlbumsWithStrategies(gameName, cancellationToken, auto);

                    // Only cache if not skipping cache (empty results will be skipped by cache service)
                    if (!skipCache && _cacheService != null)
                    {
                        _cacheService.CacheSearchResult(gameName, Source.KHInsider, khAlbums);
                    }
                    DLog.Debug($"  KHInsider: {khAlbums?.Count ?? 0}");
                }

                if (khAlbums != null && khAlbums.Count > 0)
                {
                    allAlbums.AddRange(khAlbums);
                }

                // === PRIORITY 2: Zophar (good VGM archive with emulated formats) ===
                List<Album> zopharAlbums = null;

                if (!skipCache && _cacheService != null && _cacheService.TryGetCachedAlbums(gameName, Source.Zophar, out zopharAlbums))
                {
                    DLog.Debug($"  Zophar: {zopharAlbums.Count} (cached)");
                }
                else
                {
                    zopharAlbums = _zopharDownloader?.GetAlbumsForGame(gameName, cancellationToken, auto)?.ToList()
                        ?? new List<Album>();

                    if (!skipCache && _cacheService != null)
                    {
                        _cacheService.CacheSearchResult(gameName, Source.Zophar, zopharAlbums);
                    }
                    DLog.Debug($"  Zophar: {zopharAlbums.Count}");
                }

                if (zopharAlbums != null && zopharAlbums.Count > 0)
                {
                    allAlbums.AddRange(zopharAlbums);
                }

                // === PRIORITY 3: YouTube (last resort, requires yt-dlp) ===
                var ytAlbums = GetYouTubeAlbumsWithCache(gameName, cancellationToken, auto, skipCache);

                if (ytAlbums != null && ytAlbums.Count > 0)
                {
                    DLog.Debug($"  YouTube: {ytAlbums.Count}");
                    allAlbums.AddRange(ytAlbums);
                }

                DLog.Debug($"  → Total: {allAlbums.Count} albums");
                return allAlbums;
            }

            // For specific sources, use the appropriate downloader with caching
            if (source == Source.KHInsider || source == Source.YouTube || source == Source.Zophar)
            {
                // Check cache only if not skipping
                if (!skipCache && _cacheService != null && _cacheService.TryGetCachedAlbums(gameName, source, out var cachedAlbums))
                {
                    return cachedAlbums;
                }

                var downloader = GetDownloaderForSource(source);
                if (downloader == null)
                {
                    Logger.Warn($"No downloader available for source: {source}");
                    return Enumerable.Empty<Album>();
                }

                var albums = downloader.GetAlbumsForGame(gameName, cancellationToken, auto)?.ToList()
                    ?? new List<Album>();

                // Only cache if not skipping cache
                if (!skipCache && _cacheService != null)
                {
                    _cacheService.CacheSearchResult(gameName, source, albums);
                }

                return albums;
            }
            else
            {
                var downloader = GetDownloaderForSource(source);
                if (downloader == null)
                {
                    Logger.Warn($"No downloader available for source: {source}");
                    return Enumerable.Empty<Album>();
                }

                return downloader.GetAlbumsForGame(gameName, cancellationToken, auto);
            }
        }

        /// <summary>
        /// Gets KHInsider albums using multiple search strategies to improve match rate.
        /// KHInsider album titles often differ slightly from game names (e.g., colons removed).
        /// </summary>
        private List<Album> GetKHInsiderAlbumsWithStrategies(string gameName, CancellationToken cancellationToken, bool auto)
        {
            var khAlbums = new List<Album>();

            // Pre-process: Normalize game name for search (removes trademark symbols, edition suffixes, etc.)
            var originalName = gameName;
            var normalizedName = StringHelper.NormalizeSearchQuery(gameName);
            if (!normalizedName.Equals(originalName, StringComparison.Ordinal))
            {
                DLog.Debug($"KHInsider: Normalized '{originalName}' -> '{normalizedName}'");
            }

            // Check for KHInsider album hint FIRST (user-provided override)
            var hint = _hintsService?.GetHint(originalName) ?? _hintsService?.GetHint(normalizedName);
            gameName = normalizedName;
            if (hint != null && !string.IsNullOrWhiteSpace(hint.KHInsiderAlbum))
            {
                DLog.Debug($"KHInsider: Using hint '{hint.KHInsiderAlbum}' for '{gameName}'");
                var hintAlbum = CreateAlbumFromKHInsiderSlug(hint.KHInsiderAlbum, gameName);
                if (hintAlbum != null)
                {
                    khAlbums.Add(hintAlbum);
                    return khAlbums;
                }
            }

            // Strategy 1: Exact game name
            var strategy1 = _khDownloader?.GetAlbumsForGame(gameName, cancellationToken, auto)?.ToList()
                ?? new List<Album>();
            khAlbums.AddRange(strategy1);

            if (khAlbums.Count > 0)
            {
                DLog.Debug($"KHInsider: Found {khAlbums.Count} with exact name");
                return khAlbums;
            }

            // Strategy 2: Replace colons, dashes, and special characters with spaces
            var cleanedName = Regex.Replace(gameName, @"[:–—\-]+", " ");
            cleanedName = Regex.Replace(cleanedName, @"\s+", " ").Trim();

            if (!cleanedName.Equals(gameName, StringComparison.OrdinalIgnoreCase))
            {
                var strategy2 = _khDownloader?.GetAlbumsForGame(cleanedName, cancellationToken, auto)?.ToList()
                    ?? new List<Album>();
                khAlbums.AddRange(strategy2);

                if (khAlbums.Count > 0)
                {
                    DLog.Debug($"KHInsider: Found {khAlbums.Count} with cleaned name");
                    return khAlbums;
                }
            }

            // Strategy 3: Simplified name (strip edition suffixes)
            var simplifiedName = StringHelper.StripGameNameSuffixes(gameName);
            if (!string.IsNullOrWhiteSpace(simplifiedName) &&
                !simplifiedName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                !simplifiedName.Equals(cleanedName, StringComparison.OrdinalIgnoreCase))
            {
                var strategy3 = _khDownloader?.GetAlbumsForGame(simplifiedName, cancellationToken, auto)?.ToList()
                    ?? new List<Album>();
                khAlbums.AddRange(strategy3);

                if (khAlbums.Count > 0)
                {
                    DLog.Debug($"KHInsider: Found {khAlbums.Count} with simplified name");
                    return khAlbums;
                }

                // Strategy 4: Simplified name with colons/dashes cleaned
                var simplifiedCleaned = Regex.Replace(simplifiedName, @"[:–—\-]+", " ");
                simplifiedCleaned = Regex.Replace(simplifiedCleaned, @"\s+", " ").Trim();

                if (!simplifiedCleaned.Equals(simplifiedName, StringComparison.OrdinalIgnoreCase) &&
                    !simplifiedCleaned.Equals(cleanedName, StringComparison.OrdinalIgnoreCase))
                {
                    var strategy4 = _khDownloader?.GetAlbumsForGame(simplifiedCleaned, cancellationToken, auto)?.ToList()
                        ?? new List<Album>();
                    khAlbums.AddRange(strategy4);
                }
            }

            return khAlbums;
        }

        /// <summary>
        /// Gets YouTube albums with caching and search hints support.
        /// </summary>
        private List<Album> GetYouTubeAlbumsWithCache(string gameName, CancellationToken cancellationToken, bool auto, bool skipCache = false)
        {
            var seenIds = new HashSet<string>();
            var ytAlbums = new List<Album>();

            // Pre-process: Normalize game name for search
            var originalName = gameName;
            var normalizedName = StringHelper.NormalizeSearchQuery(gameName);
            if (!normalizedName.Equals(originalName, StringComparison.Ordinal))
            {
                DLog.Debug($"YouTube: Normalized '{originalName}' -> '{normalizedName}'");
            }

            // Check for search hints FIRST (user-provided overrides)
            var hint = _hintsService?.GetHint(originalName) ?? _hintsService?.GetHint(normalizedName);
            gameName = normalizedName;
            if (hint != null)
            {
                // If direct playlist ID is provided, use it (highest priority)
                if (!string.IsNullOrWhiteSpace(hint.YouTubePlaylistId))
                {
                    DLog.Debug($"YouTube: Using hint playlist '{hint.YouTubePlaylistId}' for '{gameName}'");
                    var playlistAlbum = CreateAlbumFromPlaylistId(hint.YouTubePlaylistId, gameName);
                    if (playlistAlbum != null)
                    {
                        ytAlbums.Add(playlistAlbum);
                        CacheAndReturn(gameName, ytAlbums, skipCache);
                        return ytAlbums;
                    }
                }

                // If custom search terms are provided, use those instead of default
                if (hint.SearchTerms != null && hint.SearchTerms.Count > 0)
                {
                    DLog.Debug($"YouTube: Using hint search terms for '{gameName}'");
                    foreach (var query in hint.SearchTerms)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        SearchAndAddResults(query, ytAlbums, seenIds, cancellationToken, auto);
                        if (ytAlbums.Count >= 20) break;
                    }

                    // Mark all albums from hint search terms as "HintSearch" so they bypass strict filtering
                    foreach (var album in ytAlbums)
                    {
                        album.Type = "HintSearch";
                    }

                    CacheAndReturn(gameName, ytAlbums, skipCache);
                    return ytAlbums;
                }
            }

            // Check cache for default searches (only if no hint was used)
            if (!skipCache && _cacheService != null && _cacheService.TryGetCachedAlbums(gameName, Source.YouTube, out var cachedAlbums))
            {
                DLog.Debug($"YouTube: {cachedAlbums.Count} cached for '{gameName}'");
                return cachedAlbums;
            }

            // Default search: use base game name with OST/soundtrack/music suffixes
            var baseGameName = StringHelper.ExtractBaseGameName(gameName);
            var searchQueries = new[] { $"{baseGameName} OST", $"{baseGameName} soundtrack", $"{baseGameName} music" };

            foreach (var query in searchQueries)
            {
                if (cancellationToken.IsCancellationRequested) break;
                SearchAndAddResults(query, ytAlbums, seenIds, cancellationToken, auto);
                if (ytAlbums.Count >= 20) break;
            }

            CacheAndReturn(gameName, ytAlbums, skipCache);
            return ytAlbums;
        }

        private void SearchAndAddResults(string query, List<Album> albums, HashSet<string> seenIds, CancellationToken ct, bool auto)
        {
            var results = _ytDownloader?.GetAlbumsForGame(query, ct, auto)?.ToList() ?? new List<Album>();
            foreach (var album in results)
            {
                if (!string.IsNullOrEmpty(album.Id) && seenIds.Add(album.Id))
                    albums.Add(album);
            }
            DLog.Debug($"YouTube: '{query}' -> {results.Count} results");
        }

        private void CacheAndReturn(string gameName, List<Album> albums, bool skipCache)
        {
            if (!skipCache && _cacheService != null)
                _cacheService.CacheSearchResult(gameName, Source.YouTube, albums);
        }

        private Album CreateAlbumFromPlaylistId(string playlistId, string gameName)
        {
            // Create a synthetic album entry for the playlist
            // The actual songs will be fetched when GetSongsFromAlbum is called
            return new Album
            {
                Id = playlistId,
                Name = $"{gameName} (UPS Hint)",
                Source = Source.YouTube,
                Type = "Hint"
            };
        }

        private Album CreateAlbumFromKHInsiderSlug(string albumSlug, string gameName)
        {
            // Create an album entry for the KHInsider album slug
            // KHInsiderDownloader.GetSongsFromAlbum builds URL as: BaseUrl + album.Id
            // Album IDs from search are stored as "game-soundtracks/album/{slug}"
            // So we need to prefix the slug with the path
            var albumId = $"game-soundtracks/album/{albumSlug}";
            return new Album
            {
                Id = albumId,
                Name = $"{gameName} (UPS Hint)",
                Source = Source.KHInsider,
                Type = "Hint"
            };
        }

        private const int MinFuzzyScoreTrusted = 50;  // KHInsider, Zophar
        private const int MinFuzzyScoreYouTube = 70;
        private const int YouTubeFuzzyFallback = 85;
        private const int BroaderThreshold = 50;

        // Roman ↔ Arabic numeral mappings for game series matching
        private static readonly Dictionary<string, string> RomanToArabic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "i", "1" }, { "ii", "2" }, { "iii", "3" }, { "iv", "4" }, { "v", "5" },
            { "vi", "6" }, { "vii", "7" }, { "viii", "8" }, { "ix", "9" }, { "x", "10" },
            { "xi", "11" }, { "xii", "12" }, { "xiii", "13" }, { "xiv", "14" }, { "xv", "15" },
            { "xvi", "16" }, { "xvii", "17" }, { "xviii", "18" }, { "xix", "19" }, { "xx", "20" }
        };

        private static readonly Dictionary<string, string> ArabicToRoman = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "1", "i" }, { "2", "ii" }, { "3", "iii" }, { "4", "iv" }, { "5", "v" },
            { "6", "vi" }, { "7", "vii" }, { "8", "viii" }, { "9", "ix" }, { "10", "x" },
            { "11", "xi" }, { "12", "xii" }, { "13", "xiii" }, { "14", "xiv" }, { "15", "xv" },
            { "16", "xvi" }, { "17", "xvii" }, { "18", "xviii" }, { "19", "xix" }, { "20", "xx" }
        };

        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "of", "and", "or", "in", "on", "at", "to", "for",
            "with", "by", "from", "as", "is", "was", "are", "be", "been",
            "game", "edition", "version", "vol", "volume"
        };

        private static readonly Regex SuffixPattern = new Regex(
            @"\s*(original\s+)?soundtrack.*$|\s*ost.*$|\s*music.*$|\s*score.*$|\s*bgm.*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Album BestAlbumPick(IEnumerable<Album> albums, Game game)
        {
            var albumsList = albums?.ToList() ?? new List<Album>();
            if (albumsList.Count == 0) return null;

            // Log to downloader.log for debugging
            DLog.Debug($"BestAlbumPick: '{game.Name}' - {albumsList.Count} albums");

            // Priority 0: Hint albums from search_hints.json (highest priority - user-defined overrides)
            var hintAlbum = albumsList.FirstOrDefault(a => a.Type == "Hint");
            if (hintAlbum != null)
            {
                DLog.Debug($"  → Hint: '{hintAlbum.Name}'");
                return hintAlbum;
            }

            // Priority 0.5: First result from hint search terms (trusted user-provided search)
            var hintSearchAlbum = albumsList.FirstOrDefault(a => a.Type == "HintSearch");
            if (hintSearchAlbum != null)
            {
                DLog.Debug($"  → HintSearch: '{hintSearchAlbum.Name}'");
                return hintSearchAlbum;
            }

            var gameName = NormalizeForMatching(game.Name);
            var keyWords = ExtractKeyWords(game.Name);
            var seriesNumbers = ExtractSeriesNumbers(keyWords);

            // Separate by source
            var khAlbums = albumsList.Where(a => a.Source == Source.KHInsider).ToList();
            var zopharAlbums = albumsList.Where(a => a.Source == Source.Zophar).ToList();
            var ytAlbums = albumsList.Where(a => a.Source == Source.YouTube && IsLikelyGameMusic(a, game, auto: true)).ToList();

            // Priority 1: KHInsider with ALL keywords
            var result = FindBestWithKeywords(khAlbums, gameName, keyWords, 0, "KH");
            if (result != null) return result;

            // Priority 2: KHInsider fuzzy (must match series number)
            result = FindBestFuzzy(khAlbums, gameName, seriesNumbers, MinFuzzyScoreTrusted, "KH");
            if (result != null) return result;

            // Priority 3: Zophar with ALL keywords
            result = FindBestWithKeywords(zopharAlbums, gameName, keyWords, 0, "ZO");
            if (result != null) return result;

            // Priority 4: Zophar fuzzy (must match series number)
            result = FindBestFuzzy(zopharAlbums, gameName, seriesNumbers, MinFuzzyScoreTrusted, "ZO");
            if (result != null) return result;

            // Priority 5: YouTube with ALL keywords
            result = FindBestWithKeywords(ytAlbums, gameName, keyWords, MinFuzzyScoreYouTube, "YT");
            if (result != null) return result;

            // Priority 6: YouTube fuzzy fallback (high threshold, must match series)
            result = FindBestFuzzy(ytAlbums, gameName, seriesNumbers, YouTubeFuzzyFallback, "YT");
            if (result != null) return result;

            DLog.Warn($"  → No match for '{game.Name}'");
            return null;
        }

        /// <summary>
        /// Broader matching for retry operations. Still enforces series number matching.
        /// </summary>
        public Album BestAlbumPickBroader(IEnumerable<Album> albums, Game game)
        {
            var albumList = albums?.ToList();
            if (albumList == null || albumList.Count == 0) return null;

            // Priority 0: Hint albums from search_hints.json (highest priority)
            var hintAlbum = albumList.FirstOrDefault(a => a.Type == "Hint");
            if (hintAlbum != null)
            {
                DLog.Debug($"BroaderPick: Hint '{hintAlbum.Name}'");
                return hintAlbum;
            }

            // Priority 0.5: First result from hint search terms
            var hintSearchAlbum = albumList.FirstOrDefault(a => a.Type == "HintSearch");
            if (hintSearchAlbum != null)
            {
                DLog.Debug($"BroaderPick: HintSearch '{hintSearchAlbum.Name}'");
                return hintSearchAlbum;
            }

            var gameName = StringHelper.ExtractBaseGameName(game?.Name ?? "");
            var keyWords = ExtractKeyWords(game?.Name ?? "");
            var seriesNumbers = ExtractSeriesNumbers(keyWords);

            var scored = albumList
                .Select(a => new { Album = a, Score = Fuzz.TokenSetRatio(gameName, CleanAlbumName(a.Name)) })
                .Where(x => MatchesSeriesNumber(x.Album.Name, seriesNumbers))
                .OrderByDescending(x => x.Score)
                .ToList();

            var best = scored.FirstOrDefault(x => x.Score >= BroaderThreshold)
                    ?? (scored.FirstOrDefault()?.Score >= 30 ? scored.First() : null);

            if (best != null)
            {
                DLog.Debug($"BroaderPick: '{best.Album.Name}' (score {best.Score})");
                return best.Album;
            }

            return null;
        }

        private Album FindBestWithKeywords(List<Album> albums, string gameName, List<string> keyWords, int minScore, string tag)
        {
            if (!albums.Any()) return null;

            var matches = albums
                .Where(a => ContainsAllKeyWords(a.Name, keyWords))
                .Select(a => new { Album = a, Score = Fuzz.TokenSetRatio(gameName, CleanAlbumName(a.Name)) })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (matches != null && matches.Score >= minScore)
            {
                DLog.Debug($"  → [{tag}] '{matches.Album.Name}' (score {matches.Score})");
                return matches.Album;
            }
            return null;
        }

        private Album FindBestFuzzy(List<Album> albums, string gameName, List<string> seriesNumbers, int minScore, string tag)
        {
            if (!albums.Any()) return null;

            var matches = albums
                .Select(a => new { Album = a, Score = Fuzz.TokenSetRatio(gameName, CleanAlbumName(a.Name)) })
                .Where(x => MatchesSeriesNumber(x.Album.Name, seriesNumbers) && x.Score >= minScore)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (matches != null)
            {
                DLog.Debug($"  → [{tag}] '{matches.Album.Name}' (score {matches.Score})");
                return matches.Album;
            }
            return null;
        }

        private List<string> ExtractSeriesNumbers(List<string> keyWords)
        {
            return keyWords.Where(k => Regex.IsMatch(k, @"^\d{1,2}$") && int.Parse(k) <= 20).ToList();
        }

        private bool MatchesSeriesNumber(string albumName, List<string> seriesNumbers)
        {
            return seriesNumbers.Count == 0 || seriesNumbers.All(num => ContainsAllKeyWords(albumName, new List<string> { num }));
        }

        private string CleanAlbumName(string albumName)
        {
            if (string.IsNullOrWhiteSpace(albumName)) return string.Empty;
            return SuffixPattern.Replace(NormalizeForMatching(albumName), "").Trim();
        }

        private List<string> ExtractKeyWords(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return new List<string>();

            var baseName = StringHelper.ExtractBaseGameName(gameName);
            var words = NormalizeForMatching(baseName).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var keyWords = words
                .Where(w => w.Length >= 2 || Regex.IsMatch(w, @"^[1-9]$"))
                .Where(w => !StopWords.Contains(w))
                .Where(w => !Regex.IsMatch(w, @"^\d{4,}$"))
                .ToList();

            if (keyWords.Count == 0 && words.Length > 0)
                keyWords.Add(words.First(w => w.Length >= 2));

            return keyWords;
        }

        private bool ContainsAllKeyWords(string albumName, List<string> keyWords)
        {
            if (keyWords == null || keyWords.Count == 0) return true;

            var normalized = NormalizeForMatching(albumName);
            return keyWords.All(kw => normalized.Contains(kw) ||
                (GetNumeralAlternate(kw) is string alt && normalized.Contains(alt)));
        }

        private string GetNumeralAlternate(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return null;
            var lower = keyword.ToLowerInvariant();
            return RomanToArabic.TryGetValue(lower, out var arabic) ? arabic
                 : ArabicToRoman.TryGetValue(lower, out var roman) ? roman : null;
        }

        // Keywords that indicate non-music content (gameplay, streams, covers, etc.)
        private static readonly string[] RejectKeywords = {
            "[eng sub]", "[sub]", "[dubbed]", "episode", "ep.", "ep ", "e1", "e2", "e3", "s1", "s2",
            "drama", "movie", "film", "trailer", "review", "reaction",
            "gameplay", "walkthrough", "let's play", "lets play", "playthrough", "longplay",
            "full game", "all cutscenes", "no commentary", "blind playthrough", "first playthrough",
            "100%", "speedrun", "tutorial", "guide", "how to",
            "cover", "remix", "fan made", "fanmade", "mashup",
            "1 hour", "2 hour", "3 hour", "10 hour", "extended", "loop",
            "stream", "live stream", "vod", "twitch",
            "part 1", "part 2", "part 3", "part 4", "part 5",
            "pt.1", "pt.2", "pt.3", "pt 1", "pt 2", "pt 3", "#1", "#2", "#3", "| 1", "| 2", "| 3"
        };

        private static readonly string[] MusicKeywords = { "ost", "soundtrack", "original soundtrack", "game music", "bgm", "score", "theme", "music" };

        private bool IsLikelyGameMusic(Album album, Game game, bool auto)
        {
            if (album == null || string.IsNullOrWhiteSpace(album.Name)) return false;

            var albumLower = album.Name.ToLowerInvariant();

            // Reject non-music content
            if (RejectKeywords.Any(kw => albumLower.Contains(kw))) return false;

            // KHInsider/Zophar are trusted music sources
            if (album.Source == Source.KHInsider || album.Source == Source.Zophar) return true;

            // YouTube: Must contain music-related keywords
            if (!MusicKeywords.Any(kw => albumLower.Contains(kw))) return false;

            // YouTube auto-mode: Require minimum fuzzy match
            if (auto)
            {
                var normalizedGame = NormalizeForMatching(game.Name);
                var cleanedAlbum = CleanAlbumName(album.Name);
                var score = Fuzz.TokenSetRatio(normalizedGame, cleanedAlbum);
                if (score < MinFuzzyScoreYouTube)
                {
                    DLog.Debug($"YouTube: REJECT '{album.Name}' (score {score})");
                    return false;
                }
            }

            return true;
        }

        public List<Song> BestSongPick(IEnumerable<Song> songs, string gameName, int maxSongs = 1)
        {
            var songsList = songs?.ToList() ?? new List<Song>();

            if (songsList.Count == 0)
                return new List<Song>();

            // Score all songs
            var scoredSongs = songsList.Select(song => new
            {
                Song = song,
                Score = CalculateSongRelevance(song, gameName)
            })
            .OrderByDescending(x => x.Score)
            .ToList();

            // Filter out songs that were hard-rejected (int.MinValue score = exceeded duration limit)
            var validSongs = scoredSongs.Where(x => x.Score > int.MinValue).ToList();

            if (validSongs.Count == 0)
            {
                // All songs exceeded duration limit - this is likely a gameplay video album
                var maxDuration = songsList.Where(s => s.Length.HasValue).Max(s => s.Length.Value.TotalMinutes);
                DLog.Warn($"All {songsList.Count} songs for '{gameName}' exceed {Constants.MaxAllowedSongDurationMinutes}min limit (longest: {maxDuration:F1}min). Album likely contains gameplay videos.");
                Logger.Warn($"[{LogPrefix}] No valid songs for '{gameName}' - all exceed {Constants.MaxAllowedSongDurationMinutes}min duration limit");
                return new List<Song>();
            }

            var result = validSongs.Take(maxSongs).Select(x => x.Song).ToList();

            if (result.Any())
            {
                DLog.Debug($"Song pick for '{gameName}': '{result[0].Name}'");
            }

            return result;
        }

        /// <summary>
        /// Picks the best songs with detailed rejection reason if no songs are valid.
        /// </summary>
        /// <returns>Tuple of (songs, rejectionReason) where rejectionReason is null if songs were found</returns>
        public (List<Song> songs, string rejectionReason) BestSongPickWithReason(IEnumerable<Song> songs, string gameName, int maxSongs = 1)
        {
            var songsList = songs?.ToList() ?? new List<Song>();

            if (songsList.Count == 0)
                return (new List<Song>(), null);

            // Score all songs
            var scoredSongs = songsList.Select(song => new
            {
                Song = song,
                Score = CalculateSongRelevance(song, gameName)
            })
            .OrderByDescending(x => x.Score)
            .ToList();

            // Filter out songs that were hard-rejected (int.MinValue score = exceeded duration limit)
            var validSongs = scoredSongs.Where(x => x.Score > int.MinValue).ToList();
            var rejectedDueToDuration = scoredSongs.Count - validSongs.Count;

            if (validSongs.Count == 0 && rejectedDueToDuration > 0)
            {
                // All songs exceeded duration limit - likely gameplay videos
                var longestSong = songsList.Where(s => s.Length.HasValue).OrderByDescending(s => s.Length.Value).FirstOrDefault();
                var durationInfo = longestSong != null ? $" (longest: {longestSong.Length.Value.TotalMinutes:F0}min)" : "";
                var reason = $"All {rejectedDueToDuration} songs exceed {Constants.MaxAllowedSongDurationMinutes}min limit{durationInfo}. Album likely contains gameplay videos.";
                DLog.Warn($"Duration filter: {reason}");
                return (new List<Song>(), reason);
            }

            if (validSongs.Count == 0)
                return (new List<Song>(), null);

            var result = validSongs.Take(maxSongs).Select(x => x.Song).ToList();

            if (result.Any())
            {
                DLog.Debug($"Song pick for '{gameName}': '{result[0].Name}'");
            }

            return (result, null);
        }

        private int CalculateSongRelevance(Song song, string gameName)
        {
            if (song == null)
                return 0;

            // HARD FILTER: Reject songs over 30 minutes (likely gameplay videos, streams, or compilations)
            if (song.Length.HasValue && song.Length.Value > MaxAllowedDuration)
            {
                DLog.Debug($"Rejecting '{song.Name}' - duration {song.Length.Value.TotalMinutes:F1}min exceeds {MaxAllowedDuration.TotalMinutes}min limit");
                return int.MinValue;
            }

            var score = 0;
            var songName = song.Name?.ToLowerInvariant() ?? string.Empty;
            var cleanGameName = NormalizeForMatching(gameName);
            
            // High priority: Theme/Title songs (these are what we want for game previews)
            if (songName.Contains("title screen") || songName.Contains("title theme"))
                score += 5000;
            if (songName.Contains("main theme") || songName.Contains("main menu"))
                score += 4500;
            if (songName.Contains("opening theme") || songName.Contains("opening"))
                score += 4000;
            
            var highPriorityKeywords = new[] { "theme", "title", "menu", "intro", "prologue" };
            foreach (var keyword in highPriorityKeywords)
            {
                if (songName.Contains(keyword))
                {
                    score += 2000;
                    break;
                }
            }
            
            // Medium priority: Game name relevance
            if (!string.IsNullOrEmpty(cleanGameName) && cleanGameName.Length > 2)
            {
                if (songName.Contains(cleanGameName))
                    score += 1500;
                    
                if (songName.StartsWith(cleanGameName))
                    score += 500;
            }
            
            // Positive indicators
            if (songName.Contains("overworld") || songName.Contains("hub") || songName.Contains("world map"))
                score += 800;
                
            if (songName.Contains("protagonist") || songName.Contains("hero"))
                score += 600;
            if (songName.Contains("stage 1") || songName.Contains("level 1") || 
                songName.Contains("chapter 1") || songName.Contains("act 1"))
                score += 400;
            
            // Negative indicators (things to avoid for auto-selection)
            if (songName.Contains("remix") || songName.Contains("cover") || 
                songName.Contains("arrange") || songName.Contains("arranged"))
                score -= 1000;
            
            if (songName.Contains("extended") || songName.Contains("loop") || 
                songName.Contains("10 hour") || songName.Contains("1 hour"))
                score -= 2000;
            
            if (songName.Contains("battle") || songName.Contains("boss") || 
                songName.Contains("combat") || songName.Contains("fight"))
                score -= 300;
            
            if (songName.Contains("game over") || songName.Contains("death") || 
                songName.Contains("ending") || songName.Contains("credits") ||
                songName.Contains("sad") || songName.Contains("tragic"))
                score -= 500;
            
            if (songName.Contains("sfx") || songName.Contains("sound effect") || 
                songName.Contains("jingle") || songName.Contains("fanfare"))
                score -= 1500;
            
            // Song length scoring
            if (song.Length.HasValue)
            {
                var minutes = song.Length.Value.TotalMinutes;
                
                // Ideal length is 1.5-4 minutes (typical theme/title music)
                if (minutes >= 1.5 && minutes <= 4)
                    score += 300;
                else if (minutes >= 1 && minutes <= 5)
                    score += 150;
                else if (minutes < 0.5)
                    score -= 500; // Too short (probably jingle)
                else if (minutes > MaxSongLength.TotalMinutes)
                    score -= 1000; // Too long
            }
            
            // Track position bonus: Early tracks are often title/menu music
            // Track numbers may appear in song names (e.g., "01 Title Theme")
            if (Regex.IsMatch(songName, @"^(01|1\.|track\s*1|#1)\s"))
                score += 200;
            else if (Regex.IsMatch(songName, @"^(02|2\.|track\s*2|#2)\s"))
                score += 100;
            
            return score;
        }

        public IEnumerable<Song> GetSongsFromAlbum(Album album, CancellationToken cancellationToken)
        {
            if (album == null)
                return Enumerable.Empty<Song>();

            if (_errorHandler != null)
            {
                return _errorHandler.Try(
                    () =>
                    {
                        var downloader = GetDownloaderForSource(album.Source);
                        if (downloader == null)
                        {
                            Logger.Warn($"No downloader available for source: {album.Source}");
                            return Enumerable.Empty<Song>();
                        }

                        return downloader.GetSongsFromAlbum(album, cancellationToken);
                    },
                    defaultValue: Enumerable.Empty<Song>(),
                    context: $"getting songs from album '{album.Name}'"
                );
            }
            else
            {
                // Fallback to original error handling
                try
                {
                    var downloader = GetDownloaderForSource(album.Source);
                    if (downloader == null)
                    {
                        Logger.Warn($"No downloader available for source: {album.Source}");
                        return Enumerable.Empty<Song>();
                    }

                    return downloader.GetSongsFromAlbum(album, cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error getting songs from album '{album.Name}': {ex.Message}");
                    return Enumerable.Empty<Song>();
                }
            }
        }

        public bool DownloadSong(Song song, string path, CancellationToken cancellationToken, bool isPreview = false)
        {
            if (song == null)
            {
                Logger.Warn("Cannot download: song is null");
                return false;
            }

            if (_errorHandler != null)
            {
                return _errorHandler.Try(
                    () =>
                    {
                        var downloader = GetDownloaderForSource(song.Source);
                        if (downloader == null)
                        {
                            Logger.Warn($"No downloader available for source: {song.Source}");
                            return false;
                        }

                        // Use temp path if no path specified
                        var downloadPath = string.IsNullOrWhiteSpace(path) ? GetTempPath(song) : path;

                        // Ensure directory exists
                        var directory = Path.GetDirectoryName(downloadPath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        var success = downloader.DownloadSong(song, downloadPath, cancellationToken, isPreview);

                        // Move from temp if needed
                        if (success && !string.IsNullOrWhiteSpace(path) && downloadPath != path && File.Exists(downloadPath))
                        {
                            if (File.Exists(path))
                            {
                                File.Delete(path);
                            }
                            File.Move(downloadPath, path);
                        }

                        return success;
                    },
                    defaultValue: false,
                    context: $"downloading song '{song.Name}'"
                );
            }
            else
            {
                // Fallback to original error handling
                try
                {
                    var downloader = GetDownloaderForSource(song.Source);
                    if (downloader == null)
                    {
                        Logger.Warn($"No downloader available for source: {song.Source}");
                        return false;
                    }

                    // Use temp path if no path specified
                    var downloadPath = string.IsNullOrWhiteSpace(path) ? GetTempPath(song) : path;

                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(downloadPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var success = downloader.DownloadSong(song, downloadPath, cancellationToken, isPreview);

                    // Move from temp if needed
                    if (success && !string.IsNullOrWhiteSpace(path) && downloadPath != path && File.Exists(downloadPath))
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                        File.Move(downloadPath, path);
                    }

                    return success;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error downloading song '{song.Name}': {ex.Message}");
                    return false;
                }
            }
        }

        public void Cleanup()
        {
            // Use ErrorHandlerService if available, otherwise fall back to direct logging
            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () =>
                    {
                        if (Directory.Exists(_tempPath))
                        {
                            Directory.Delete(_tempPath, true);
                        }
                        Directory.CreateDirectory(_tempPath);
                    },
                    context: "cleaning up temp directory"
                );
            }
            else
            {
                // Fallback to original error handling if ErrorHandlerService not available
                try
                {
                    if (Directory.Exists(_tempPath))
                    {
                        Directory.Delete(_tempPath, true);
                    }
                    Directory.CreateDirectory(_tempPath);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Error cleaning up temp directory: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets hint-based albums for a game (from search_hints.json).
        /// Returns albums created from YouTubePlaylistId, KHInsiderAlbum, and SoundCloudUrl hints.
        /// </summary>
        public List<Album> GetHintAlbums(string gameName)
        {
            var hintAlbums = new List<Album>();

            if (_hintsService == null || string.IsNullOrWhiteSpace(gameName))
            {
                return hintAlbums;
            }

            // Try original name first, then normalized name (consistent with other hint lookups)
            var hint = _hintsService.GetHint(gameName);
            var normalizedName = StringHelper.NormalizeSearchQuery(gameName);
            if (hint == null && !normalizedName.Equals(gameName, StringComparison.Ordinal))
            {
                hint = _hintsService.GetHint(normalizedName);
            }

            if (hint == null)
            {
                return hintAlbums;
            }

            DLog.Debug($"Hint found for '{gameName}': KH={!string.IsNullOrWhiteSpace(hint.KHInsiderAlbum)}, SC={!string.IsNullOrWhiteSpace(hint.SoundCloudUrl)}, YT={!string.IsNullOrWhiteSpace(hint.YouTubePlaylistId)}");

            // Priority order: KHInsider (best quality) → SoundCloud → YouTube (last resort)

            // Add KHInsider album if available (highest priority)
            if (!string.IsNullOrWhiteSpace(hint.KHInsiderAlbum))
            {
                var albumId = $"game-soundtracks/album/{hint.KHInsiderAlbum}";
                hintAlbums.Add(new Album
                {
                    Id = albumId,
                    Name = $"{gameName} (UPS Hint - KHInsider)",
                    Source = Source.KHInsider,
                    Type = "Hint"
                });
                DLog.Debug($"Hint: KHInsider album for '{gameName}'");
            }

            // Add SoundCloud track/playlist if available
            if (!string.IsNullOrWhiteSpace(hint.SoundCloudUrl))
            {
                hintAlbums.Add(new Album
                {
                    Id = hint.SoundCloudUrl,
                    Name = $"{gameName} (UPS Hint - SoundCloud)",
                    Source = Source.SoundCloud,
                    Type = "Hint"
                });
                DLog.Debug($"Hint: SoundCloud URL for '{gameName}'");
            }

            // Add YouTube playlist album if available (lowest priority)
            if (!string.IsNullOrWhiteSpace(hint.YouTubePlaylistId))
            {
                hintAlbums.Add(new Album
                {
                    Id = hint.YouTubePlaylistId,
                    Name = $"{gameName} (UPS Hint - YouTube Playlist)",
                    Source = Source.YouTube,
                    Type = "Hint"
                });
                DLog.Debug($"Hint: YouTube playlist for '{gameName}'");
            }

            return hintAlbums;
        }

        private IDownloader GetDownloaderForSource(Source source)
        {
            switch (source)
            {
                case Source.KHInsider:
                    return _khDownloader;
                case Source.Zophar:
                    return _zopharDownloader;
                case Source.YouTube:
                    return _ytDownloader;
                case Source.SoundCloud:
                    return _soundCloudDownloader;
                case Source.All:
                    return _khDownloader;
                default:
                    DLog.Warn($"Unknown source: {source}");
                    return null;
            }
        }

        /// <summary>
        /// Normalizes a string for matching (lowercase, remove special chars, collapse spaces)
        /// </summary>
        private string NormalizeForMatching(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Remove special characters, keeping only alphanumeric and spaces
            var normalized = Regex.Replace(name, @"[^\w\s]", " ");
            // Collapse multiple spaces
            normalized = Regex.Replace(normalized, @"\s+", " ");
            return normalized.Trim().ToLowerInvariant();
        }

        public string GetTempPath(Song song)
        {
            if (song == null)
                return null;
                
            // Create a hash-based temp filename
            // SHA256 provides deterministic, collision-resistant filenames from song identifiers
            // This enables caching: same song ID = same filename = reuses existing download
            var hash = BitConverter.ToString(
                System.Security.Cryptography.SHA256.Create()
                    .ComputeHash(System.Text.Encoding.UTF8.GetBytes(song.Id ?? song.Name ?? Guid.NewGuid().ToString())))
                .Replace("-", "");

            var extension = Path.GetExtension(song.Id ?? ".mp3");
            if (string.IsNullOrEmpty(extension))
                extension = ".mp3";

            return Path.Combine(_tempPath, hash + extension);
        }
    }
}

