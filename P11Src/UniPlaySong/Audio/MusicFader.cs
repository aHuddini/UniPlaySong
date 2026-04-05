using Playnite;
using UniPlaySong.Common;

namespace UniPlaySong.Audio;

// Orchestrates fade-in/out by delegating per-sample ramping to the player's
// SmoothVolumeSampleProvider. Uses PeriodicTimer (thread-pool based) to poll
// for ramp completion and execute lifecycle actions.
class MusicFader : IDisposable
{
    private static readonly ILogger _logger = LogManager.GetLogger();

    private readonly IMusicPlayer _player;
    private readonly SynchronizationContext _syncContext;

    private CancellationTokenSource? _fadeCts;
    private bool _disposed;

    public bool IsFading { get; private set; }

    public MusicFader(IMusicPlayer player)
    {
        _player = player;
        _syncContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("MusicFader must be created on the UI thread");
    }

    // Full song switch: fade out → stop → load → play → fade in
    public void Switch(
        Action stopAction,
        Action loadAction,
        Action playAction,
        double targetVolume,
        TimeSpan fadeOutDuration,
        TimeSpan fadeInDuration)
    {
        _logger.Info($"Fader.Switch: fadeOut={fadeOutDuration.TotalMilliseconds:F0}ms, fadeIn={fadeInDuration.TotalMilliseconds:F0}ms, targetVol={targetVolume:F2}");
        CancelCurrentFade();
        var cts = new CancellationTokenSource();
        _fadeCts = cts;
        IsFading = true;

        _player.SetVolumeRamp(0.0, fadeOutDuration);

        _ = PollUntilRampCompleteAsync(cts.Token, () =>
        {
            _syncContext.Post(_ =>
            {
                if (cts.IsCancellationRequested) return;

                _logger.Info("Fader.Switch: fade-out complete — executing stop/load/play");
                stopAction();
                loadAction();
                playAction();

                _player.SetVolume(0.0);
                _player.SetVolumeRamp(targetVolume, fadeInDuration);

                _ = PollUntilRampCompleteAsync(cts.Token, () =>
                {
                    _syncContext.Post(_ =>
                    {
                        _logger.Info("Fader.Switch: fade-in complete");
                        IsFading = false;
                    }, null);
                });
            }, null);
        });
    }

    public void FadeIn(double targetVolume, TimeSpan duration)
    {
        _logger.Info($"Fader.FadeIn: targetVol={targetVolume:F2}, duration={duration.TotalMilliseconds:F0}ms");
        CancelCurrentFade();
        var cts = new CancellationTokenSource();
        _fadeCts = cts;
        IsFading = true;

        _player.SetVolume(0.0);
        _player.SetVolumeRamp(targetVolume, duration);

        _ = PollUntilRampCompleteAsync(cts.Token, () =>
        {
            _syncContext.Post(_ =>
            {
                _logger.Info("Fader.FadeIn: complete");
                IsFading = false;
            }, null);
        });
    }

    public void FadeOut(TimeSpan duration, Action? onComplete = null)
    {
        _logger.Info($"Fader.FadeOut: duration={duration.TotalMilliseconds:F0}ms");
        CancelCurrentFade();
        var cts = new CancellationTokenSource();
        _fadeCts = cts;
        IsFading = true;

        _player.SetVolumeRamp(0.0, duration);

        _ = PollUntilRampCompleteAsync(cts.Token, () =>
        {
            _syncContext.Post(_ =>
            {
                _logger.Info("Fader.FadeOut: complete");
                IsFading = false;
                onComplete?.Invoke();
            }, null);
        });
    }

    public void CancelCurrentFade()
    {
        _fadeCts?.Cancel();
        _fadeCts?.Dispose();
        _fadeCts = null;
        IsFading = false;
    }

    private async Task PollUntilRampCompleteAsync(CancellationToken ct, Action onComplete)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Constants.FaderPollIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (_player.IsVolumeRampComplete)
                {
                    onComplete();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Fade was cancelled — do nothing
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelCurrentFade();
    }
}
