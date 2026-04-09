using Playnite;
using UniPlaySong.Models;

namespace UniPlaySong.Services;

// Routes all user-initiated playback actions (media keys, menu items, future UI controls).
// Single point of entry for play/pause, skip, etc. Keeps plugin shell free of control logic.
class MediaControlService : IDisposable
{
    private static readonly ILogger _logger = LogManager.GetLogger();

    private readonly MusicPlaybackService _playbackService;
    private readonly MediaKeyService _mediaKeyService;
    private bool _disposed;

    public bool IsPlaying => _playbackService.IsPlaying;
    public bool IsPaused => _playbackService.IsPaused;

    public MediaControlService(MusicPlaybackService playbackService)
    {
        _playbackService = playbackService;

        _mediaKeyService = new MediaKeyService();
        _mediaKeyService.PlayPausePressed += TogglePlayPause;
        _mediaKeyService.NextTrackPressed += SkipToNext;
        _mediaKeyService.Start();
    }

    public void TogglePlayPause()
    {
        if (_playbackService.IsPlaying)
        {
            _logger.Info("MediaControlService: pausing (Manual)");
            _playbackService.AddPauseSource(PauseSource.Manual);
        }
        else if (_playbackService.IsPaused || _playbackService.IsLoaded)
        {
            _logger.Info("MediaControlService: resuming (Manual)");
            _playbackService.RemovePauseSource(PauseSource.Manual);
        }
    }

    public void SkipToNext()
    {
        _logger.Info("MediaControlService: skip to next");
        _playbackService.SkipToNext();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mediaKeyService.Dispose();
    }
}
