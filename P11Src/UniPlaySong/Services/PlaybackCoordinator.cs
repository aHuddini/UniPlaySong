using Playnite;
using UniPlaySong.Settings;

namespace UniPlaySong.Services;

class PlaybackCoordinator
{
    private static readonly ILogger _logger = LogManager.GetLogger();

    private readonly MusicPlaybackService _playbackService;
    private readonly UniPlaySongSettings _settings;

    public PlaybackCoordinator(MusicPlaybackService playbackService, UniPlaySongSettings settings)
    {
        _playbackService = playbackService;
        _settings = settings;
    }

    public void HandleGameSelected(Game? game)
    {
        // Radio mode plays continuously — ignore game selection entirely
        if (_settings.RadioModeEnabled && _playbackService.IsPlaying)
        {
            _logger.Info("Coordinator: radio mode active — ignoring game selection");
            return;
        }

        if (game == null)
        {
            _logger.Info("Coordinator: null game — fading out");
            _playbackService.FadeOutAndStop();
            return;
        }

        if (!ShouldPlayMusic())
        {
            _logger.Info($"Coordinator: ShouldPlayMusic=false (enabled={_settings.EnableMusic}, vol={_settings.MusicVolume}) — fading out");
            _playbackService.FadeOutAndStop();
            return;
        }

        _playbackService.PlayGameMusic(game);
    }

    public bool ShouldPlayMusic()
    {
        return _settings.EnableMusic && _settings.MusicVolume > 0;
    }
}
