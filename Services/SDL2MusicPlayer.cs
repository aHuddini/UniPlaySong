using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Playnite.SDK;
using UniPlaySong.Players.SDL;
using UniPlaySong.Services;

namespace UniPlaySong.Services
{
    /// <summary>
    /// SDL2-based music player (matching PlayniteSound's implementation)
    /// Provides reliable volume control without WPF threading issues
    /// </summary>
    public class SDL2MusicPlayer : IMusicPlayer, IDisposable
    {
        // Instance variables
        private IntPtr _music = IntPtr.Zero;
        private IntPtr _preloadedMusic = IntPtr.Zero;
        private string _preloadedPath = string.Empty;
        private string _source = string.Empty;
        private bool _isDisposed = false;
        private bool _isActive = false;
        private bool _isLoaded = false;
        private double _volume = 1.0;
        private static bool _isSDLAudioInitialized = false;
        private SDL2Mixer.MusicFinishedCallback _musicFinishedCallback;
        private readonly ErrorHandlerService _errorHandler;

        public event EventHandler MediaEnded;
        public event EventHandler<ExceptionEventArgs> MediaFailed;

        private static readonly ILogger Logger = LogManager.GetLogger();

        public SDL2MusicPlayer(ErrorHandlerService errorHandler = null)
        {
            _errorHandler = errorHandler;
            InitializeSDL();
            _musicFinishedCallback = OnMusicFinishedInternal;
            SDL2Mixer.Mix_HookMusicFinished(Marshal.GetFunctionPointerForDelegate(_musicFinishedCallback));
            // SDL2 mixer initialized for playback
        }

        private void InitializeSDL()
        {
            if (!_isSDLAudioInitialized)
            {
                // Initialize SDL with audio support
                if (SDL2.SDL_Init(SDL2.SDL_INIT_AUDIO) < 0)
                {
                    throw new Exception($"SDL could not initialize! SDL Error: {SDL2.SDL_GetError()}");
                }

                // Initialize SDL_mixer
                if (SDL2Mixer.Mix_OpenAudio(44100, SDL2.MIX_DEFAULT_FORMAT, 2, 2048) < 0)
                {
                    throw new Exception($"SDL_mixer could not initialize! Mixer Error: {SDL2Mixer.GetMixError()}");
                }

                _isSDLAudioInitialized = true;
            }
        }

        public double Volume
        {
            get => _volume;
            set
            {
                // SDL2 uses 0-128 range, we use 0.0-1.0
                double oldVolume = _volume;
                _volume = Math.Max(0.0, Math.Min(1.0, value));
                int mixerVolume = (int)(_volume * 128);
                SDL2Mixer.Mix_VolumeMusic(mixerVolume);
            }
        }

        public bool IsLoaded => _isLoaded;

        public bool IsActive => _isActive && SDL2Mixer.Mix_PlayingMusic() == 1;

        public TimeSpan? CurrentTime
        {
            get
            {
                if (_errorHandler != null)
                {
                    return _errorHandler.Try<TimeSpan?>(
                        () =>
                        {
                            double pos = SDL2Mixer.Mix_GetMusicPosition(_music);
                            return TimeSpan.FromSeconds(pos);
                        },
                        defaultValue: null,
                        context: "getting SDL2 music player current time",
                        showUserMessage: false
                    );
                }
                try
                {
                    double pos = SDL2Mixer.Mix_GetMusicPosition(_music);
                    return TimeSpan.FromSeconds(pos);
                }
                catch
                {
                    return null;
                }
            }
        }

        public string Source => _source;

        public void PreLoad(string filePath)
        {
            EnsureNotDisposed();
            if (_preloadedMusic != IntPtr.Zero)
            {
                SDL2Mixer.Mix_FreeMusic(_preloadedMusic);
                _preloadedMusic = IntPtr.Zero;
                _preloadedPath = string.Empty;
            }

            _preloadedMusic = SDL2Mixer.Mix_LoadMUS(filePath);
            if (_preloadedMusic == IntPtr.Zero)
            {
                throw new Exception($"Failed to load music! SDL Error: {SDL2.SDL_GetError()}");
            }
            _preloadedPath = filePath;
        }

        public void Load(string filePath)
        {
            try
            {
                EnsureNotDisposed();

                Close();

                if (_preloadedMusic != IntPtr.Zero)
                {
                    if (_preloadedPath == filePath)
                    {
                        // Swap to preloaded music
                        if (_music != IntPtr.Zero)
                        {
                            SDL2Mixer.Mix_FreeMusic(_preloadedMusic);
                        }
                        _music = _preloadedMusic;
                        _preloadedMusic = IntPtr.Zero;
                    }
                    else
                    {
                        // Preloaded different file, free it
                        SDL2Mixer.Mix_FreeMusic(_preloadedMusic);
                        _preloadedMusic = IntPtr.Zero;
                        _preloadedPath = string.Empty;
                    }
                }
                
                if (_music == IntPtr.Zero)
                {
                    // Load fresh
                    _music = SDL2Mixer.Mix_LoadMUS(filePath);
                    if (_music == IntPtr.Zero)
                    {
                        throw new Exception($"Failed to load music! SDL Error: {SDL2.SDL_GetError()}");
                    }
                }

                _source = filePath;
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                _isLoaded = false;
                _errorHandler?.HandleError(
                    ex,
                    context: $"loading SDL2 music file",
                    showUserMessage: false
                );
                MediaFailed?.Invoke(this, null);
                throw;
            }
        }

        public void Play()
        {
            Play(default(TimeSpan));
        }

        public void Play(TimeSpan startFrom)
        {
            try
            {
                EnsureNotDisposed();
                if (!_isLoaded)
                {
                    throw new InvalidOperationException("No music loaded. Call Load() first.");
                }

                if (SDL2Mixer.Mix_PlayMusic(_music, 0) < 0)
                {
                    throw new Exception($"Failed to play music! SDL Error: {SDL2.SDL_GetError()}");
                }

                // Set position after starting playback (if specified)
                if (startFrom != default(TimeSpan))
                {
                    SDL2Mixer.Mix_SetMusicPosition(startFrom.TotalSeconds);
                }

                _isActive = true;
            }
            catch (Exception ex)
            {
                _isActive = false;
                _errorHandler?.HandleError(
                    ex,
                    context: "playing SDL2 music",
                    showUserMessage: false
                );
                MediaFailed?.Invoke(this, null);
            }
        }

        public void Pause()
        {
            if (IsActive)
            {
                SDL2Mixer.Mix_PauseMusic();
                _isActive = false;
            }
        }

        public void Resume()
        {
            if (!IsActive && _isLoaded)
            {
                SDL2Mixer.Mix_ResumeMusic();
                _isActive = true;
            }
        }

        public void Stop()
        {
            _isActive = false;
            SDL2Mixer.Mix_HaltMusic();
        }

        public void Close()
        {
            if (_music != IntPtr.Zero)
            {
                Stop();
                SDL2Mixer.Mix_FreeMusic(_music);
                _music = IntPtr.Zero;
            }

            _isLoaded = false;
            _isActive = false;
            _source = string.Empty;
        }

        private void OnMusicFinishedInternal()
        {
            if (_isActive)
            {
                _isActive = false;
                MediaEnded?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (_preloadedMusic != IntPtr.Zero)
            {
                SDL2Mixer.Mix_FreeMusic(_preloadedMusic);
                _preloadedMusic = IntPtr.Zero;
                _preloadedPath = string.Empty;
            }
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // Dispose managed state
                }

                Close();
                _isDisposed = true;
            }
        }

        ~SDL2MusicPlayer()
        {
            Dispose(false);
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(SDL2MusicPlayer));
            }
        }
    }
}

