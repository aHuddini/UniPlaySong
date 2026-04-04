using UniPlaySong.Common;

namespace UniPlaySong.Audio;

// Orchestrates fade-in/out by delegating per-sample ramping to the player's
// SmoothVolumeSampleProvider. Uses PeriodicTimer (thread-pool based) to poll
// for ramp completion and execute lifecycle actions.
class MusicFader : IDisposable
{
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
        CancelCurrentFade();
        var cts = new CancellationTokenSource();
        _fadeCts = cts;
        IsFading = true;

        _player.SetVolumeRamp(0.0, fadeOutDuration);

        _ = PollUntilRampCompleteAsync(cts.Token, () =>
        {
            // Fade out complete — execute switch on UI thread
            _syncContext.Post(_ =>
            {
                if (cts.IsCancellationRequested) return;

                stopAction();
                loadAction();
                playAction();

                // Start fade in
                _player.SetVolume(0.0);
                _player.SetVolumeRamp(targetVolume, fadeInDuration);

                _ = PollUntilRampCompleteAsync(cts.Token, () =>
                {
                    _syncContext.Post(_ => IsFading = false, null);
                });
            }, null);
        });
    }

    // Standalone fade in
    public void FadeIn(double targetVolume, TimeSpan duration)
    {
        CancelCurrentFade();
        var cts = new CancellationTokenSource();
        _fadeCts = cts;
        IsFading = true;

        _player.SetVolume(0.0);
        _player.SetVolumeRamp(targetVolume, duration);

        _ = PollUntilRampCompleteAsync(cts.Token, () =>
        {
            _syncContext.Post(_ => IsFading = false, null);
        });
    }

    // Standalone fade out with callback
    public void FadeOut(TimeSpan duration, Action? onComplete = null)
    {
        CancelCurrentFade();
        var cts = new CancellationTokenSource();
        _fadeCts = cts;
        IsFading = true;

        _player.SetVolumeRamp(0.0, duration);

        _ = PollUntilRampCompleteAsync(cts.Token, () =>
        {
            _syncContext.Post(_ =>
            {
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
