using System;
using System.Timers;
using System.Windows;
using Playnite.SDK;
using UniPlaySong.Services;
using UniPlaySong.Common;

namespace UniPlaySong.Players
{
    /// <summary>
    /// Music fader matching PlayniteSound pattern.
    /// Uses synchronous Invoke to ensure accurate fade timing.
    /// </summary>
    public class MusicFader : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IMusicPlayer _player;
        private readonly Func<double> _getMusicVolume;
        private readonly Func<double> _getFadeInDuration;
        private readonly Func<double> _getFadeOutDuration;
        private readonly ErrorHandlerService _errorHandler;
        
        private Timer _fadeTimer;
        
        // State - matching PNS exactly
        private bool _isFadingOut;
        private bool _isPaused;
        private Action _pauseAction;
        private Action _stopAction;
        private Action _playAction;
        private Action _preloadAction;
        
        // Dynamic frequency tracking (matching PNS exactly)
        private DateTime _lastTickCall = default;
        private DateTime _fadeStartTime = default;
        
        // Store the starting volume when fade-out begins
        // This ensures we continue from the current volume when switching to games with no music
        private double _fadeOutStartVolume = 0.0;

        public MusicFader(IMusicPlayer player, Func<double> getMusicVolume, Func<double> getFadeInDuration, Func<double> getFadeOutDuration, ErrorHandlerService errorHandler = null)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _getMusicVolume = getMusicVolume ?? (() => 1.0);
            _getFadeInDuration = getFadeInDuration ?? (() => Constants.DefaultFadeInDuration);
            _getFadeOutDuration = getFadeOutDuration ?? (() => Constants.DefaultFadeOutDuration);
            _errorHandler = errorHandler;

            // Use 16ms interval (60 FPS) for smoother, more perceptible fades
            // Common practice: 16-33ms (60-30 FPS) for audio fades
            // However, frequency will be calculated dynamically based on actual tick intervals
            _fadeTimer = new Timer(16) { AutoReset = false };
            _fadeTimer.Elapsed += (sender, e) =>
            {
                var app = Application.Current;
                if (app?.Dispatcher != null)
                {
                    // Use Invoke (synchronous) to ensure fade timing is accurate
                    // BeginInvoke can cause timing issues with the fade progression
                    app.Dispatcher.Invoke(new Action(() => TimerTick()));
                }
                else
                {
                    TimerTick();
                }
            };
        }

        private void TimerTick()
        {
            try
            {
                // Handle preload action first (matching PNS)
                if (_preloadAction != null)
                {
                    _preloadAction.Invoke();
                    _preloadAction = null;
                }

                double musicVolume = _getMusicVolume();
                
                // Use appropriate fade duration based on direction
                double fadeDuration = _isFadingOut ? _getFadeOutDuration() : _getFadeInDuration();
                
                // Initialize fade start time if this is the first tick of a new fade
                if (_fadeStartTime == default)
                {
                    _fadeStartTime = DateTime.Now;
                    // Capture current player volume as starting point for fade-out
                    // This ensures smooth continuation when switching to games with no music
                    if (_isFadingOut && _player?.IsActive == true)
                    {
                        _fadeOutStartVolume = _player.Volume;
                    }
                    else if (_isFadingOut)
                    {
                        // If player not active, use music volume as starting point
                        _fadeOutStartVolume = musicVolume;
                    }
                }
                
                // Calculate elapsed time since fade started
                double elapsedSeconds = (DateTime.Now - _fadeStartTime).TotalSeconds;
                double progress = Math.Min(1.0, elapsedSeconds / fadeDuration);
                
                // Apply exponential curve for natural-sounding fades
                // Human perception of volume is logarithmic, so we use exponential curves
                // Fade-in: exponential curve (starts fast, slows down) - uses progress^2
                // Fade-out: inverse exponential (starts slow, speeds up) - uses 1 - (1-progress)^2
                double targetVolume;
                if (_isFadingOut)
                {
                    // Fade-out: exponential decay (1 - progress^2) - starts slow, speeds up
                    // This makes the fade feel natural as it gets quieter
                    // CRITICAL FIX: Use stored starting volume instead of musicVolume
                    // This preserves volume state when switching to games with no music
                    double startVolume = _fadeOutStartVolume > 0.001 ? _fadeOutStartVolume : musicVolume;
                    double curveProgress = 1.0 - Math.Pow(1.0 - progress, 2.0);
                    targetVolume = startVolume * (1.0 - curveProgress);
                }
                else
                {
                    // Fade-in: exponential rise (progress^2) - starts fast, slows down
                    // This prevents the "delayed at low volume" feeling
                    double curveProgress = Math.Pow(progress, 2.0);
                    targetVolume = musicVolume * curveProgress;
                }
                
                // Clamp to valid range
                // For fade-out, clamp to the starting volume (preserves current state)
                if (_isFadingOut)
                {
                    double maxVolume = _fadeOutStartVolume > 0.001 ? _fadeOutStartVolume : musicVolume;
                    targetVolume = Math.Max(0.0, Math.Min(maxVolume, targetVolume));
                }
                else
                {
                    targetVolume = Math.Max(0.0, Math.Min(musicVolume, targetVolume));
                }
                
                // Apply the target volume directly (time-based, not step-based)
                // This ensures smooth, predictable fades regardless of tick timing
                if (_isFadingOut && _player?.IsActive == true)
                {
                    _player.Volume = targetVolume;
                }
                else if (_isFadingOut)
                {
                    _player.Volume = 0;
                }
                else
                {
                    // Fade in: apply target volume
                    _player.Volume = targetVolume;
                }

                // Handle song switching during fade-out FIRST (before completion check)
                // This ensures smooth transition from fade-out to fade-in
                if (_isFadingOut && _player.Volume <= 0.01 && _pauseAction == null && _playAction != null)
                {
                    // Fade out complete (or nearly complete), switch to new song
                    _stopAction?.Invoke();
                    _player.Volume = 0; // Ensure volume is 0 before playAction
                    _playAction.Invoke();
                    
                    // CRITICAL FIX: After playAction (which calls Load()/Play()), force volume to 0
                    // SDL2's Play() doesn't reset volume, but we need to ensure it's 0 for fade-in
                    // This is especially important in fullscreen where volume state might be inconsistent
                    // Also ensure _isFadingOut is false BEFORE setting volume, so next tick knows to fade in
                    _isFadingOut = false;
                    _player.Volume = 0;
                    
                    // Reset fade start time for new fade-in
                    _fadeStartTime = default;
                    _fadeOutStartVolume = 0.0; // Clear fade-out start volume
                    
                    _stopAction = _playAction = null;
                    // CRITICAL: Don't return here - continue timer to fade in (matching PNS)
                    // The next tick will be in fade-in mode and will start fading in from volume 0
                }
                // Check completion - time-based approach (after handling song switch)
                else if (progress >= 1.0)
                {
                    // Fade complete - set final volume and stop
                    if (_isFadingOut)
                    {
                        _player.Volume = 0;
                    }
                    else
                    {
                        _player.Volume = musicVolume;
                    }
                    
                    // Reset fade start time for next fade
                    _fadeStartTime = default;
                    _fadeOutStartVolume = 0.0; // Clear fade-out start volume
                    
                    // If fade-in is complete, we're done
                    if (!_isFadingOut)
                    {
                        return;
                    }
                }
                
                // Handle pause/stop actions (separate from song switching)
                if (_player.Volume == 0 && (_pauseAction != null || _stopAction != null))
                {
                    // Fade out complete, pause or stop
                    _pauseAction?.Invoke();
                    _stopAction?.Invoke();
                    _isPaused = _pauseAction != null;
                    _stopAction = _pauseAction = null;
                    return;
                }

                // Continue fading (matching PNS - always restart timer at end)
                _fadeTimer?.Start();
                _lastTickCall = DateTime.Now; // Track actual tick time for dynamic frequency
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

        private void EnsureTimer()
        {
            if (_fadeTimer != null && !_fadeTimer.Enabled)
            {
                // Reset tick tracking and fade start time when starting new fade
                _lastTickCall = default;
                _fadeStartTime = default;
                
                // Diagnostic: Log dispatcher availability
                _fadeTimer.Start();
            }
        }

        public void Pause()
        {
            _isFadingOut = true;
            _pauseAction = _player.Pause;
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
                // Always start fade-out when switching (even if already fading in)
                bool wasAlreadyFadingOut = _isFadingOut;
                _isFadingOut = true;
                
                // CRITICAL FIX: If already fading out, don't reset fade start time
                // This prevents volume from resetting to full when rapidly switching games
                // Instead, continue the fade-out from the current volume level
                if (!wasAlreadyFadingOut)
                {
                    // Only reset fade start time if this is a new fade-out
                    _fadeStartTime = default;
                }
                // If already fading out, keep the existing _fadeStartTime to continue the fade smoothly
            }
            EnsureTimer();
        }

        public void Resume()
        {
            if (!_isPaused)
            {
                _pauseAction = null;
                if (_playAction == null && _stopAction == null)
                {
                    // Reversing a fade-out that hasn't completed yet.
                    // Calculate where the fade-in should start to match current volume,
                    // preventing a volume jump (echo/doppler artifact on focus restore).
                    double currentVolume = _player?.Volume ?? 0;
                    double musicVolume = _getMusicVolume();
                    _isFadingOut = false;
                    _fadeOutStartVolume = 0.0;

                    if (musicVolume > 0.001 && currentVolume > 0.001)
                    {
                        // Fade-in formula: volume = musicVolume * progress²
                        // Solve for progress: progress = sqrt(currentVolume / musicVolume)
                        double ratio = Math.Min(1.0, currentVolume / musicVolume);
                        double equivalentProgress = Math.Sqrt(ratio);
                        double fadeInDuration = _getFadeInDuration();
                        // Backdate start time so next tick continues from current volume
                        _fadeStartTime = DateTime.Now.AddSeconds(-equivalentProgress * fadeInDuration);
                    }
                    else
                    {
                        _fadeStartTime = default;
                    }
                }
            }
            else
            {
                if (_playAction != null || _stopAction != null)
                {
                    _stopAction?.Invoke();
                    _playAction?.Invoke();
                    _player.Volume = 0;
                    _stopAction = _playAction = null;
                }
                else
                {
                    _player.Resume();
                }
                _isPaused = false;
                _isFadingOut = false;
            }
            EnsureTimer();
        }

        public void FadeIn()
        {
            _isFadingOut = false;
            _pauseAction = null;
            _stopAction = null;
            _playAction = null;
            // CRITICAL: Ensure volume starts at 0 for fade-in
            // This is especially important in fullscreen where volume might not be properly initialized
            if (_player != null)
            {
                _player.Volume = 0;
            }
            // Reset fade start time for new fade-in
            _fadeStartTime = default;
            _fadeOutStartVolume = 0.0; // Clear fade-out start volume
            EnsureTimer();
        }

        /// <summary>
        /// Cancels any ongoing fade operation and stops the timer.
        /// Used when setting volume directly (e.g., preview playback).
        /// </summary>
        public void CancelFade()
        {
            _fadeTimer?.Stop();
            _isFadingOut = false;
            _isPaused = false;
            _pauseAction = null;
            _stopAction = null;
            _playAction = null;
            _preloadAction = null;
            _fadeStartTime = default;
            _fadeOutStartVolume = 0.0;
        }

        public void FadeOutAndStop(Action onComplete = null)
        {
            bool wasAlreadyFadingOut = _isFadingOut;
            _isFadingOut = true;
            _stopAction = () =>
            {
                _player?.Stop();
                onComplete?.Invoke();
            };
            _pauseAction = null;
            _playAction = null;
            
            // CRITICAL FIX: If already fading out, don't reset fade start time or starting volume
            // This prevents the fade-out from restarting when rapidly switching to games with no music
            // The fade-out will continue from its current progress and complete naturally
            if (!wasAlreadyFadingOut)
            {
                // Only reset fade start time if this is a new fade-out
                _fadeStartTime = default;
                _fadeOutStartVolume = 0.0; // Will be captured on first tick
            }
            // If already fading out, keep the existing _fadeStartTime and _fadeOutStartVolume
            // to continue the fade smoothly from the current volume level
            
            EnsureTimer();
        }

        public void Dispose()
        {
            try
            {
                _fadeTimer?.Stop();
                _fadeTimer?.Close();
                _fadeTimer?.Dispose();
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


