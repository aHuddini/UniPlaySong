using System.IO;
using Playnite;
using UniPlaySong.Audio;
using UniPlaySong.Common;
using UniPlaySong.Models;
using UniPlaySong.Settings;

namespace UniPlaySong.Services;

class MusicPlaybackService
{
    private static readonly ILogger _logger = LogManager.GetLogger();

    private readonly IMusicPlayer _player;
    private readonly MusicFader _fader;
    private readonly GameMusicFileService _fileService;
    private readonly RadioService _radioService;
    private readonly UniPlaySongSettings _settings;
    private readonly PlaybackState _state = new();
    private readonly HashSet<PauseSource> _activePauseSources = [];

    private TimeSpan _savedPosition;
    private TimeSpan _defaultMusicPausedOnTime; // position preservation for default music
    private bool _isPaused; // tracks logical pause state for EOF guard

    public bool IsPlaying => _player.IsPlaying;
    public bool IsPaused => _player.IsPaused;
    public bool IsLoaded => _player.IsLoaded;
    public string? CurrentSongPath => _state.CurrentSongPath;
    public Game? CurrentGame => _state.CurrentGame;
    public bool IsPlayingDefaultMusic => _state.IsPlayingDefaultMusic;

    public event Action? OnPlaybackStateChanged;

    public MusicPlaybackService(
        IMusicPlayer player,
        MusicFader fader,
        GameMusicFileService fileService,
        RadioService radioService,
        UniPlaySongSettings settings)
    {
        _player = player;
        _fader = fader;
        _fileService = fileService;
        _radioService = radioService;
        _settings = settings;

        _player.OnSongEnded += HandleSongEnded;
        _player.OnError += HandlePlayerError;
    }

    public void PlayGameMusic(Game game)
    {
        _logger.Info($"PlayGameMusic: {game.Name} [id={game.Id}]");

        if (_settings.RadioModeEnabled)
        {
            _logger.Info("PlayGameMusic: radio mode active — delegating");
            PlayRadioSong();
            return;
        }

        var songs = _fileService.GetAvailableSongs(game);
        _logger.Info($"PlayGameMusic: {songs.Length} song(s) found for {game.Name}");

        if (songs.Length == 0)
        {
            if (_settings.EnableDefaultMusic && !string.IsNullOrEmpty(_settings.DefaultMusicPath)
                && File.Exists(_settings.DefaultMusicPath))
            {
                var defaultPath = _settings.DefaultMusicPath!;

                if (_state.IsPlayingDefaultMusic
                    && string.Equals(_state.CurrentSongPath, defaultPath, StringComparison.OrdinalIgnoreCase)
                    && _player.IsPlaying)
                {
                    _logger.Info($"PlayGameMusic: default music already playing — continuing for {game.Name}");
                    _state.CurrentGame = game;
                    return;
                }

                _logger.Info($"PlayGameMusic: no game music, switching to default: {Path.GetFileName(defaultPath)}");
                PlayDefaultMusic(defaultPath, game);
            }
            else
            {
                _logger.Info("PlayGameMusic: no music and no default configured — fading to silence");
                FadeOutAndStop();
            }
            return;
        }

        if (_state.IsPlayingDefaultMusic && _player.IsPlaying)
        {
            _defaultMusicPausedOnTime = _player.CurrentTime;
            _logger.Info($"PlayGameMusic: saving default music position at {_defaultMusicPausedOnTime.TotalSeconds:F1}s");
        }

        if (_state.CurrentGame?.Id == game.Id && _state.CurrentGameSongs != null)
        {
            if (_state.CurrentSongPath != null && _player.IsPlaying && !_state.IsPlayingDefaultMusic)
            {
                _logger.Info($"PlayGameMusic: same game, same song still playing — no action");
                return;
            }
        }

        int songIndex = 0;
        var songPath = songs[songIndex];
        _state.CurrentGameSongs = songs;
        _state.CurrentSongIndex = songIndex;

        _logger.Info($"PlayGameMusic: playing {Path.GetFileName(songPath)} (song 1/{songs.Length})");
        PlaySong(songPath, game, isDefault: false);
    }

    public void SkipToNext()
    {
        if (_state.IsRadioMode)
        {
            _logger.Info("SkipToNext: radio mode — getting next shuffle");
            PlayRadioSong();
            return;
        }

        if (_state.CurrentGameSongs == null || _state.CurrentGameSongs.Length == 0) return;

        var nextIndex = (_state.CurrentSongIndex + 1) % _state.CurrentGameSongs.Length;
        _state.CurrentSongIndex = nextIndex;
        var songPath = _state.CurrentGameSongs[nextIndex];

        _logger.Info($"SkipToNext: advancing to {Path.GetFileName(songPath)} (song {nextIndex + 1}/{_state.CurrentGameSongs.Length})");
        SwitchToSong(songPath);
    }

    public void FadeOutAndStop()
    {
        if (!_player.IsLoaded && !_player.IsPlaying) return;

        _logger.Info("FadeOutAndStop: fading to silence");
        var duration = TimeSpan.FromMilliseconds(_settings.FadeOutDurationMs);
        _fader.FadeOut(duration, () =>
        {
            _logger.Info("FadeOutAndStop: fade complete — closing player");
            _player.Close();
            _state.Clear();
            _isPaused = false;
            _defaultMusicPausedOnTime = TimeSpan.Zero;
            OnPlaybackStateChanged?.Invoke();
        });
    }

    public void AddPauseSource(PauseSource source)
    {
        if (!_activePauseSources.Add(source)) return;

        _logger.Info($"AddPauseSource: {source} (active sources: {_activePauseSources.Count})");

        if (_activePauseSources.Count == 1 && _player.IsPlaying)
        {
            _savedPosition = _player.CurrentTime;
            _isPaused = true;
            _logger.Info($"AddPauseSource: first source — fading out and pausing (position={_savedPosition.TotalSeconds:F1}s)");
            var duration = TimeSpan.FromMilliseconds(_settings.FadeOutDurationMs);
            _fader.FadeOut(duration, () =>
            {
                _player.Pause();
                OnPlaybackStateChanged?.Invoke();
            });
        }
    }

    public void RemovePauseSource(PauseSource source)
    {
        if (!_activePauseSources.Remove(source)) return;

        _logger.Info($"RemovePauseSource: {source} (remaining sources: {_activePauseSources.Count})");

        if (_activePauseSources.Count == 0 && (_player.IsPaused || _isPaused))
        {
            _isPaused = false;
            _logger.Info("RemovePauseSource: last source removed — resuming with fade in");
            _player.SetVolume(0.0);
            _player.Resume();
            var targetVolume = _settings.MusicVolume / Constants.VolumeDivisor;
            var duration = TimeSpan.FromMilliseconds(_settings.FadeInDurationMs);
            _fader.FadeIn(targetVolume, duration);
            OnPlaybackStateChanged?.Invoke();
        }
    }

    private void PlayDefaultMusic(string defaultPath, Game game)
    {
        _logger.Info($"PlayDefaultMusic: {Path.GetFileName(defaultPath)} for {game.Name} (savedPosition={_defaultMusicPausedOnTime.TotalSeconds:F1}s)");
        var targetVolume = _settings.MusicVolume / Constants.VolumeDivisor;
        var fadeInDuration = TimeSpan.FromMilliseconds(_settings.FadeInDurationMs);
        var fadeOutDuration = TimeSpan.FromMilliseconds(_settings.FadeOutDurationMs);

        if (_player.IsPlaying)
        {
            _logger.Info("PlayDefaultMusic: crossfading from current song");
            _fader.Switch(
                stopAction: () => _player.Close(),
                loadAction: () => _player.Load(defaultPath),
                playAction: () =>
                {
                    if (_defaultMusicPausedOnTime > TimeSpan.Zero)
                        _player.Play(_defaultMusicPausedOnTime);
                    else
                        _player.Play();
                },
                targetVolume: targetVolume,
                fadeOutDuration: fadeOutDuration,
                fadeInDuration: fadeInDuration);
        }
        else
        {
            _player.Load(defaultPath);
            if (_defaultMusicPausedOnTime > TimeSpan.Zero)
                _player.Play(_defaultMusicPausedOnTime);
            else
                _player.Play();
            _fader.FadeIn(targetVolume, fadeInDuration);
        }

        _state.CurrentGame = game;
        _state.CurrentSongPath = defaultPath;
        _state.IsPlayingDefaultMusic = true;
        _state.IsRadioMode = false;
        _state.CurrentGameSongs = null;
        OnPlaybackStateChanged?.Invoke();
    }

    private void PlaySong(string songPath, Game game, bool isDefault)
    {
        var targetVolume = _settings.MusicVolume / Constants.VolumeDivisor;
        var fadeInDuration = TimeSpan.FromMilliseconds(_settings.FadeInDurationMs);
        var fadeOutDuration = TimeSpan.FromMilliseconds(_settings.FadeOutDurationMs);

        if (_player.IsPlaying)
        {
            SwitchToSong(songPath);
        }
        else
        {
            _player.Load(songPath);
            _player.Play();
            _fader.FadeIn(targetVolume, fadeInDuration);
        }

        _state.CurrentGame = game;
        _state.CurrentSongPath = songPath;
        _state.IsPlayingDefaultMusic = isDefault;
        _state.IsRadioMode = false;
        OnPlaybackStateChanged?.Invoke();
    }

    private void PlayRadioSong()
    {
        var nextSong = _radioService.GetNextSong();
        if (nextSong == null)
        {
            _logger.Info("Radio mode: no songs in pool");
            FadeOutAndStop();
            return;
        }

        var targetVolume = _settings.MusicVolume / Constants.VolumeDivisor;
        var fadeInDuration = TimeSpan.FromMilliseconds(_settings.FadeInDurationMs);

        if (_player.IsPlaying)
        {
            SwitchToSong(nextSong);
        }
        else
        {
            _player.Load(nextSong);
            _player.Play();
            _fader.FadeIn(targetVolume, fadeInDuration);
        }

        _state.CurrentSongPath = nextSong;
        _state.IsRadioMode = true;
        _state.IsPlayingDefaultMusic = false;
        OnPlaybackStateChanged?.Invoke();
    }

    private void SwitchToSong(string songPath)
    {
        var targetVolume = _settings.MusicVolume / Constants.VolumeDivisor;
        var fadeInDuration = TimeSpan.FromMilliseconds(_settings.FadeInDurationMs);
        var fadeOutDuration = TimeSpan.FromMilliseconds(_settings.FadeOutDurationMs);

        _fader.Switch(
            stopAction: () => _player.Close(),
            loadAction: () => _player.Load(songPath),
            playAction: () => _player.Play(),
            targetVolume: targetVolume,
            fadeOutDuration: fadeOutDuration,
            fadeInDuration: fadeInDuration);

        _state.CurrentSongPath = songPath;
    }

    private void HandleSongEnded()
    {
        _logger.Info($"HandleSongEnded: current={Path.GetFileName(_state.CurrentSongPath)}, isDefault={_state.IsPlayingDefaultMusic}, isRadio={_state.IsRadioMode}, isPaused={_isPaused}");

        if (_isPaused)
        {
            _logger.Info("HandleSongEnded: ignoring — music is paused (short track EOF guard)");
            return;
        }

        if (_state.IsRadioMode)
        {
            _logger.Info("HandleSongEnded: radio mode — advancing to next shuffle");
            PlayRadioSong();
            return;
        }

        if (_state.IsPlayingDefaultMusic && !string.IsNullOrEmpty(_state.CurrentSongPath))
        {
            _logger.Info($"HandleSongEnded: looping default music: {Path.GetFileName(_state.CurrentSongPath)}");
            var targetVolume = _settings.MusicVolume / Constants.VolumeDivisor;
            _defaultMusicPausedOnTime = TimeSpan.Zero;
            _player.Load(_state.CurrentSongPath);
            _player.Play();
            _player.SetVolume(targetVolume);
            return;
        }

        if (_state.CurrentGameSongs == null || _state.CurrentGameSongs.Length == 0)
        {
            _logger.Info("HandleSongEnded: no game songs available — doing nothing");
            return;
        }

        if (_state.CurrentGameSongs.Length == 1)
        {
            _logger.Info($"HandleSongEnded: looping single game song: {Path.GetFileName(_state.CurrentGameSongs[0])}");
            var targetVolume = _settings.MusicVolume / Constants.VolumeDivisor;
            _player.Load(_state.CurrentGameSongs[0]);
            _player.Play();
            _player.SetVolume(targetVolume);
            return;
        }

        _logger.Info("HandleSongEnded: advancing to next game song");
        SkipToNext();
    }

    private void HandlePlayerError(Exception ex)
    {
        _logger.Error(ex, $"Player error: {Path.GetFileName(_state.CurrentSongPath)}");

        if (_state.CurrentGameSongs != null && _state.CurrentGameSongs.Length > 1)
        {
            _logger.Info("HandlePlayerError: skipping to next song");
            SkipToNext();
        }
        else if (_settings.EnableDefaultMusic && !string.IsNullOrEmpty(_settings.DefaultMusicPath))
        {
            _logger.Info("HandlePlayerError: falling back to default music");
            _player.Load(_settings.DefaultMusicPath);
            _player.Play();
            var targetVolume = _settings.MusicVolume / Constants.VolumeDivisor;
            _player.SetVolume(targetVolume);
            _state.IsPlayingDefaultMusic = true;
        }
        else
        {
            _logger.Info("HandlePlayerError: no fallback — closing player");
            _player.Close();
            _state.Clear();
        }
        OnPlaybackStateChanged?.Invoke();
    }
}
