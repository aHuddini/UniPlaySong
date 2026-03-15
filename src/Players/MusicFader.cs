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

        // True when paused with a pending play action (e.g., pause interrupted a song switch).
        // MusicPlaybackService checks this on RemovePauseSource to execute the orphaned action.
        public bool HasPendingPlayAction => _isPaused && _playAction != null;



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

                // Detect stalled ramp: if the player's audio thread stopped (e.g. short
                // song reached EOF during fade-out), the volume ramp freezes because
                // Read() is no longer called.  Force-complete pending actions immediately.
                bool rampStalled = _isFadingOut && _rampStarted && !_player.IsActive;

                // Handle song switching when fade-out reaches zero (or ramp stalled)
                if (_isFadingOut && (currentVol <= 0.0001 || rampStalled) && _pauseAction == null && _playAction != null)
                {
                    _fileLogger?.Debug($"[Fader] Tick — fade-out complete{(rampStalled ? " (stalled)" : "")}, switching song");
                    if (rampStalled) _player.Volume = 0;

                    // Defer both Close() and Load()+Play() to a separate dispatcher frame.
                    // Close() disposes WaveOutEvent (~15ms) and Load() creates a new one (~57ms).
                    // Running either in the timer tick blocks the UI thread during game navigation.
                    var pendingStop = _stopAction;
                    var pendingPlay = _playAction;
                    _isFadingOut = false;
                    _stopAction = _playAction = null;
                    _fadeTimer?.Stop();

                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            pendingStop?.Invoke();
                            pendingPlay.Invoke();
                            SnapshotFadeParams();
                            _rampStarted = false;
                            EnsureTimer();
                        }
                        catch (Exception ex)
                        {
                            _errorHandler?.HandleError(ex, context: "MusicFader deferred play", showUserMessage: false);
                        }
                    }), DispatcherPriority.Background);
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

                // Handle fade-out complete for pause/stop (or ramp stalled)
                if (_isFadingOut && (currentVol <= 0.0001 || rampStalled) && (_pauseAction != null || _stopAction != null))
                {
                    _fileLogger?.Debug($"[Fader] Tick — fade-out complete{(rampStalled ? " (stalled)" : "")} for {(_pauseAction != null ? "PAUSE" : "STOP")}: vol={currentVol:F4}");
                    if (rampStalled || _pauseAction != null) _player.Volume = 0;
                    _pauseAction?.Invoke();
                    _stopAction?.Invoke();
                    _isPaused = _pauseAction != null;
                    _stopAction = _pauseAction = null;
                    _fadeTimer?.Stop();
                    return;
                }

                // Pure fade-out complete (no action attached) — stop ticking
                else if (_isFadingOut && (currentVol <= 0.0001 || rampStalled))
                {
                    _fileLogger?.Debug($"[Fader] Tick — pure fade-out complete: vol={currentVol:F4}");
                    _isFadingOut = false;
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

        // Fades volume to zero with no stop/pause action.
        // Used for pre-song-end fade: the player reaches natural EOF at vol=0,
        // then OnMediaEnded fires normally to handle the auto-advance.
        public void FadeOut()
        {
            _isFadingOut = true;
            _pauseAction = null;
            _stopAction = null;
            _playAction = null;
            SnapshotFadeParams();
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
