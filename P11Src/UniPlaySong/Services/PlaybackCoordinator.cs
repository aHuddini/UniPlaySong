using Playnite;
using UniPlaySong.Settings;

namespace UniPlaySong.Services;

class PlaybackCoordinator
{
    private readonly MusicPlaybackService _playbackService;
    private readonly UniPlaySongSettings _settings;

    public PlaybackCoordinator(MusicPlaybackService playbackService, UniPlaySongSettings settings)
    {
        _playbackService = playbackService;
        _settings = settings;
    }

    public void HandleGameSelected(Game? game)
    {
        if (game == null)
        {
            _playbackService.FadeOutAndStop();
            return;
        }

        if (!ShouldPlayMusic())
        {
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
