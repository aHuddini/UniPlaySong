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
        // Radio mode takes priority
        if (_settings.RadioModeEnabled)
        {
            PlayRadioSong();
            return;
        }

        var songs = _fileService.GetAvailableSongs(game);

        if (songs.Length == 0)
        {
            // No game music — try default fallback
            if (_settings.EnableDefaultMusic && !string.IsNullOrEmpty(_settings.DefaultMusicPath)
                && File.Exists(_settings.DefaultMusicPath))
            {
                PlaySong(_settings.DefaultMusicPath!, game, isDefault: true);
            }
            else
            {
                FadeOutAndStop();
            }
            return;
        }

        // Pick song: if same game, advance to next; otherwise start at first
        int songIndex = 0;
        if (_state.CurrentGame?.Id == game.Id && _state.CurrentGameSongs != null)
        {
            // Same game — keep current song if still playing
            if (_state.CurrentSongPath != null && _player.IsPlaying)
                return;
        }

        var songPath = songs[songIndex];
        _state.CurrentGameSongs = songs;
        _state.CurrentSongIndex = songIndex;

        PlaySong(songPath, game, isDefault: false);
    }

    public void SkipToNext()
    {
        if (_state.IsRadioMode)
        {
            PlayRadioSong();
            return;
        }

        if (_state.CurrentGameSongs == null || _state.CurrentGameSongs.Length == 0) return;

        var nextIndex = (_state.CurrentSongIndex + 1) % _state.CurrentGameSongs.Length;
        _state.CurrentSongIndex = nextIndex;
        var songPath = _state.CurrentGameSongs[nextIndex];

        SwitchToSong(songPath);
    }

    public void FadeOutAndStop()
    {
        if (!_player.IsLoaded && !_player.IsPlaying) return;

        var duration = TimeSpan.FromMilliseconds(_settings.FadeOutDurationMs);
        _fader.FadeOut(duration, () =>
        {
            _player.Close();
            _state.Clear();
            OnPlaybackStateChanged?.Invoke();
        });
    }

    public void AddPauseSource(PauseSource source)
    {
        if (!_activePauseSources.Add(source)) return;

        if (_activePauseSources.Count == 1 && _player.IsPlaying)
        {
            _savedPosition = _player.CurrentTime;
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

        if (_activePauseSources.Count == 0 && _player.IsPaused)
        {
            _player.SetVolume(0.0);
            _player.Resume();
            var targetVolume = _settings.MusicVolume / Constants.VolumeDivisor;
            var duration = TimeSpan.FromMilliseconds(_settings.FadeInDurationMs);
            _fader.FadeIn(targetVolume, duration);
            OnPlaybackStateChanged?.Invoke();
        }
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
        if (_state.IsRadioMode)
        {
            PlayRadioSong();
            return;
        }

        if (_state.CurrentGameSongs == null || _state.CurrentGameSongs.Length == 0) return;

        if (_state.CurrentGameSongs.Length == 1)
        {
            // Single song — loop
            var targetVolume = _settings.MusicVolume / Constants.VolumeDivisor;
            _player.Load(_state.CurrentGameSongs[0]);
            _player.Play();
            _player.SetVolume(targetVolume);
            return;
        }

        // Multiple songs — advance
        SkipToNext();
    }

    private void HandlePlayerError(Exception ex)
    {
        _logger.Error(ex, "Player error during playback");

        if (_state.CurrentGameSongs != null && _state.CurrentGameSongs.Length > 1)
        {
            SkipToNext();
        }
        else if (_settings.EnableDefaultMusic && !string.IsNullOrEmpty(_settings.DefaultMusicPath))
        {
            _player.Load(_settings.DefaultMusicPath);
            _player.Play();
            var targetVolume = _settings.MusicVolume / Constants.VolumeDivisor;
            _player.SetVolume(targetVolume);
            _state.IsPlayingDefaultMusic = true;
        }
        else
        {
            _player.Close();
            _state.Clear();
        }
        OnPlaybackStateChanged?.Invoke();
    }
}
