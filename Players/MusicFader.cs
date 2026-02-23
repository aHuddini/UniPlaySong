using System;
using System.Windows;
using System.Windows.Threading;
using Playnite.SDK;
using UniPlaySong.Services;
using UniPlaySong.Common;

namespace UniPlaySong.Players
{
    // Music fader: delegates per-sample volume ramping to the audio thread via SetVolumeRamp().
    // DispatcherTimer at Normal priority polls for ramp completion and triggers phase transitions
    // (stop/play actions for song switches, pause actions, etc.).
    // The timer does NOT step volume — it only monitors and dispatches actions.
    public class MusicFader : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IMusicPlayer _player;
        private readonly Func<double> _getMusicVolume;
        private readonly Func<double> _getFadeInDuration;
        private readonly Func<double> _getFadeOutDuration;
        private readonly ErrorHandlerService _errorHandler;
        private readonly FileLogger _fileLogger;

        private DispatcherTimer _fadeTimer;

        // State
        private bool _isFadingOut;
        private bool _isPaused;
        private Action _pauseAction;
        private Action _stopAction;
        private Action _playAction;
        private Action _preloadAction;
        private bool _preloadFired;

        // Snapshotted fade params — captured when each phase begins
        private double _snapVolume;
        private double _snapDuration;

        // Track whether we've already kicked off the audio-thread ramp for this phase
        private bool _rampStarted;

        // Post-fade delay: lets reverb tail decay before destroying the audio chain.
        // Counts timer ticks (50ms each) after fade-out reaches 0 before executing stop/play.
        private int _postFadeDelayTicks;
        private const int PostFadeDelayTickCount = 2; // 100ms (2 × 50ms ticks)

        public MusicFader(IMusicPlayer player, Func<double> getMusicVolume, Func<double> getFadeInDuration, Func<double> getFadeOutDuration, ErrorHandlerService errorHandler = null, FileLogger fileLogger = null)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _getMusicVolume = getMusicVolume ?? (() => 1.0);
            _getFadeInDuration = getFadeInDuration ?? (() => Constants.DefaultFadeInDuration);
            _getFadeOutDuration = getFadeOutDuration ?? (() => Constants.DefaultFadeOutDuration);
            _errorHandler = errorHandler;
            _fileLogger = fileLogger;

            // DispatcherTimer polls at 50ms to detect ramp completion and fire actions.
            // The actual volume ramping happens per-sample on the audio thread (NAudio)
            // or via the player's own DispatcherTimer (SDL2/WPF).
            _fadeTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _fadeTimer.Tick += (sender, e) => TimerTick();
        }

        private void TimerTick()
        {
            try
            {
                // Fire preload action once early in fade-out (gives time to prepare next song)
                if (_preloadAction != null && !_preloadFired)
                {
                    _preloadAction.Invoke();
                    _preloadAction = null;
                    _preloadFired = true;
                }

                double musicVolume = _snapVolume;
                double currentVol = _player.Volume;

                // Start the audio-thread ramp if not yet started
                if (!_rampStarted)
                {
                    double target = _isFadingOut ? 0.0 : musicVolume;
                    double duration = _snapDuration;
                    _fileLogger?.Debug($"[Fader] Tick — starting ramp: target={target:F4}, duration={duration:F3}s, currentVol={currentVol:F4}, fadingOut={_isFadingOut}");
                    _player.SetVolumeRamp(target, duration);
                    _rampStarted = true;
                }
                else
                {
                    _fileLogger?.Debug($"[Fader] Tick — polling: vol={currentVol:F4}, fadingOut={_isFadingOut}, hasPause={_pauseAction != null}, hasPlay={_playAction != null}, hasStop={_stopAction != null}");
                }

                // Handle song switching when fade-out reaches zero
                if (_isFadingOut && currentVol <= 0.0001 && _pauseAction == null && _playAction != null)
                {
                    _player.Volume = 0;

                    // Post-fade delay: let reverb tail decay before destroying the audio chain
                    if (_postFadeDelayTicks < PostFadeDelayTickCount)
                    {
                        _postFadeDelayTicks++;
                        return;
                    }

                    _fileLogger?.Debug($"[Fader] Tick — fade-out complete, switching song");
                    _stopAction?.Invoke();
                    _playAction.Invoke();

                    _isFadingOut = false;
                    _stopAction = _playAction = null;
                    _postFadeDelayTicks = 0;
                    // Re-snapshot for fade-in phase
                    SnapshotFadeParams();
                    _rampStarted = false; // Next tick will start fade-in ramp
                    return;
                }

                // Handle fade-in complete
                if (!_isFadingOut && _rampStarted && currentVol >= musicVolume - 0.001)
                {
                    _fileLogger?.Debug($"[Fader] Tick — fade-in complete: vol={currentVol:F4}, target={musicVolume:F4}");
                    _player.Volume = musicVolume;
                    _fadeTimer?.Stop();
                    return;
                }

                // Handle fade-out complete for pause/stop
                if (_isFadingOut && currentVol <= 0.0001 && (_pauseAction != null || _stopAction != null))
                {
                    _fileLogger?.Debug($"[Fader] Tick — fade-out complete for {(_pauseAction != null ? "PAUSE" : "STOP")}: vol={currentVol:F4}");
                    _player.Volume = 0;
                    _pauseAction?.Invoke();
                    _stopAction?.Invoke();
                    _isPaused = _pauseAction != null;
                    _stopAction = _pauseAction = null;
                    _fadeTimer?.Stop();
                    return;
                }
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "MusicFader timer tick",
                    showUserMessage: false
                );
            }
        }

        private void SnapshotFadeParams()
        {
            _snapVolume = _getMusicVolume();
            _snapDuration = _isFadingOut ? _getFadeOutDuration() : _getFadeInDuration();
        }

        private void EnsureTimer()
        {
            _rampStarted = false;
            _preloadFired = false;
            _postFadeDelayTicks = 0;
            if (_fadeTimer != null && !_fadeTimer.IsEnabled)
            {
                _fadeTimer.Start();
            }
        }

        public void Pause()
        {
            _fileLogger?.Debug($"[Fader] Pause() called — vol={_player.Volume:F4}, isFadingOut={_isFadingOut}, isPaused={_isPaused}");
            _isFadingOut = true;
            _pauseAction = _player.Pause;
            SnapshotFadeParams();
            _fileLogger?.Debug($"[Fader] Pause() snap — target=0, duration={_snapDuration:F3}s, musicVol={_snapVolume:F4}");
            EnsureTimer();
        }

        public void Switch(Action stopAction, Action preloadAction = null, Action playAction = null)
        {
            _preloadAction = preloadAction;
            _playAction = playAction;
            _stopAction = stopAction;

            if (stopAction == null)
            {
                if (_isFadingOut)
                {
                    _playAction = null;
                    _preloadAction = null;
                    _isFadingOut = false;
                }
            }
            else
            {
                _isFadingOut = true;
            }
            SnapshotFadeParams();
            EnsureTimer();
        }

        public void Resume()
        {
            _fileLogger?.Debug($"[Fader] Resume() called — vol={_player.Volume:F4}, isPaused={_isPaused}, isFadingOut={_isFadingOut}, hasPlay={_playAction != null}, hasStop={_stopAction != null}");
            if (!_isPaused)
            {
                _pauseAction = null;
                if (_playAction == null && _stopAction == null)
                {
                    // Reversing a fade-out that hasn't completed — switch to fading in
                    _fileLogger?.Debug($"[Fader] Resume() — reversing incomplete fade-out, switching to fade-in");
                    _isFadingOut = false;
                }
            }
            else
            {
                if (_playAction != null || _stopAction != null)
                {
                    _fileLogger?.Debug($"[Fader] Resume() — executing pending play/stop actions");
                    _stopAction?.Invoke();
                    _playAction?.Invoke();
                    _player.Volume = 0;
                    _stopAction = _playAction = null;
                }
                else
                {
                    // Ensure volume is 0 before resuming so the fade-in ramp starts
                    // from silence — prevents a blip at the volume the player was paused at
                    _fileLogger?.Debug($"[Fader] Resume() — setting vol=0, then calling player.Resume()");
                    _player.Volume = 0;
                    _fileLogger?.Debug($"[Fader] Resume() — vol after set: {_player.Volume:F4}");
                    _player.Resume();
                    _fileLogger?.Debug($"[Fader] Resume() — player resumed, vol now: {_player.Volume:F4}");
                }
                _isPaused = false;
                _isFadingOut = false;
            }
            SnapshotFadeParams();
            _fileLogger?.Debug($"[Fader] Resume() snap — target={_snapVolume:F4}, duration={_snapDuration:F3}s, isFadingOut={_isFadingOut}");
            EnsureTimer();
        }

        public void FadeIn()
        {
            _isFadingOut = false;
            _pauseAction = null;
            _stopAction = null;
            _playAction = null;
            SnapshotFadeParams();
            EnsureTimer();
        }

        // Cancels any ongoing fade and stops the timer.
        public void CancelFade()
        {
            _fadeTimer?.Stop();
            _isFadingOut = false;
            _isPaused = false;
            _pauseAction = null;
            _stopAction = null;
            _playAction = null;
            _preloadAction = null;
            _rampStarted = false;
            _postFadeDelayTicks = 0;
        }

        public void FadeOutAndStop(Action onComplete = null)
        {
            _isFadingOut = true;
            _stopAction = () =>
            {
                _player?.Stop();
                onComplete?.Invoke();
            };
            _pauseAction = null;
            _playAction = null;
            SnapshotFadeParams();
            EnsureTimer();
        }

        public void Dispose()
        {
            try
            {
                _fadeTimer?.Stop();
                _fadeTimer = null;
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "MusicFader disposing",
                    showUserMessage: false
                );
            }
        }
    }
}
