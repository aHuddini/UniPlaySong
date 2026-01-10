using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Service for migrating music files between PlayniteSound and UniPlaySong extensions.
    /// Supports bidirectional migration with progress reporting.
    /// </summary>
    public class MigrationService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "Migration";

        // Audio file extensions to migrate (excludes metadata files like .json)
        private static readonly string[] AudioExtensions =
        {
            ".mp3", ".wav", ".flac", ".ogg", ".wma", ".m4a", ".aac", ".mid", ".aif"
        };

        private readonly string _playniteConfigPath;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ErrorHandlerService _errorHandler;

        // Path constants
        private const string ExtraMetadataFolder = "ExtraMetadata";
        private const string GamesFolder = "Games";
        private const string UniPlaySongFolder = "UniPlaySong";
        private const string PlayniteSoundMusicFolder = "Music Files";

        public MigrationService(IPlayniteAPI playniteApi, ErrorHandlerService errorHandler = null)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _playniteConfigPath = playniteApi.Paths.ConfigurationPath;
            _errorHandler = errorHandler;
        }

        /// <summary>
        /// Gets the PlayniteSound games base path: ExtraMetadata\Games\
        /// </summary>
        private string PlayniteSoundGamesPath =>
            Path.Combine(_playniteConfigPath, ExtraMetadataFolder, GamesFolder);

        /// <summary>
        /// Gets the UniPlaySong games base path: ExtraMetadata\UniPlaySong\Games\
        /// </summary>
        private string UniPlaySongGamesPath =>
            Path.Combine(_playniteConfigPath, ExtraMetadataFolder, UniPlaySongFolder, GamesFolder);

        /// <summary>
        /// Checks if a file has an audio extension
        /// </summary>
        private bool IsAudioFile(string filePath)
        {
            var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            return !string.IsNullOrEmpty(extension) && AudioExtensions.Contains(extension);
        }

        /// <summary>
        /// Scans PlayniteSound directory for game IDs that have music files
        /// </summary>
        public List<MigrationGameInfo> ScanPlayniteSoundGames()
        {
            var games = new List<MigrationGameInfo>();

            if (!Directory.Exists(PlayniteSoundGamesPath))
            {
                Logger.DebugIf(LogPrefix,$"PlayniteSound games path does not exist: {PlayniteSoundGamesPath}");
                return games;
            }

            try
            {
                foreach (var gameIdFolderPath in Directory.GetDirectories(PlayniteSoundGamesPath))
                {
                    var gameId = Path.GetFileName(gameIdFolderPath);
                    var musicFilesPath = Path.Combine(gameIdFolderPath, PlayniteSoundMusicFolder);

                    if (!Directory.Exists(musicFilesPath))
                        continue;

                    var audioFiles = Directory.GetFiles(musicFilesPath)
                        .Where(IsAudioFile)
                        .ToList();

                    if (audioFiles.Count > 0)
                    {
                        // Try to get game name from Playnite database
                        var gameName = GetGameNameById(gameId);

                        games.Add(new MigrationGameInfo
                        {
                            GameId = gameId,
                            GameName = gameName,
                            FileCount = audioFiles.Count,
                            SourcePath = musicFilesPath
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error scanning PlayniteSound games");
            }

            return games;
        }

        /// <summary>
        /// Scans UniPlaySong directory for game IDs that have music files
        /// </summary>
        public List<MigrationGameInfo> ScanUniPlaySongGames()
        {
            var games = new List<MigrationGameInfo>();

            if (!Directory.Exists(UniPlaySongGamesPath))
            {
                Logger.DebugIf(LogPrefix,$"UniPlaySong games path does not exist: {UniPlaySongGamesPath}");
                return games;
            }

            try
            {
                foreach (var gameIdFolderPath in Directory.GetDirectories(UniPlaySongGamesPath))
                {
                    var gameId = Path.GetFileName(gameIdFolderPath);

                    var audioFiles = Directory.GetFiles(gameIdFolderPath)
                        .Where(IsAudioFile)
                        .ToList();

                    if (audioFiles.Count > 0)
                    {
                        var gameName = GetGameNameById(gameId);

                        games.Add(new MigrationGameInfo
                        {
                            GameId = gameId,
                            GameName = gameName,
                            FileCount = audioFiles.Count,
                            SourcePath = gameIdFolderPath
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error scanning UniPlaySong games");
            }

            return games;
        }

        /// <summary>
        /// Attempts to get the game name from the Playnite database by ID
        /// </summary>
        private string GetGameNameById(string gameIdStr)
        {
            try
            {
                if (Guid.TryParse(gameIdStr, out var gameId))
                {
                    var game = _playniteApi.Database.Games.Get(gameId);
                    if (game != null)
                    {
                        return game.Name;
                    }
                }
            }
            catch
            {
                // Silently fail - game may have been deleted
            }

            return null; // Unknown game
        }

        /// <summary>
        /// Migrates music files from PlayniteSound to UniPlaySong for a single game
        /// </summary>
        public MigrationResult MigrateFromPlayniteSound(string gameId, bool overwrite = false)
        {
            var sourcePath = Path.Combine(PlayniteSoundGamesPath, gameId, PlayniteSoundMusicFolder);
            var destPath = Path.Combine(UniPlaySongGamesPath, gameId);

            return MigrateFiles(gameId, sourcePath, destPath, overwrite);
        }

        /// <summary>
        /// Migrates music files from UniPlaySong to PlayniteSound for a single game
        /// </summary>
        public MigrationResult MigrateToPlayniteSound(string gameId, bool overwrite = false)
        {
            var sourcePath = Path.Combine(UniPlaySongGamesPath, gameId);
            var destPath = Path.Combine(PlayniteSoundGamesPath, gameId, PlayniteSoundMusicFolder);

            return MigrateFiles(gameId, sourcePath, destPath, overwrite);
        }

        /// <summary>
        /// Core file migration logic
        /// </summary>
        private MigrationResult MigrateFiles(string gameId, string sourcePath, string destPath, bool overwrite)
        {
            var result = new MigrationResult
            {
                GameId = gameId,
                GameName = GetGameNameById(gameId)
            };

            if (!Directory.Exists(sourcePath))
            {
                result.Success = false;
                result.Error = "Source directory not found";
                return result;
            }

            try
            {
                Directory.CreateDirectory(destPath);

                var audioFiles = Directory.GetFiles(sourcePath).Where(IsAudioFile).ToList();

                foreach (var file in audioFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var destFile = Path.Combine(destPath, fileName);

                    try
                    {
                        if (File.Exists(destFile))
                        {
                            if (overwrite)
                            {
                                File.Copy(file, destFile, overwrite: true);
                                result.FilesOverwritten++;
                            }
                            else
                            {
                                result.FilesSkipped++;
                            }
                        }
                        else
                        {
                            File.Copy(file, destFile);
                            result.FilesCopied++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to copy file: {fileName}");
                        result.FilesFailed++;
                    }
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Migration failed for game: {gameId}");
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Migrates all games from PlayniteSound to UniPlaySong
        /// </summary>
        public async Task<MigrationBatchResult> MigrateAllFromPlayniteSoundAsync(
            IProgress<MigrationProgress> progress = null,
            CancellationToken cancellationToken = default,
            bool overwrite = false)
        {
            var games = ScanPlayniteSoundGames();
            return await MigrateBatchAsync(games, MigrateFromPlayniteSound, progress, cancellationToken, overwrite);
        }

        /// <summary>
        /// Migrates all games from UniPlaySong to PlayniteSound
        /// </summary>
        public async Task<MigrationBatchResult> MigrateAllToPlayniteSoundAsync(
            IProgress<MigrationProgress> progress = null,
            CancellationToken cancellationToken = default,
            bool overwrite = false)
        {
            var games = ScanUniPlaySongGames();
            return await MigrateBatchAsync(games, MigrateToPlayniteSound, progress, cancellationToken, overwrite);
        }

        /// <summary>
        /// Core batch migration logic with async progress reporting
        /// </summary>
        private async Task<MigrationBatchResult> MigrateBatchAsync(
            List<MigrationGameInfo> games,
            Func<string, bool, MigrationResult> migrateFunc,
            IProgress<MigrationProgress> progress,
            CancellationToken cancellationToken,
            bool overwrite)
        {
            var batchResult = new MigrationBatchResult
            {
                TotalGames = games.Count
            };

            for (int i = 0; i < games.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    batchResult.WasCancelled = true;
                    break;
                }

                var game = games[i];

                progress?.Report(new MigrationProgress
                {
                    CurrentIndex = i + 1,
                    TotalCount = games.Count,
                    CurrentGameId = game.GameId,
                    CurrentGameName = game.GameName ?? game.GameId,
                    Status = $"Migrating {game.GameName ?? game.GameId}..."
                });

                // Run file copy on background thread
                var result = await Task.Run(() => migrateFunc(game.GameId, overwrite), cancellationToken);
                batchResult.Results.Add(result);

                if (result.Success)
                {
                    batchResult.SuccessfulGames++;
                    batchResult.TotalFilesCopied += result.FilesCopied;
                    batchResult.TotalFilesSkipped += result.FilesSkipped;
                }
                else
                {
                    batchResult.FailedGames++;
                }

                // Small delay to allow UI updates
                await Task.Delay(10, cancellationToken);
            }

            return batchResult;
        }

        /// <summary>
        /// Gets summary statistics for both directions
        /// </summary>
        public MigrationSummary GetMigrationSummary()
        {
            var playniteSoundGames = ScanPlayniteSoundGames();
            var uniPlaySongGames = ScanUniPlaySongGames();

            return new MigrationSummary
            {
                PlayniteSoundGameCount = playniteSoundGames.Count,
                PlayniteSoundFileCount = playniteSoundGames.Sum(g => g.FileCount),
                UniPlaySongGameCount = uniPlaySongGames.Count,
                UniPlaySongFileCount = uniPlaySongGames.Sum(g => g.FileCount)
            };
        }

        /// <summary>
        /// Deletes all PlayniteSound music files (Music Files folders within game directories)
        /// </summary>
        /// <param name="progress">Progress reporter for UI updates</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result containing counts of deleted files and folders</returns>
        public async Task<PlayniteSoundDeleteResult> DeletePlayniteSoundMusicAsync(
            IProgress<MigrationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new PlayniteSoundDeleteResult();
            var games = ScanPlayniteSoundGames();
            result.TotalGames = games.Count;

            for (int i = 0; i < games.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    break;
                }

                var game = games[i];

                progress?.Report(new MigrationProgress
                {
                    CurrentIndex = i + 1,
                    TotalCount = games.Count,
                    CurrentGameId = game.GameId,
                    CurrentGameName = game.GameName ?? game.GameId,
                    Status = $"Deleting PlayniteSound music: {game.GameName ?? game.GameId}..."
                });

                try
                {
                    var musicFolderPath = game.SourcePath; // This is the "Music Files" folder

                    if (Directory.Exists(musicFolderPath))
                    {
                        // Count and delete audio files
                        var audioFiles = Directory.GetFiles(musicFolderPath).Where(IsAudioFile).ToList();
                        foreach (var file in audioFiles)
                        {
                            try
                            {
                                File.Delete(file);
                                result.FilesDeleted++;
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to delete file: {file} - {ex.Message}");
                                result.FilesFailed++;
                            }
                        }

                        // Try to delete the Music Files folder if empty
                        try
                        {
                            if (Directory.GetFiles(musicFolderPath).Length == 0 &&
                                Directory.GetDirectories(musicFolderPath).Length == 0)
                            {
                                Directory.Delete(musicFolderPath);
                                result.FoldersDeleted++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to delete folder: {musicFolderPath} - {ex.Message}");
                        }

                        // Try to delete parent game folder if now empty (no other ExtraMetadata content)
                        var gameFolder = Path.GetDirectoryName(musicFolderPath);
                        try
                        {
                            if (Directory.Exists(gameFolder) &&
                                Directory.GetFiles(gameFolder).Length == 0 &&
                                Directory.GetDirectories(gameFolder).Length == 0)
                            {
                                Directory.Delete(gameFolder);
                                result.FoldersDeleted++;
                            }
                        }
                        catch
                        {
                            // Ignore - folder may contain other metadata
                        }

                        result.GamesProcessed++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to delete PlayniteSound music for game: {game.GameId}");
                    result.GamesFailed++;
                }

                // Small delay to allow UI updates
                await Task.Delay(10, cancellationToken);
            }

            Logger.Info($"DeletePlayniteSoundMusic: Deleted {result.FilesDeleted} files from {result.GamesProcessed} games");
            return result;
        }
    }

    /// <summary>
    /// Result of deleting PlayniteSound music files
    /// </summary>
    public class PlayniteSoundDeleteResult
    {
        public int TotalGames { get; set; }
        public int GamesProcessed { get; set; }
        public int GamesFailed { get; set; }
        public int FilesDeleted { get; set; }
        public int FilesFailed { get; set; }
        public int FoldersDeleted { get; set; }
        public bool WasCancelled { get; set; }
    }

    #region Migration Models

    /// <summary>
    /// Information about a game with music files
    /// </summary>
    public class MigrationGameInfo
    {
        public string GameId { get; set; }
        public string GameName { get; set; }
        public int FileCount { get; set; }
        public string SourcePath { get; set; }

        public string DisplayName => GameName ?? $"Unknown ({GameId.Substring(0, 8)}...)";
    }

    /// <summary>
    /// Result of migrating a single game
    /// </summary>
    public class MigrationResult
    {
        public bool Success { get; set; }
        public string GameId { get; set; }
        public string GameName { get; set; }
        public int FilesCopied { get; set; }
        public int FilesSkipped { get; set; }
        public int FilesOverwritten { get; set; }
        public int FilesFailed { get; set; }
        public string Error { get; set; }

        public int TotalFilesProcessed => FilesCopied + FilesSkipped + FilesOverwritten + FilesFailed;
    }

    /// <summary>
    /// Result of batch migration
    /// </summary>
    public class MigrationBatchResult
    {
        public int TotalGames { get; set; }
        public int SuccessfulGames { get; set; }
        public int FailedGames { get; set; }
        public int TotalFilesCopied { get; set; }
        public int TotalFilesSkipped { get; set; }
        public bool WasCancelled { get; set; }
        public List<MigrationResult> Results { get; set; } = new List<MigrationResult>();
    }

    /// <summary>
    /// Progress update for migration operations
    /// </summary>
    public class MigrationProgress
    {
        public int CurrentIndex { get; set; }
        public int TotalCount { get; set; }
        public string CurrentGameId { get; set; }
        public string CurrentGameName { get; set; }
        public string Status { get; set; }

        public double ProgressPercentage => TotalCount > 0 ? (CurrentIndex * 100.0 / TotalCount) : 0;
    }

    /// <summary>
    /// Summary of available migrations
    /// </summary>
    public class MigrationSummary
    {
        public int PlayniteSoundGameCount { get; set; }
        public int PlayniteSoundFileCount { get; set; }
        public int UniPlaySongGameCount { get; set; }
        public int UniPlaySongFileCount { get; set; }
    }

    #endregion
}
