using Playnite;

namespace UniPlaySong.Services;

class RadioService
{
    private static readonly ILogger _logger = LogManager.GetLogger();

    private readonly GameMusicFileService _fileService;
    private List<string> _songPool = [];
    private int _currentIndex;
    private bool _initialized;

    public int PoolSize => _songPool.Count;

    public RadioService(GameMusicFileService fileService)
    {
        _fileService = fileService;
        _fileService.OnGameMusicChanged += OnGameMusicChanged;
    }

    public string? GetNextSong()
    {
        if (!_initialized) BuildPool();
        if (_songPool.Count == 0) return null;

        if (_currentIndex >= _songPool.Count)
        {
            Shuffle();
            _currentIndex = 0;
        }

        return _songPool[_currentIndex++];
    }

    private void BuildPool()
    {
        _songPool = _fileService.GetAllSongs();
        Shuffle();
        _currentIndex = 0;
        _initialized = true;
        _logger.Info($"Radio pool built: {_songPool.Count} songs");
    }

    // Fisher-Yates shuffle — guarantees no consecutive repeats when cycling through
    private void Shuffle()
    {
        var rng = Random.Shared;
        for (int i = _songPool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (_songPool[i], _songPool[j]) = (_songPool[j], _songPool[i]);
        }
    }

    private void OnGameMusicChanged(string gameId)
    {
        // Rebuild the entire pool on any change — simple and correct.
        if (_initialized)
        {
            BuildPool();
        }
    }
}
