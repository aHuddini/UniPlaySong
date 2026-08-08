using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Storage-side maintenance for the music library: measuring what is on disk, finding orphaned and oversized
    // files, and deleting them. This was the bulk of UniPlaySong.cs's "Cleanup Operations" region, where it reached
    // the plugin class for nothing but an API handle, a logger and the music path.
    //
    // Three neighbours in that region deliberately stayed behind, because they are not storage work and do depend
    // on plugin state: settings reset/import (live settings object plus SavePluginSettings), the snapshot export
    // (needs IsFullscreen), and FactoryReset (spans both concerns and clears the search cache).
    //
    // Every method reports rather than throws — each is wired to a settings-dialog button, so a
    // failure has to come back as a result the UI can display.
    public class LibraryMaintenanceService
    {
        private readonly IPlayniteAPI _api;
        private readonly FileLogger _fileLogger;
        private readonly string _gamesPath;
        // Resolved per call, not captured: the plugin rebuilds the file service when the music
        // path changes and replaces the playback service on every audio-backend swap.
        private readonly Func<GameMusicFileService> _fileService;
        private readonly Func<IMusicPlaybackService> _playbackService;
        private readonly Func<ITrailerAudioService> _trailerAudioService;

        public LibraryMaintenanceService(
            IPlayniteAPI api,
            FileLogger fileLogger,
            string gamesPath,
            Func<GameMusicFileService> fileService,
            Func<IMusicPlaybackService> playbackService,
            Func<ITrailerAudioService> trailerAudioService)
        {
            _api = api;
            _fileLogger = fileLogger;
            _gamesPath = gamesPath;
            _fileService = fileService;
            _playbackService = playbackService;
            _trailerAudioService = trailerAudioService;
        }

        // Gets storage information for the cleanup UI.
        public (int gameCount, int fileCount, long totalBytes, int preservedCount, long preservedBytes) GetStorageInfo()
        {
            try
            {
                var basePath = Path.Combine(_api.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName);
                var gamesPath = Path.Combine(basePath, Constants.GamesFolderName);
                var preservedPath = Path.Combine(basePath, "PreservedOriginals");

                int gameCount = 0;
                int fileCount = 0;
                long totalBytes = 0;
                int preservedCount = 0;
                long preservedBytes = 0;

                // Count game folders and music files
                if (Directory.Exists(gamesPath))
                {
                    var gameDirs = Directory.GetDirectories(gamesPath);
                    gameCount = gameDirs.Length;

                    foreach (var gameDir in gameDirs)
                    {
                        var files = Directory.GetFiles(gameDir, "*.*", SearchOption.AllDirectories)
                            .Where(f => Constants.SupportedAudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                        foreach (var file in files)
                        {
                            fileCount++;
                            try { totalBytes += new FileInfo(file).Length; } catch { }
                        }
                    }
                }

                // Count preserved originals
                if (Directory.Exists(preservedPath))
                {
                    var preservedFiles = Directory.GetFiles(preservedPath, "*.*", SearchOption.AllDirectories);
                    preservedCount = preservedFiles.Length;
                    foreach (var file in preservedFiles)
                    {
                        try { preservedBytes += new FileInfo(file).Length; } catch { }
                    }
                }

                return (gameCount, fileCount, totalBytes, preservedCount, preservedBytes);
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"GetStorageInfo: Error - {ex.Message}", ex);
                return (0, 0, 0, 0, 0);
            }
        }

        // Deletes all music files and game folders.
        public (int deletedFiles, int deletedFolders, bool success) DeleteAllMusic()
        {
            try
            {
                // Stop any playing music first
                _playbackService()?.Stop();

                var basePath = Path.Combine(_api.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName);
                var gamesPath = Path.Combine(basePath, Constants.GamesFolderName);
                var preservedPath = Path.Combine(basePath, "PreservedOriginals");

                int deletedFiles = 0;
                int deletedFolders = 0;

                // Delete all game music folders
                if (Directory.Exists(gamesPath))
                {
                    var gameDirs = Directory.GetDirectories(gamesPath);
                    foreach (var gameDir in gameDirs)
                    {
                        try
                        {
                            var files = Directory.GetFiles(gameDir, "*.*", SearchOption.AllDirectories);
                            deletedFiles += files.Length;
                            Directory.Delete(gameDir, true);
                            deletedFolders++;
                        }
                        catch (Exception ex)
                        {
                            _fileLogger?.Warn($"DeleteAllMusic: Failed to delete '{gameDir}' - {ex.Message}");
                        }
                    }
                }

                // Delete preserved originals
                if (Directory.Exists(preservedPath))
                {
                    try
                    {
                        var files = Directory.GetFiles(preservedPath, "*.*", SearchOption.AllDirectories);
                        deletedFiles += files.Length;
                        Directory.Delete(preservedPath, true);
                        _fileLogger?.Debug("DeleteAllMusic: Deleted PreservedOriginals folder");
                    }
                    catch (Exception ex)
                    {
                        _fileLogger?.Warn($"DeleteAllMusic: Failed to delete PreservedOriginals - {ex.Message}");
                    }
                }

                _fileLogger?.Debug($"DeleteAllMusic: Deleted {deletedFiles} files in {deletedFolders} folders");
                return (deletedFiles, deletedFolders, true);
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DeleteAllMusic: Error - {ex.Message}", ex);
                return (0, 0, false);
            }
        }

        // Clears all extracted trailer-audio cache files. Delegates to the service so the
        // deletion logic lives in one place. Returns (filesDeleted, bytesFreed); (0,0) if the
        // service was never constructed (feature unused this session).
        public (int filesDeleted, long bytesFreed) ClearTrailerAudioCache()
        {
            return _trailerAudioService()?.ClearCache() ?? (0, 0);
        }

        /// <summary>
        /// Scans the Games music folder for orphaned directories (music for games no longer in the library)
        /// and deletes them.
        /// </summary>
        /// <returns>Tuple of (deletedFolders, deletedFiles, success)</returns>
        public (int deletedFolders, int deletedFiles, bool success) CleanupOrphanedMusic()
        {
            try
            {
                if (!Directory.Exists(_gamesPath))
                {
                    _fileLogger?.Debug("CleanupOrphanedMusic: Games path does not exist, nothing to clean");
                    return (0, 0, true);
                }

                var gameDirs = Directory.GetDirectories(_gamesPath);
                int deletedFolders = 0;
                int deletedFiles = 0;

                foreach (var gameDir in gameDirs)
                {
                    try
                    {
                        var dirName = Path.GetFileName(gameDir);

                        if (!Guid.TryParse(dirName, out var gameId))
                        {
                            _fileLogger?.Debug($"CleanupOrphanedMusic: Skipping non-GUID directory '{dirName}'");
                            continue;
                        }

                        // Check if this game still exists in the database
                        var game = _api.Database.Games[gameId];
                        if (game != null)
                        {
                            continue;
                        }

                        // Game no longer exists — this is an orphaned music folder
                        // Stop playback if this orphaned folder is somehow playing
                        if (_playbackService()?.CurrentGame?.Id == gameId)
                        {
                            _playbackService().Stop();
                        }

                        var fileCount = Directory.GetFiles(gameDir, "*.*", SearchOption.AllDirectories).Length;
                        Directory.Delete(gameDir, true);
                        deletedFiles += fileCount;
                        deletedFolders++;
                        _fileLogger?.Debug($"CleanupOrphanedMusic: Deleted orphaned music directory '{dirName}' ({fileCount} files)");
                    }
                    catch (Exception ex)
                    {
                        _fileLogger?.Warn($"CleanupOrphanedMusic: Failed to delete '{gameDir}' - {ex.Message}");
                    }
                }

                _fileLogger?.Debug($"CleanupOrphanedMusic: Completed - Deleted {deletedFolders} orphaned folders ({deletedFiles} files)");
                return (deletedFolders, deletedFiles, true);
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"CleanupOrphanedMusic: Error - {ex.Message}", ex);
                return (0, 0, false);
            }
        }

        /// <summary>
        /// Counts orphaned music directories (music for games no longer in the library).
        /// </summary>
        /// <returns>Number of orphaned game music directories</returns>
        public int CountOrphanedMusicFolders()
        {
            try
            {
                if (!Directory.Exists(_gamesPath))
                {
                    return 0;
                }

                int count = 0;
                var gameDirs = Directory.GetDirectories(_gamesPath);

                foreach (var gameDir in gameDirs)
                {
                    var dirName = Path.GetFileName(gameDir);
                    if (!Guid.TryParse(dirName, out var gameId))
                    {
                        continue;
                    }

                    if (_api.Database.Games[gameId] == null)
                    {
                        count++;
                    }
                }

                return count;
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"CountOrphanedMusicFolders: Error - {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Scans all music files and returns info about songs longer than the specified duration.
        /// </summary>
        /// <param name="maxMinutes">Maximum allowed duration in minutes</param>
        /// <param name="progressArgs">Optional progress args for UI updates</param>
        /// <returns>List of (filePath, duration, fileSize, gameFolder) for songs exceeding the limit</returns>
        public List<(string filePath, TimeSpan duration, long fileSize, string gameFolder)> GetLongSongs(int maxMinutes, GlobalProgressActionArgs progressArgs = null)
        {
            var longSongs = new List<(string filePath, TimeSpan duration, long fileSize, string gameFolder)>();
            var maxDuration = TimeSpan.FromMinutes(maxMinutes);

            try
            {
                var basePath = Path.Combine(_api.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName);
                var gamesPath = Path.Combine(basePath, Constants.GamesFolderName);

                if (!Directory.Exists(gamesPath))
                    return longSongs;

                // First collect all audio files
                var allFiles = new List<(string path, string gameFolder)>();
                var gameDirs = Directory.GetDirectories(gamesPath);
                foreach (var gameDir in gameDirs)
                {
                    var gameFolder = Path.GetFileName(gameDir);
                    var files = Directory.GetFiles(gameDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => Constants.SupportedAudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                    foreach (var file in files)
                    {
                        allFiles.Add((file, gameFolder));
                    }
                }

                if (progressArgs != null)
                {
                    progressArgs.ProgressMaxValue = allFiles.Count;
                }

                int processed = 0;
                foreach (var (file, gameFolder) in allFiles)
                {
                    if (progressArgs?.CancelToken.IsCancellationRequested == true)
                        break;

                    processed++;
                    if (progressArgs != null)
                    {
                        progressArgs.CurrentProgressValue = processed;
                        progressArgs.Text = $"Scanning ({processed}/{allFiles.Count}): {Path.GetFileName(file)}";
                    }

                    try
                    {
                        using (var reader = new NAudio.Wave.AudioFileReader(file))
                        {
                            if (reader.TotalTime > maxDuration)
                            {
                                var fileSize = new FileInfo(file).Length;
                                longSongs.Add((file, reader.TotalTime, fileSize, gameFolder));
                                _fileLogger?.Debug($"GetLongSongs: Found long song '{Path.GetFileName(file)}' ({reader.TotalTime:hh\\:mm\\:ss}) in {gameFolder}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _fileLogger?.Warn($"GetLongSongs: Failed to read duration of '{file}' - {ex.Message}");
                    }
                }

                _fileLogger?.Debug($"GetLongSongs: Found {longSongs.Count} songs longer than {maxMinutes} minutes (scanned {allFiles.Count} files)");
                return longSongs;
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"GetLongSongs: Error - {ex.Message}", ex);
                return longSongs;
            }
        }

        /// <summary>
        /// Deletes the specified list of long songs.
        /// </summary>
        /// <param name="longSongs">List of songs to delete (from GetLongSongs)</param>
        /// <param name="progressArgs">Optional progress args for UI updates</param>
        /// <returns>Number of deleted files, freed bytes, and success status</returns>
        public (int deletedFiles, long freedBytes, bool success) DeleteLongSongs(List<(string filePath, TimeSpan duration, long fileSize, string gameFolder)> longSongs, GlobalProgressActionArgs progressArgs = null)
        {
            try
            {
                // Stop any playing music first
                _playbackService()?.Stop();

                int deletedFiles = 0;
                long freedBytes = 0;

                if (progressArgs != null)
                {
                    progressArgs.ProgressMaxValue = longSongs.Count;
                }

                // Delete files in parallel for performance
                int processed = 0;
                System.Threading.Tasks.Parallel.ForEach(longSongs, (song, state) =>
                {
                    if (progressArgs?.CancelToken.IsCancellationRequested == true)
                    {
                        state.Break();
                        return;
                    }

                    try
                    {
                        File.Delete(song.filePath);
                        System.Threading.Interlocked.Increment(ref deletedFiles);
                        System.Threading.Interlocked.Add(ref freedBytes, song.fileSize);
                        _fileLogger?.Debug($"DeleteLongSongs: Deleted '{Path.GetFileName(song.filePath)}' ({song.duration:hh\\:mm\\:ss}, {song.fileSize / 1024.0 / 1024.0:F1} MB)");
                    }
                    catch (Exception ex)
                    {
                        _fileLogger?.Warn($"DeleteLongSongs: Failed to delete '{song.filePath}' - {ex.Message}");
                    }

                    var current = System.Threading.Interlocked.Increment(ref processed);
                    if (progressArgs != null)
                    {
                        progressArgs.CurrentProgressValue = current;
                        progressArgs.Text = $"Deleting ({current}/{longSongs.Count})...";
                    }
                });

                _fileLogger?.Debug($"DeleteLongSongs: Deleted {deletedFiles} files, freed {freedBytes / 1024.0 / 1024.0:F1} MB");

                // Invalidate cache for all affected directories since we deleted files
                if (deletedFiles > 0 && _fileService != null)
                {
                    var affectedDirs = longSongs
                        .Select(s => System.IO.Path.GetDirectoryName(s.filePath))
                        .Distinct()
                        .ToList();
                    foreach (var dir in affectedDirs)
                    {
                        _fileService().InvalidateCacheForDirectory(dir);
                        _fileService().CleanupEmptyDirectory(dir);
                    }
                }

                return (deletedFiles, freedBytes, true);
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DeleteLongSongs: Error - {ex.Message}", ex);
                return (0, 0, false);
            }
        }

        // Apply settings imported from a JSON file (Backup tab → Import Settings). The settings object passed in has
        // already been merged by SettingsBackupService (machine-specific paths preserved from current settings,
        // imported values merged on top). This method pushes the merged object through SettingsService so all
        // downstream subscribers react and persists to disk.
    }
}
