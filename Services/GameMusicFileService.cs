using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Service for managing game music files and directories
    /// </summary>
    public class GameMusicFileService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly string _baseMusicPath;
        private readonly ErrorHandlerService _errorHandler;
        private readonly Func<UniPlaySongSettings> _settingsProvider;
        private static readonly string[] SupportedExtensions = Constants.SupportedAudioExtensions;

        // Song list cache to avoid repeated directory scans
        private readonly Dictionary<string, List<string>> _songCache = new Dictionary<string, List<string>>();

        public GameMusicFileService(string baseMusicPath, ErrorHandlerService errorHandler = null, Func<UniPlaySongSettings> settingsProvider = null)
        {
            _baseMusicPath = baseMusicPath ?? throw new ArgumentNullException(nameof(baseMusicPath));
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

        /// <summary>
        /// Gets the primary song for a game
        /// </summary>
        public string GetPrimarySong(Game game)
        {
            var directory = GetGameMusicDirectory(game);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            return PrimarySongManager.GetPrimarySong(directory, _errorHandler);
        }

        /// <summary>
        /// Sets the primary song for a game
        /// </summary>
        public void SetPrimarySong(Game game, string songFilePath)
        {
            var directory = GetGameMusicDirectory(game);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            PrimarySongManager.SetPrimarySong(directory, songFilePath, _errorHandler);
        }

        /// <summary>
        /// Clears the primary song for a game
        /// </summary>
        public void ClearPrimarySong(Game game)
        {
            var directory = GetGameMusicDirectory(game);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            PrimarySongManager.ClearPrimarySong(directory, _errorHandler);
        }

        /// <summary>
        /// Removes the primary song for a game (alias for ClearPrimarySong)
        /// </summary>
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
            }
            return directory;
        }

        /// <summary>
        /// Invalidates the song cache for a specific game
        /// </summary>
        public void InvalidateCacheForGame(Game game)
        {
            var directory = GetGameMusicDirectory(game);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _songCache.Remove(directory);
            }
        }

        /// <summary>
        /// Invalidates the song cache for a specific directory
        /// </summary>
        public void InvalidateCacheForDirectory(string directory)
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _songCache.Remove(directory);
            }
        }

        /// <summary>
        /// Checks if a game has any music files
        /// </summary>
        public bool HasMusic(Game game)
        {
            var songs = GetAvailableSongs(game);
            return songs.Count > 0;
        }

        /// <summary>
        /// Deletes all music files for a game
        /// </summary>
        /// <returns>Number of files deleted</returns>
        public int DeleteAllMusic(Game game)
        {
            var directory = GetGameMusicDirectory(game);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return 0;
            }

            var deletedCount = 0;

            if (_errorHandler != null)
            {
                return _errorHandler.Try(
                    () =>
                    {
                        var files = Directory.GetFiles(directory)
                            .Where(f => Constants.SupportedAudioExtensionsLowercase.Contains(Path.GetExtension(f)))
                            .ToList();

                        foreach (var file in files)
                        {
                            try
                            {
                                File.Delete(file);
                                deletedCount++;
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to delete '{file}': {ex.Message}");
                            }
                        }

                        // Also clear primary song metadata
                        PrimarySongManager.ClearPrimarySong(directory, _errorHandler);

                        return deletedCount;
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

                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to delete '{file}': {ex.Message}");
                        }
                    }

                    // Also clear primary song metadata
                    PrimarySongManager.ClearPrimarySong(directory, null);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error deleting music for game '{game?.Name}': {ex.Message}");
                }
            }

            // Invalidate cache since we deleted files
            InvalidateCacheForGame(game);

            return deletedCount;
        }
    }
}

