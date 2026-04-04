using System.Collections.Concurrent;
using System.IO;
using Playnite;
using UniPlaySong.Common;

namespace UniPlaySong.Services;

class GameMusicFileService : IDisposable
{
    private static readonly ILogger _logger = LogManager.GetLogger();

    private readonly string _baseMusicPath;
    private readonly ConcurrentDictionary<string, string> _pathCache = new();
    private readonly ConcurrentDictionary<string, string[]> _songCache = new();
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    // Fired when a game's music folder changes (for RadioService to update its pool)
    public event Action<string>? OnGameMusicChanged;

    public string BaseMusicPath => _baseMusicPath;

    public GameMusicFileService(string userDataDir)
    {
        _baseMusicPath = Path.Combine(userDataDir, Constants.PluginFolderName, Constants.GamesFolderName);
        Directory.CreateDirectory(_baseMusicPath);
        InitializeWatcher();
    }

    public string GetGameMusicDirectory(Game game)
    {
        return _pathCache.GetOrAdd(game.Id, id => Path.Combine(_baseMusicPath, id));
    }

    public string[] GetAvailableSongs(Game game)
    {
        return _songCache.GetOrAdd(game.Id, _ => ScanDirectory(game));
    }

    public bool HasMusic(Game game)
    {
        return GetAvailableSongs(game).Length > 0;
    }

    // Returns all game IDs that have music folders
    public IEnumerable<string> GetAllGameIdsWithMusic()
    {
        if (!Directory.Exists(_baseMusicPath)) yield break;

        foreach (var dir in Directory.EnumerateDirectories(_baseMusicPath))
        {
            var gameId = Path.GetFileName(dir);
            if (gameId != null) yield return gameId;
        }
    }

    // Scans all songs across all game folders (used by RadioService for pool building)
    public List<string> GetAllSongs()
    {
        var songs = new List<string>();
        if (!Directory.Exists(_baseMusicPath)) return songs;

        foreach (var dir in Directory.EnumerateDirectories(_baseMusicPath))
        {
            songs.AddRange(ScanDirectoryPath(dir));
        }
        return songs;
    }

    private string[] ScanDirectory(Game game)
    {
        var path = GetGameMusicDirectory(game);
        return ScanDirectoryPath(path);
    }

    private static string[] ScanDirectoryPath(string path)
    {
        if (!Directory.Exists(path)) return [];

        try
        {
            return Directory.GetFiles(path)
                .Where(f => Constants.SupportedExtensions.Contains(
                    Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            LogManager.GetLogger().Error(ex, $"Failed to scan directory: {path}");
            return [];
        }
    }

    private void InitializeWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(_baseMusicPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Error += OnWatcherError;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize FileSystemWatcher — cache will not auto-invalidate");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        InvalidateForPath(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        InvalidateForPath(e.OldFullPath);
        InvalidateForPath(e.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.Error(e.GetException(), "FileSystemWatcher error — clearing all caches");
        _songCache.Clear();
    }

    private void InvalidateForPath(string fullPath)
    {
        // Extract the game ID from the path (first subdirectory under _baseMusicPath)
        var relativePath = Path.GetRelativePath(_baseMusicPath, fullPath);
        var gameId = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault();
        if (gameId != null)
        {
            _songCache.TryRemove(gameId, out _);
            OnGameMusicChanged?.Invoke(gameId);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
    }
}
