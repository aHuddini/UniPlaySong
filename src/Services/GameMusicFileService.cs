using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Services
{
    // Service for managing game music files and directories
    public class GameMusicFileService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly string _baseMusicPath;
        private readonly string _emlGamesPath;
        private readonly ErrorHandlerService _errorHandler;
        private readonly Func<UniPlaySongSettings> _settingsProvider;
        private static readonly string[] SupportedExtensions = Constants.SupportedAudioExtensions;

        // Song list cache to avoid repeated directory scans
        private readonly Dictionary<string, List<string>> _songCache = new Dictionary<string, List<string>>();

        // emlGamesPath: ExtraMetadataLoader's games root (<Config>\ExtraMetadata\Games),
        // supplied by the caller from the SDK ConfigurationPath so trailer detection works
        // on portable installs. Optional — when null, HasTrailerVideo() returns false.
        public GameMusicFileService(string baseMusicPath, ErrorHandlerService errorHandler = null, Func<UniPlaySongSettings> settingsProvider = null, string emlGamesPath = null)
        {
            _baseMusicPath = baseMusicPath ?? throw new ArgumentNullException(nameof(baseMusicPath));
            _emlGamesPath = emlGamesPath;
            _errorHandler = errorHandler;
            _settingsProvider = settingsProvider;
            Directory.CreateDirectory(_baseMusicPath);
        }

        public string GetGameMusicDirectory(Game game)
        {
            if (game == null)
            {
                return null;
            }

            var gameId = game.Id.ToString();
            return Path.Combine(_baseMusicPath, gameId);
        }

        // Returns true if ExtraMetadataLoader has a trailer video for this game.
        // EML stores trailers at <Config>\ExtraMetadata\Games\{GameId}\VideoTrailer.mp4
        // (or VideoMicrotrailer.mp4). _emlGamesPath is the <Config>\ExtraMetadata\Games
        // root, supplied by the caller from the SDK ConfigurationPath so this resolves
        // correctly on portable installs.
        public bool HasTrailerVideo(Game game)
        {
            if (game == null || string.IsNullOrEmpty(_emlGamesPath))
            {
                return false;
            }

            try
            {
                var emlGameDir = Path.Combine(_emlGamesPath, game.Id.ToString());
                return File.Exists(Path.Combine(emlGameDir, Constants.VideoTrailerFileName))
                    || File.Exists(Path.Combine(emlGameDir, Constants.VideoMicrotrailerFileName));
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(ex, "HasTrailerVideo");
                return false;
            }
        }

        public List<string> GetAvailableSongs(Game game)
        {
            var directory = GetGameMusicDirectory(game);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return new List<string>();
            }

            // Check if caching is enabled (default: false/disabled if settings not available)
            bool cacheEnabled = _settingsProvider?.Invoke()?.EnableSongListCache ?? false;

            // Clear cache when user disables the setting
            if (!cacheEnabled && _songCache.Count > 0)
            {
                _songCache.Clear();
            }

            // Check cache first (if enabled)
            if (cacheEnabled && _songCache.TryGetValue(directory, out var cached))
            {
                return new List<string>(cached); // Return copy to prevent external modification
            }

            // Cache miss or caching disabled - scan directory
            List<string> files;
            if (_errorHandler != null)
            {
                files = _errorHandler.Try(
                    () =>
                    {
                        return Directory.GetFiles(directory)
                            .Where(f => Constants.SupportedAudioExtensionsLowercase.Contains(Path.GetExtension(f)))
                            .OrderBy(f => f)
                            .ToList();
                    },
                    defaultValue: new List<string>(),
                    context: $"getting songs for game '{game?.Name}'"
                );
            }
            else
            {
                try
                {
                    files = Directory.GetFiles(directory)
                        .Where(f => Constants.SupportedAudioExtensionsLowercase.Contains(Path.GetExtension(f)))
                        .OrderBy(f => f)
                        .ToList();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error getting songs for game '{game.Name}': {ex.Message}");
                    return new List<string>();
                }
            }

            // Cache the result (only if caching is enabled)
            if (cacheEnabled)
            {
                _songCache[directory] = files;
            }
            return new List<string>(files);
        }

        // Returns total size in bytes of supported audio files in the game's music directory
        public long GetMusicFolderSize(Game game)
        {
            var directory = GetGameMusicDirectory(game);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return 0;

            try
            {
                return Directory.GetFiles(directory)
                    .Where(f => Constants.SupportedAudioExtensionsLowercase.Contains(Path.GetExtension(f)))
                    .Sum(f => new FileInfo(f).Length);
            }
            catch
            {
                return 0;
            }
        }

        public string GetPrimarySong(Game game)
        {
            var directory = GetGameMusicDirectory(game);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            return PrimarySongManager.GetPrimarySong(directory, _errorHandler);
        }

        public void SetPrimarySong(Game game, string songFilePath)
        {
            var directory = GetGameMusicDirectory(game);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            PrimarySongManager.SetPrimarySong(directory, songFilePath, _errorHandler);
        }

        public void ClearPrimarySong(Game game)
        {
            var directory = GetGameMusicDirectory(game);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            PrimarySongManager.ClearPrimarySong(directory, _errorHandler);
        }

        // Alias for ClearPrimarySong (kept for backward compatibility with menu handlers)
        public void RemovePrimarySong(Game game)
        {
            ClearPrimarySong(game);
        }

        public string EnsureGameMusicDirectory(Game game)
        {
            var directory = GetGameMusicDirectory(game);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                WriteBreadcrumb(game, directory);
            }
            return directory;
        }

        public void WriteBreadcrumb(Game game, string directory)
        {
            if (game == null || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            var safeName = SanitizeFileName(game.Name ?? "Unknown");
            var breadcrumbPath = Path.Combine(directory, safeName + ".txt");

            // Skip if any breadcrumb already exists
            if (Directory.GetFiles(directory, "*.txt").Length > 0)
                return;

            File.WriteAllText(breadcrumbPath,
                $"Game: {game.Name ?? "Unknown"}\r\nID: {game.Id}\r\n");
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).Trim();
            if (sanitized.Length > 100)
                sanitized = sanitized.Substring(0, 100).TrimEnd();
            return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
        }

        public void InvalidateCacheForGame(Game game)
        {
            var directory = GetGameMusicDirectory(game);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _songCache.Remove(directory);
            }
        }

        public void InvalidateCacheForDirectory(string directory)
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _songCache.Remove(directory);
            }
        }

        public bool HasMusic(Game game)
        {
            var songs = GetAvailableSongs(game);
            return songs.Count > 0;
        }

        /// <summary>
        /// Deletes all music files for a game using parallel I/O
        /// </summary>
        /// <returns>Number of files deleted</returns>
        public int DeleteAllMusic(Game game)
        {
            var directory = GetGameMusicDirectory(game);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return 0;
            }

            if (_errorHandler != null)
            {
                return _errorHandler.Try(
                    () =>
                    {
                        var files = Directory.GetFiles(directory)
                            .Where(f => Constants.SupportedAudioExtensionsLowercase.Contains(Path.GetExtension(f)))
                            .ToList();

                        var deleted = DeleteFilesParallel(files);

                        PrimarySongManager.ClearPrimarySong(directory, _errorHandler);
                        InvalidateCacheForGame(game);
                        CleanupEmptyDirectory(directory);

                        return deleted;
                    },
                    defaultValue: 0,
                    context: $"deleting all music for game '{game?.Name}'"
                );
            }
            else
            {
                try
                {
                    var files = Directory.GetFiles(directory)
                        .Where(f => Constants.SupportedAudioExtensionsLowercase.Contains(Path.GetExtension(f)))
                        .ToList();

                    var deleted = DeleteFilesParallel(files);

                    PrimarySongManager.ClearPrimarySong(directory, null);
                    InvalidateCacheForGame(game);
                    CleanupEmptyDirectory(directory);

                    return deleted;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error deleting music for game '{game?.Name}': {ex.Message}");
                    return 0;
                }
            }
        }

        // Deletes files in parallel and returns the count of successfully deleted files
        private int DeleteFilesParallel(List<string> files)
        {
            if (files == null || files.Count == 0) return 0;

            int deleted = 0;
            Parallel.ForEach(files, file =>
            {
                try
                {
                    File.Delete(file);
                    Interlocked.Increment(ref deleted);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to delete '{file}': {ex.Message}");
                }
            });
            return deleted;
        }

        // Removes a game music directory if it contains no more audio files
        public void CleanupEmptyDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;

            bool hasAudioFiles = Directory.EnumerateFiles(directory)
                .Any(f => Constants.SupportedAudioExtensionsLowercase.Contains(Path.GetExtension(f)));

            if (!hasAudioFiles)
            {
                try
                {
                    Directory.Delete(directory, true);
                    Logger.Debug($"Cleaned up empty music directory: {Path.GetFileName(directory)}");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to clean up directory '{directory}': {ex.Message}");
                }
            }
        }
    }
}

