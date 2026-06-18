using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using UniPlaySong.Common;
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
        private readonly SettingsService _settingsService;

        // Volume ramp state — SDL2 has no per-sample ramp, so we step via DispatcherTimer
        private DispatcherTimer _rampTimer;
        private double _rampTarget;
        private double _rampStartVolume;
        private DateTime _rampStartTime;
        private double _rampDuration;
        private const double RampIntervalMs = 16; // ~60 steps/sec

        // v1.5.3 idle-teardown timer (issue #81). SDL_mixer holds the audio
        // device open via Mix_OpenAudio for the whole process, which Windows
        // reads as an active audio session and refuses to autosuspend. When the
        // player sits idle (no song loaded/playing) past the user's configured
        // threshold, we Mix_CloseAudio to release the device; the next Load()
        // re-opens it via InitializeSDL().
        //
        // Only the MAIN player owns the teardown. The SDL audio device is
        // process-wide (guarded by the static _isSDLAudioInitialized), so a
        // transient jingle player must NOT tear it down out from under the main
        // player — it's created with enableIdleTeardown:false.
        private DispatcherTimer _idleTeardownTimer;
        private readonly bool _enableIdleTeardown;
        private DateTime _lastActivityUtc = DateTime.UtcNow;

        public event EventHandler MediaEnded;
        public event EventHandler<ExceptionEventArgs> MediaFailed;

        private static readonly ILogger Logger = LogManager.GetLogger();

        public SDL2MusicPlayer(ErrorHandlerService errorHandler = null, SettingsService settingsService = null, bool enableIdleTeardown = false)
        {
            _errorHandler = errorHandler;
            _settingsService = settingsService;
            _enableIdleTeardown = enableIdleTeardown;
            InitializeSDL();
            _musicFinishedCallback = OnMusicFinishedInternal;
            SDL2Mixer.Mix_HookMusicFinished(Marshal.GetFunctionPointerForDelegate(_musicFinishedCallback));
            if (_enableIdleTeardown)
                StartIdleTeardownTimer();
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

                // v1.5.3 (issue #81) — explicit Windows power-state opt-out.
                // Same call as the NAudio backend uses; clears any keep-alive
                // assertions UPS might be associated with. The underlying
                // Mix_OpenAudio still holds an audio session open, but UPS
                // isn't contributing to that on top.
                PowerStateHelper.OptOutOfKeepAwake();

                _isSDLAudioInitialized = true;
            }

            MarkAudioActivity();
        }

        // v1.5.3 (issue #81): start the idle-teardown timer. Polls every minute;
        // on each tick, if the player has been idle past the user's configured
        // threshold, closes the SDL audio device so Windows can autosuspend.
        private void StartIdleTeardownTimer()
        {
            if (_idleTeardownTimer != null) return;
            _idleTeardownTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _idleTeardownTimer.Tick += OnIdleTeardownTick;
            _idleTeardownTimer.Start();
        }

        private void StopIdleTeardownTimer()
        {
            if (_idleTeardownTimer == null) return;
            _idleTeardownTimer.Stop();
            _idleTeardownTimer.Tick -= OnIdleTeardownTick;
            _idleTeardownTimer = null;
        }

        // Resets the idle countdown. Called on InitializeSDL and on Load — any
        // real playback activity keeps the device alive.
        private void MarkAudioActivity()
        {
            _lastActivityUtc = DateTime.UtcNow;
        }

        private void OnIdleTeardownTick(object sender, EventArgs e)
        {
            try
            {
                if (_isDisposed || !_isSDLAudioInitialized) return;

                var minutes = _settingsService?.Current?.IdleAudioDeviceTeardownMinutes ?? 5;
                if (minutes <= 0) return; // user disabled the feature

                // Only tear down if genuinely idle: nothing loaded, nothing playing.
                if (_isLoaded || _music != IntPtr.Zero) { MarkAudioActivity(); return; }
                if (SDL2Mixer.Mix_PlayingMusic() == 1 || SDL2Mixer.Mix_PausedMusic() == 1) { MarkAudioActivity(); return; }

                var idleFor = DateTime.UtcNow - _lastActivityUtc;
                if (idleFor.TotalMinutes < minutes) return;

                TearDownSDLAudio(idleFor);
            }
            catch (Exception ex)
            {
                Logger.Warn($"SDL2 idle teardown tick failed: {ex.Message}");
            }
        }

        // Closes the SDL audio device, releasing the Windows audio session so the
        // system can autosuspend. Leaves the SDL audio subsystem initialized; the
        // static _isSDLAudioInitialized flag is reset so the next InitializeSDL()
        // re-opens the device via Mix_OpenAudio.
        private void TearDownSDLAudio(TimeSpan idleFor)
        {
            if (!_isSDLAudioInitialized) return;

            // Free any preloaded handle first — it would dangle once the device closes.
            if (_preloadedMusic != IntPtr.Zero)
            {
                SDL2Mixer.Mix_FreeMusic(_preloadedMusic);
                _preloadedMusic = IntPtr.Zero;
                _preloadedPath = string.Empty;
            }

            SDL2Mixer.Mix_CloseAudio();
            _isSDLAudioInitialized = false;

            Logger.Debug($"SDL2 idle teardown: {idleFor.TotalMinutes:F1}min idle — closed audio device so Windows can sleep");
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

        public TimeSpan? TotalTime =>
            _music != IntPtr.Zero
                ? TimeSpan.FromSeconds(SDL2Mixer.Mix_MusicDuration(_music))
                : (TimeSpan?)null;

        public string Source => _source;

        public void PreLoad(string filePath)
        {
            EnsureNotDisposed();
            InitializeSDL(); // re-opens the device if the idle timer tore it down
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
                InitializeSDL(); // re-opens the device if the idle timer tore it down

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
                MarkAudioActivity();
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

        public void Resume(Action onReady = null)
        {
            if (!IsActive && _isLoaded)
            {
                // If the audio device was closed (e.g., ReleaseAudioDevice() on system
                // suspend), reopen it first. After a close+reopen the SDL pause state is
                // gone, so we restart via Mix_PlayMusic rather than Mix_ResumeMusic.
                bool deviceWasClosed = !_isSDLAudioInitialized;
                InitializeSDL();

                if (deviceWasClosed)
                    SDL2Mixer.Mix_PlayMusic(_music, 0);
                else
                    SDL2Mixer.Mix_ResumeMusic();

                _isActive = true;
            }
            onReady?.Invoke();
        }

        // Immediately closes the SDL audio device so Windows can sleep (issue #81).
        // Called externally (e.g., on system suspend) without waiting for the idle timer.
        // The device is transparently re-opened by the next InitializeSDL() call (Load,
        // PreLoad, or Resume all call InitializeSDL at entry).
        public void ReleaseAudioDevice()
        {
            if (!_isSDLAudioInitialized) return;
            Logger.Debug("SDL2MusicPlayer: ReleaseAudioDevice — closing audio device for system suspend");
            TearDownSDLAudio(DateTime.UtcNow - _lastActivityUtc);
            StopIdleTeardownTimer();
        }

        public void Stop()
        {
            _isActive = false;
            SDL2Mixer.Mix_HaltMusic();
        }

        public void Close()
        {
            _rampTimer?.Stop();

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

        // Exponential-curve volume ramp matching the old fader's behavior.
        // Fade-in: progress^2 (starts fast, slows). Fade-out: 1-(1-progress)^2 (starts slow, speeds up).
        public void SetVolumeRamp(double targetVolume, double durationSeconds)
        {
            _rampTimer?.Stop();

            if (durationSeconds <= 0)
            {
                Volume = targetVolume;
                return;
            }

            _rampTarget = Math.Max(0.0, Math.Min(1.0, targetVolume));
            _rampStartVolume = _volume;
            _rampStartTime = DateTime.Now;
            _rampDuration = durationSeconds;

            if (_rampTimer == null)
            {
                _rampTimer = new DispatcherTimer(DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(RampIntervalMs)
                };
                _rampTimer.Tick += OnRampTick;
            }
            _rampTimer.Start();
        }

        private void OnRampTick(object sender, EventArgs e)
        {
            double elapsed = (DateTime.Now - _rampStartTime).TotalSeconds;
            double progress = Math.Min(1.0, elapsed / _rampDuration);

            bool fadingOut = _rampTarget < _rampStartVolume;
            double curvedVolume;

            if (fadingOut)
            {
                // Fade-out: exponential decay — 1-(1-progress)^2
                double curve = 1.0 - Math.Pow(1.0 - progress, 2.0);
                curvedVolume = _rampStartVolume * (1.0 - curve);
            }
            else
            {
                // Fade-in: exponential rise — progress^2
                double curve = Math.Pow(progress, 2.0);
                curvedVolume = _rampStartVolume + (_rampTarget - _rampStartVolume) * curve;
            }

            Volume = Math.Max(0.0, Math.Min(1.0, curvedVolume));

            if (progress >= 1.0)
            {
                Volume = _rampTarget;
                _rampTimer?.Stop();
            }
        }

        // Called on SDL2 audio callback thread when music finishes.
        // Must marshal MediaEnded to the UI thread — calling Mix_LoadMUS/Mix_PlayMusic
        // from within this callback crashes because SDL2 holds the audio device lock.
        private void OnMusicFinishedInternal()
        {
            if (_isActive)
            {
                _isActive = false;
                try
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher != null)
                    {
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            MediaEnded?.Invoke(this, EventArgs.Empty);
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "SDL2 OnMusicFinished: Error dispatching MediaEnded");
                }
            }
        }

        public void Dispose()
        {
            StopIdleTeardownTimer();
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

