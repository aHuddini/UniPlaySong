using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Playnite.SDK;
using UniPlaySong.Services;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Music player with dual-player preloading system (like PlayniteSound)
    /// Allows seamless switching by preloading next song while current fades out
    /// </summary>
    public class MusicPlayer : IMusicPlayer
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        
        private MediaPlayer _mediaPlayer;
        private MediaTimeline _timeLine;
        private readonly ErrorHandlerService _errorHandler;
        
        // Preloaded player for seamless switching
        private MediaPlayer _preloadedMediaPlayer;
        private MediaTimeline _preloadedTimeLine;
        private string _preloadedFile = "";

        public MusicPlayer(ErrorHandlerService errorHandler = null)
        {
            _errorHandler = errorHandler;
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;
            _timeLine = new MediaTimeline();
        }

        public event EventHandler MediaEnded;
        public event EventHandler<ExceptionEventArgs> MediaFailed;

        public double Volume
        {
            get => _mediaPlayer?.Volume ?? 0;
            set
            {
                // PNS pattern: Simple, direct volume set (MediaPlayer is free-threaded)
                // No dispatcher, no _pendingVolume, no complexity
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Volume = value;
                }
            }
        }

        public bool IsLoaded => _mediaPlayer?.Clock != null;
        
        public bool IsActive
        {
            get
            {
                if (_errorHandler != null)
                {
                    return _errorHandler.Try(
                        () => _mediaPlayer?.Clock?.CurrentState == ClockState.Active,
                        defaultValue: false,
                        context: "checking if music player is active",
                        showUserMessage: false
                    );
                }
                try { return _mediaPlayer?.Clock?.CurrentState == ClockState.Active; }
                catch { return false; }
            }
        }

        public TimeSpan? CurrentTime
        {
            get
            {
                if (_errorHandler != null)
                {
                    return _errorHandler.Try<TimeSpan?>(
                        () => _mediaPlayer?.Clock?.CurrentTime ?? TimeSpan.Zero,
                        defaultValue: null,
                        context: "getting music player current time",
                        showUserMessage: false
                    );
                }
                try { return _mediaPlayer?.Clock?.CurrentTime ?? TimeSpan.Zero; }
                catch { return null; }
            }
        }

        public string Source => _mediaPlayer?.Source?.LocalPath ?? string.Empty;

        /// <summary>
        /// Preload a file into a separate player (for seamless switching)
        /// </summary>
        public void PreLoad(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                return;

            try
            {
                // Dispose any existing preloaded player
                DisposePreloaded();
                
                // Create new preloaded player
                _preloadedMediaPlayer = new MediaPlayer();
                _preloadedMediaPlayer.MediaEnded += OnMediaEnded;
                _preloadedMediaPlayer.MediaFailed += OnMediaFailed;
                _preloadedMediaPlayer.Volume = 0; // Start silent
                
                _preloadedTimeLine = new MediaTimeline(new Uri(filePath));
                _preloadedMediaPlayer.Clock = _preloadedTimeLine.CreateClock();
                _preloadedFile = filePath;
                
                // Pause at start
                _preloadedMediaPlayer.Clock.Controller?.Pause();
                
                Logger.Debug($"Preloaded: {filePath}");
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: $"preloading music file '{filePath}'",
                    showUserMessage: false
                );
            }
        }

        /// <summary>
        /// Load a file - uses preloaded player if available
        /// </summary>
        public void Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                return;

            try
            {
                // Check if this file was preloaded
                if (_preloadedMediaPlayer != null && _preloadedFile == filePath)
                {
                    // Swap players - instant!
                    SwapMediaPlayers();
                    Logger.Debug($"Swapped to preloaded: {filePath}");
                }
                else
                {
                    // Load fresh (matching PNS exactly - no volume restoration)
                    _timeLine.Source = new Uri(filePath);
                    _mediaPlayer.Clock = _timeLine.CreateClock();
                    Logger.Debug($"Loaded fresh: {filePath}");
                }
                
                // Dispose any remaining preloaded player
                DisposePreloaded();
                
                // PNS pattern: Don't restore volume here - let the fader control volume completely
                // The fader will set volume to 0 before calling playAction, then fade in
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: $"loading music file",
                    showUserMessage: false
                );
            }
        }

        private void SwapMediaPlayers()
        {
            if (_preloadedMediaPlayer == null) return;
            
            // Swap references (matching PNS exactly - no volume manipulation)
            var temp = _mediaPlayer;
            _mediaPlayer = _preloadedMediaPlayer;
            _preloadedMediaPlayer = temp;
            
            var tempTimeline = _timeLine;
            _timeLine = _preloadedTimeLine;
            _preloadedTimeLine = tempTimeline;
        }

        private void DisposePreloaded()
        {
            if (_preloadedMediaPlayer == null) return;
            
            try
            {
                _preloadedMediaPlayer.MediaEnded -= OnMediaEnded;
                _preloadedMediaPlayer.MediaFailed -= OnMediaFailed;
                _preloadedMediaPlayer.Clock = null;
                _preloadedMediaPlayer.Close();
            }
            catch { }
            
            _preloadedMediaPlayer = null;
            _preloadedTimeLine = null;
            _preloadedFile = "";
        }

        public void Play()
        {
            Play(default(TimeSpan));
        }

        public void Play(TimeSpan startFrom)
        {
            try
            {
                if (_mediaPlayer?.Clock != null)
                {
                    // PNS pattern: Don't restore volume here - let the fader control volume completely
                    // The fader will set volume to 0 before calling playAction, then fade in
                    _mediaPlayer.Clock.Controller?.Resume();
                    
                    // Seek to specified position (or beginning if default)
                    TimeSpan seekPosition = startFrom != default(TimeSpan) ? startFrom : TimeSpan.Zero;
                    _mediaPlayer.Clock.Controller?.Seek(seekPosition, TimeSeekOrigin.BeginTime);
                }
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "playing music",
                    showUserMessage: false
                );
            }
        }

        public void Stop()
        {
            try
            {
                _mediaPlayer?.Clock?.Controller?.Stop();
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "stopping music",
                    showUserMessage: false
                );
            }
        }

        public void Pause()
        {
            try
            {
                _mediaPlayer?.Clock?.Controller?.Pause();
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "pausing music",
                    showUserMessage: false
                );
            }
        }

        public void Resume()
        {
            try
            {
                if (_mediaPlayer != null && IsLoaded && _mediaPlayer.Clock != null)
                {
                    // PNS pattern: Don't restore volume here - let the fader control volume completely
                    _mediaPlayer.Clock.Controller?.Resume();
                }
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "resuming music",
                    showUserMessage: false
                );
            }
        }

        public void Close()
        {
            try
            {
                if (_mediaPlayer != null)
                {
                    if (_mediaPlayer.Clock != null)
                    {
                        _mediaPlayer.Clock = null;
                    }
                    _mediaPlayer.Close();
                }
                DisposePreloaded();
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "closing music player",
                    showUserMessage: false
                );
            }
        }

        private void OnMediaEnded(object sender, EventArgs e)
        {
            MediaEnded?.Invoke(sender, e);
        }

        private void OnMediaFailed(object sender, ExceptionEventArgs e)
        {
            Logger.Error(e.ErrorException, $"Media failed: {e.ErrorException?.Message}");
            MediaFailed?.Invoke(sender, e);
        }
    }
}
