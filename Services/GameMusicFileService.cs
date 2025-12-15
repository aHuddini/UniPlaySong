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

        // Supported audio file extensions (using Constants for consistency)
        private static readonly string[] SupportedExtensions = Constants.SupportedAudioExtensions;

        public GameMusicFileService(string baseMusicPath, ErrorHandlerService errorHandler = null)
        {
            _baseMusicPath = baseMusicPath ?? throw new ArgumentNullException(nameof(baseMusicPath));
            _errorHandler = errorHandler;
            Directory.CreateDirectory(_baseMusicPath);
        }

        /// <summary>
        /// Gets the music directory path for a game
        /// </summary>
        public string GetGameMusicDirectory(Game game)
        {
            if (game == null)
            {
                return null;
            }

            var gameId = game.Id.ToString();
            return Path.Combine(_baseMusicPath, gameId);
        }

        /// <summary>
        /// Gets all available music files for a game
        /// </summary>
        public List<string> GetAvailableSongs(Game game)
        {
            var directory = GetGameMusicDirectory(game);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return new List<string>();
            }

            if (_errorHandler != null)
            {
                return _errorHandler.Try(
                    () =>
                    {
                        var files = Directory.GetFiles(directory)
                            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .OrderBy(f => f)
                            .ToList();

                        return files;
                    },
                    defaultValue: new List<string>(),
                    context: $"getting songs for game '{game?.Name}'"
                );
            }
            else
            {
                // Fallback to original error handling
                try
                {
                    var files = Directory.GetFiles(directory)
                        .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .OrderBy(f => f)
                        .ToList();

                    return files;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error getting songs for game '{game.Name}': {ex.Message}");
                    return new List<string>();
                }
            }
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

        /// <summary>
        /// Creates the music directory for a game if it doesn't exist
        /// </summary>
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
        /// Checks if a game has any music files
        /// </summary>
        public bool HasMusic(Game game)
        {
            var songs = GetAvailableSongs(game);
            return songs.Count > 0;
        }
    }
}

