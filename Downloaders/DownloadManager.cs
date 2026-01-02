using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
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
        private static readonly TimeSpan MaxSongLength = new TimeSpan(0, Constants.MaxPreviewSongLengthMinutes, 0);
        private static readonly List<string> PreferredSongEndings = Constants.PreferredSongEndings;

        /// <summary>
        /// Logs a debug message if debug logging is enabled.
        /// </summary>
        private static void LogDebug(string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                Logger.Debug(message);
            }
        }

        private readonly IDownloader _khDownloader;
        private readonly IDownloader _ytDownloader;
        private readonly string _tempPath;
        private readonly ErrorHandlerService _errorHandler;
        private readonly SearchCacheService _cacheService;
        private readonly UniPlaySongSettings _settings;

        public DownloadManager(HttpClient httpClient, HtmlAgilityPack.HtmlWeb htmlWeb, string tempPath,
            string ytDlpPath = null, string ffmpegPath = null, ErrorHandlerService errorHandler = null,
            SearchCacheService cacheService = null, UniPlaySongSettings settings = null)
        {
            _tempPath = tempPath ?? throw new ArgumentNullException(nameof(tempPath));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _cacheService = cacheService;
            _settings = settings;

            _khDownloader = new KHInsiderDownloader(httpClient, htmlWeb, errorHandler);
            var useFirefoxCookies = settings?.UseFirefoxCookies ?? false;
            _ytDownloader = new YouTubeDownloader(httpClient, ytDlpPath, ffmpegPath, useFirefoxCookies, errorHandler);

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
            // For Source.All: Try KHInsider first, fallback to YouTube if no results
            if (source == Source.All)
            {
                LogDebug($"[Source.All] Searching for '{gameName}' (skipCache={skipCache})");
                LogDebug($"[Source.All] Step 1/2: Trying KHInsider...");

                List<Album> khAlbums = null;

                // Check cache only if not skipping
                if (!skipCache && _cacheService != null && _cacheService.TryGetCachedAlbums(gameName, Source.KHInsider, out khAlbums))
                {
                    if (khAlbums.Count > 0)
                    {
                        LogDebug($"[Source.All] KHInsider (cached): Found {khAlbums.Count} album(s) for '{gameName}'");
                        return khAlbums;
                    }
                    else
                    {
                        LogDebug($"[Source.All] KHInsider (cached): No results for '{gameName}', skipping to YouTube");
                        LogDebug($"[Source.All] Step 2/2: Falling back to YouTube...");
                        return GetYouTubeAlbumsWithCache(gameName, cancellationToken, auto, skipCache);
                    }
                }

                khAlbums = GetKHInsiderAlbumsWithStrategies(gameName, cancellationToken, auto);

                // Only cache if not skipping cache
                if (!skipCache && _cacheService != null)
                {
                    _cacheService.CacheSearchResult(gameName, Source.KHInsider, khAlbums);
                }

                if (khAlbums.Count > 0)
                {
                    LogDebug($"[Source.All] KHInsider: Found {khAlbums.Count} album(s) for '{gameName}'");
                    return khAlbums;
                }

                LogDebug($"[Source.All] KHInsider: No results for '{gameName}'");
                LogDebug($"[Source.All] Step 2/2: Falling back to YouTube...");

                return GetYouTubeAlbumsWithCache(gameName, cancellationToken, auto, skipCache);
            }

            // For specific sources, use the appropriate downloader with caching
            if (source == Source.KHInsider || source == Source.YouTube)
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

            // Strategy 1: Exact game name
            Logger.Info($"[KHInsider] Strategy 1: Exact name '{gameName}'");
            var strategy1 = _khDownloader?.GetAlbumsForGame(gameName, cancellationToken, auto)?.ToList()
                ?? new List<Album>();
            khAlbums.AddRange(strategy1);

            if (khAlbums.Count > 0)
            {
                Logger.Info($"[KHInsider] Strategy 1 SUCCESS: {khAlbums.Count} result(s)");
                return khAlbums;
            }

            // Strategy 2: Replace colons, dashes, and special characters with spaces
            // e.g., "Hitman: Absolution" -> "Hitman Absolution"
            var cleanedName = Regex.Replace(gameName, @"[:–—\-]+", " ");
            cleanedName = Regex.Replace(cleanedName, @"\s+", " ").Trim();

            if (!cleanedName.Equals(gameName, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"[KHInsider] Strategy 2: Cleaned name '{cleanedName}'");
                var strategy2 = _khDownloader?.GetAlbumsForGame(cleanedName, cancellationToken, auto)?.ToList()
                    ?? new List<Album>();
                khAlbums.AddRange(strategy2);

                if (khAlbums.Count > 0)
                {
                    Logger.Info($"[KHInsider] Strategy 2 SUCCESS: {khAlbums.Count} result(s)");
                    return khAlbums;
                }
            }
            else
            {
                LogDebug($"[KHInsider] Strategy 2 SKIPPED: cleaned name same as original");
            }

            // Strategy 3: Simplified name (strip edition suffixes)
            // e.g., "Dishonored - Definitive Edition" -> "Dishonored"
            var simplifiedName = StringHelper.StripGameNameSuffixes(gameName);
            if (!string.IsNullOrWhiteSpace(simplifiedName) &&
                !simplifiedName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                !simplifiedName.Equals(cleanedName, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"[KHInsider] Strategy 3: Simplified name '{simplifiedName}'");
                var strategy3 = _khDownloader?.GetAlbumsForGame(simplifiedName, cancellationToken, auto)?.ToList()
                    ?? new List<Album>();
                khAlbums.AddRange(strategy3);

                if (khAlbums.Count > 0)
                {
                    Logger.Info($"[KHInsider] Strategy 3 SUCCESS: {khAlbums.Count} result(s)");
                    return khAlbums;
                }

                // Strategy 4: Simplified name with colons/dashes cleaned
                var simplifiedCleaned = Regex.Replace(simplifiedName, @"[:–—\-]+", " ");
                simplifiedCleaned = Regex.Replace(simplifiedCleaned, @"\s+", " ").Trim();

                if (!simplifiedCleaned.Equals(simplifiedName, StringComparison.OrdinalIgnoreCase) &&
                    !simplifiedCleaned.Equals(cleanedName, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info($"[KHInsider] Strategy 4: Simplified+cleaned name '{simplifiedCleaned}'");
                    var strategy4 = _khDownloader?.GetAlbumsForGame(simplifiedCleaned, cancellationToken, auto)?.ToList()
                        ?? new List<Album>();
                    khAlbums.AddRange(strategy4);

                    if (khAlbums.Count > 0)
                    {
                        Logger.Info($"[KHInsider] Strategy 4 SUCCESS: {khAlbums.Count} result(s)");
                    }
                }
                else
                {
                    LogDebug($"[KHInsider] Strategy 4 SKIPPED: simplified+cleaned same as previous");
                }
            }
            else
            {
                LogDebug($"[KHInsider] Strategies 3-4 SKIPPED: no edition suffix to strip");
            }

            Logger.Info($"[KHInsider] All strategies completed for '{gameName}': {khAlbums.Count} total result(s)");
            return khAlbums;
        }

        /// <summary>
        /// Gets YouTube albums with caching support, using multiple search strategies.
        /// </summary>
        private List<Album> GetYouTubeAlbumsWithCache(string gameName, CancellationToken cancellationToken, bool auto, bool skipCache = false)
        {
            // Check cache only if not skipping
            if (!skipCache && _cacheService != null && _cacheService.TryGetCachedAlbums(gameName, Source.YouTube, out var cachedAlbums))
            {
                LogDebug($"[Source.All] YouTube (cached): Found {cachedAlbums.Count} album(s) for '{gameName}'");
                return cachedAlbums;
            }

            // Try multiple YouTube search strategies
            var ytAlbums = new List<Album>();

            // Strategy 1: "[Game Name]" OST (quoted for exact match)
            LogDebug($"[YouTube] Strategy 1: Searching for '\"{gameName}\" OST'");
            var strategy1 = _ytDownloader?.GetAlbumsForGame($"\"{gameName}\" OST", cancellationToken, auto)?.ToList()
                ?? new List<Album>();
            ytAlbums.AddRange(strategy1);

            if (ytAlbums.Count > 0)
            {
                LogDebug($"[YouTube] Strategy 1 found {ytAlbums.Count} result(s)");
            }
            else
            {
                // Strategy 2: "[Game Name]" soundtrack
                LogDebug($"[YouTube] Strategy 2: Searching for '\"{gameName}\" soundtrack'");
                var strategy2 = _ytDownloader?.GetAlbumsForGame($"\"{gameName}\" soundtrack", cancellationToken, auto)?.ToList()
                    ?? new List<Album>();
                ytAlbums.AddRange(strategy2);

                if (ytAlbums.Count > 0)
                {
                    LogDebug($"[YouTube] Strategy 2 found {ytAlbums.Count} result(s)");
                }
                else
                {
                    // Strategy 3: [Game Name] original soundtrack
                    LogDebug($"[YouTube] Strategy 3: Searching for '{gameName} original soundtrack'");
                    var strategy3 = _ytDownloader?.GetAlbumsForGame($"{gameName} original soundtrack", cancellationToken, auto)?.ToList()
                        ?? new List<Album>();
                    ytAlbums.AddRange(strategy3);

                    if (ytAlbums.Count > 0)
                    {
                        LogDebug($"[YouTube] Strategy 3 found {ytAlbums.Count} result(s)");
                    }
                    else
                    {
                        // Strategy 4: Try simplified game name (without edition suffixes)
                        // e.g., "Dishonored - Definitive Edition" -> "Dishonored"
                        var simplifiedName = StringHelper.StripGameNameSuffixes(gameName);
                        if (!string.IsNullOrWhiteSpace(simplifiedName) &&
                            !simplifiedName.Equals(gameName, StringComparison.OrdinalIgnoreCase))
                        {
                            LogDebug($"[YouTube] Strategy 4: Searching with simplified name '\"{simplifiedName}\" OST'");
                            var strategy4 = _ytDownloader?.GetAlbumsForGame($"\"{simplifiedName}\" OST", cancellationToken, auto)?.ToList()
                                ?? new List<Album>();
                            ytAlbums.AddRange(strategy4);

                            if (ytAlbums.Count > 0)
                            {
                                LogDebug($"[YouTube] Strategy 4 found {ytAlbums.Count} result(s)");
                            }
                            else
                            {
                                // Strategy 5: Simplified name + soundtrack
                                LogDebug($"[YouTube] Strategy 5: Searching for '\"{simplifiedName}\" soundtrack'");
                                var strategy5 = _ytDownloader?.GetAlbumsForGame($"\"{simplifiedName}\" soundtrack", cancellationToken, auto)?.ToList()
                                    ?? new List<Album>();
                                ytAlbums.AddRange(strategy5);

                                if (ytAlbums.Count > 0)
                                {
                                    LogDebug($"[YouTube] Strategy 5 found {ytAlbums.Count} result(s)");
                                }
                            }
                        }
                    }
                }
            }

            // Only cache if not skipping cache
            if (!skipCache && _cacheService != null)
            {
                _cacheService.CacheSearchResult(gameName, Source.YouTube, ytAlbums);
            }

            LogDebug($"[Source.All] YouTube: Found {ytAlbums.Count} total album(s) for '{gameName}'");

            return ytAlbums;
        }

        /// <summary>
        /// Minimum score required for an album to be considered a valid match.
        /// Simplified: We now rely on strict keyword filtering rather than complex scoring
        /// </summary>
        private const int MinimumAlbumRelevanceScore = 1000;

        public Album BestAlbumPick(IEnumerable<Album> albums, Game game)
        {
            var albumsList = albums?.ToList() ?? new List<Album>();

            if (albumsList.Count == 0)
                return null;

            var gameName = StringHelper.PrepareForSearch(game.Name);

            // First pass: Filter out obvious non-game-music albums (auto-mode only filters)
            var filteredAlbums = albumsList.Where(album => IsLikelyGameMusic(album, game, auto: true)).ToList();
            
            LogDebug($"Album filtering for '{game.Name}': {albumsList.Count} total -> {filteredAlbums.Count} after filtering");
            
            if (filteredAlbums.Count == 0)
            {
                Logger.Warn($"No valid game music albums found for '{game.Name}' after filtering");
                return null;
            }
            
            // Second pass: Score remaining albums
            var scoredAlbums = filteredAlbums.Select(album => new
            {
                Album = album,
                Score = CalculateAlbumRelevance(album, game)
            })
            .OrderByDescending(x => x.Score)
            .ToList();

            LogDebug($"Album candidates for '{game.Name}' (search: '{gameName}'):");
            foreach (var candidate in scoredAlbums.Take(5))
            {
                LogDebug($"  - '{candidate.Album.Name}' (score: {candidate.Score}, source: {candidate.Album.Source})");
            }

            var validAlbums = scoredAlbums.Where(x => x.Score >= MinimumAlbumRelevanceScore).ToList();

            if (validAlbums.Count == 0)
            {
                Logger.Warn($"No albums met minimum relevance threshold ({MinimumAlbumRelevanceScore}) for game '{game.Name}'");
                Logger.Warn($"Best candidate was '{scoredAlbums.FirstOrDefault()?.Album?.Name}' with score {scoredAlbums.FirstOrDefault()?.Score}");
                return null;
            }

            var best = validAlbums.First();
            LogDebug($"Selected album '{best.Album.Name}' with score {best.Score} for game '{game.Name}'");
            
            return best.Album;
        }
        
        /// <summary>
        /// First-pass filter: Quickly eliminate albums that are clearly NOT game music
        /// This prevents scoring non-game content like TV shows, movies, random music
        /// </summary>
        /// <param name="album">The album to check</param>
        /// <param name="game">The game being searched for</param>
        /// <param name="auto">Whether this is auto-mode (applies whitelist and strict filtering) or manual mode</param>
        private bool IsLikelyGameMusic(Album album, Game game, bool auto)
        {
            if (album == null || string.IsNullOrWhiteSpace(album.Name))
                return false;

            var albumName = album.Name.ToLowerInvariant();
            var gameName = game.Name.ToLowerInvariant();

            // Requirement 1: Must contain game-music-related keywords
            var musicKeywords = new[] { "ost", "soundtrack", "original soundtrack", "game music", "bgm", "score", "theme" };
            bool hasMusicKeyword = musicKeywords.Any(keyword => albumName.Contains(keyword));

            if (!hasMusicKeyword)
            {
                LogDebug($"Album '{album.Name}' rejected: No music keywords (OST/soundtrack/etc)");
                return false;
            }

            // Requirement 2: Reject obvious non-game content
            var rejectKeywords = new[]
            {
                "[eng sub]", "[sub]", "episode", "drama", "movie", "film",
                "trailer", "review", "gameplay", "walkthrough", "let's play",
                "reaction", "cover", "remix", "fan made", "fanmade"
            };

            if (rejectKeywords.Any(keyword => albumName.Contains(keyword)))
            {
                LogDebug($"Album '{album.Name}' rejected: Contains non-game keyword");
                return false;
            }

            // Requirement 3: For YouTube in auto-mode, apply stricter word matching
            if (album.Source == Source.YouTube && auto)
            {
                var gameWords = GetSignificantWords(gameName);
                var albumWords = GetSignificantWords(albumName);

                if (gameWords.Count > 0)
                {
                    int matchedWords = gameWords.Count(gw =>
                        albumWords.Any(aw => string.Equals(gw, aw, StringComparison.OrdinalIgnoreCase)));

                    // For YouTube, require at least 50% word match (or 100% for single-word games)
                    double matchPercentage = (double)matchedWords / gameWords.Count;
                    double requiredMatch = gameWords.Count == 1 ? 1.0 : 0.5;

                    if (matchPercentage < requiredMatch)
                    {
                        LogDebug($"[Auto-mode] Album '{album.Name}' rejected: Insufficient word match for YouTube ({matchPercentage:P0} < {requiredMatch:P0})");
                        return false;
                    }
                }
            }

            return true;
        }

        public List<Song> BestSongPick(IEnumerable<Song> songs, string gameName, int maxSongs = 1)
        {
            var songsList = songs?.ToList() ?? new List<Song>();
            
            if (songsList.Count == 0)
                return new List<Song>();
                
            if (songsList.Count == 1)
                return new List<Song> { songsList.First() };
            
            var scoredSongs = songsList.Select(song => new
            {
                Song = song,
                Score = CalculateSongRelevance(song, gameName)
            })
            .OrderByDescending(x => x.Score)
            .ToList();
            
            var topCandidates = scoredSongs.Take(5).ToList();
            LogDebug($"Top song candidates for '{gameName}':");
            foreach (var candidate in topCandidates)
            {
                LogDebug($"  - '{candidate.Song.Name}' (score: {candidate.Score})");
            }
            
            var result = scoredSongs.Take(maxSongs).Select(x => x.Song).ToList();
            
            if (result.Any())
            {
                LogDebug($"Selected song for '{gameName}': '{result[0].Name}' (score: {scoredSongs[0].Score})");
            }
            
            return result;
        }
        
        private int CalculateSongRelevance(Song song, string gameName)
        {
            if (song == null)
                return 0;
                
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

        private IDownloader GetDownloaderForSource(Source source)
        {
            switch (source)
            {
                case Source.KHInsider:
                    return _khDownloader;
                case Source.YouTube:
                    return _ytDownloader;
                case Source.All:
                    // For "All", return KHInsider as default (can be enhanced to try multiple)
                    return _khDownloader;
                default:
                    Logger.Warn($"Unknown source: {source}");
                    return null;
            }
        }

        private int CalculateAlbumRelevance(Album album, Game game)
        {
            if (album == null || game == null)
                return 0;

            // Normalize both names for comparison
            var gameName = NormalizeForMatching(StringHelper.PrepareForSearch(game.Name));
            var albumName = NormalizeForMatching(album.Name);
            
            // Also get individual words for partial matching
            var gameWords = GetSignificantWords(gameName);
            var albumWords = GetSignificantWords(albumName);
            
            var score = 0;

            // === EXACT MATCH (highest priority) ===
            if (string.Equals(albumName, gameName, StringComparison.OrdinalIgnoreCase))
            {
                score += 10000;
            }
            // Album starts with exact game name
            else if (albumName.StartsWith(gameName + " ", StringComparison.OrdinalIgnoreCase) ||
                     albumName.StartsWith(gameName + ":", StringComparison.OrdinalIgnoreCase))
            {
                score += 8000;
            }
            // Game name is contained in album name
            else if (albumName.ContainsIgnoreCase(gameName))
            {
                score += 6000;
            }
            
            // === WORD-BASED MATCHING (simplified) ===
            if (gameWords.Count > 0)
            {
                // Count how many game words appear in album name
                int matchedWords = gameWords.Count(gw => 
                    albumWords.Any(aw => string.Equals(gw, aw, StringComparison.OrdinalIgnoreCase)));
                
                // Calculate match percentage
                double matchPercentage = (double)matchedWords / gameWords.Count;
                
                // All words match
                if (matchPercentage >= 1.0)
                {
                    score += 5000;
                }
                // Most words match (75%+)
                else if (matchPercentage >= 0.75)
                {
                    score += 3000;
                }
                // Half words match
                else if (matchPercentage >= 0.5)
                {
                    score += 1500;
                }
                // Some words match (33%+)
                else if (matchPercentage >= 0.33)
                {
                    score += 500;
                }
            }
            
            // === SOUNDTRACK INDICATORS (bonus) ===
            if (albumName.Contains("original soundtrack"))
                score += 300;
            else if (albumName.Contains("soundtrack") || albumName.Contains("ost"))
                score += 200;
            else if (albumName.Contains("score") || albumName.Contains("music"))
                score += 100;
            
            // === PLATFORM MATCH (bonus) ===
            if (game.Platforms != null && album.Platforms != null && album.Platforms.Any())
            {
                var gamePlatforms = game.Platforms.Select(p => NormalizeForMatching(p.Name)).ToList();
                var albumPlatforms = album.Platforms.Select(NormalizeForMatching).ToList();
                
                bool platformMatch = gamePlatforms.Any(gp => 
                    albumPlatforms.Any(ap => 
                        string.Equals(gp, ap, StringComparison.OrdinalIgnoreCase) ||
                        gp.Contains(ap) || ap.Contains(gp)));
                
                if (platformMatch)
                {
                    score += 200;
                }
            }

            // === TYPE PREFERENCE (bonus) ===
            if (!string.IsNullOrWhiteSpace(album.Type))
            {
                if (string.Equals(album.Type, "GameRip", StringComparison.OrdinalIgnoreCase))
                    score += 150;
                else if (string.Equals(album.Type, "Soundtrack", StringComparison.OrdinalIgnoreCase))
                    score += 100;
            }

            // === YEAR MATCH (bonus) ===
            if (!string.IsNullOrWhiteSpace(album.Year) && game.ReleaseYear.HasValue)
            {
                if (album.Year.Contains(game.ReleaseYear.Value.ToString()))
                    score += 50;
            }

            return score;
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
        
        /// <summary>
        /// Gets significant words from a name (filters out common words)
        /// </summary>
        private List<string> GetSignificantWords(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new List<string>();
            
            // Common words to ignore
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the", "a", "an", "of", "in", "on", "at", "to", "for", "and", "or",
                "vol", "volume", "part", "disc", "cd", "ost", "soundtrack", "original",
                "sound", "music", "game", "video"
            };
            
            var words = name.ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 1 && !stopWords.Contains(w))
                .ToList();
            
            return words;
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

