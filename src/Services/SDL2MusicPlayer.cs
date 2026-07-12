using System;
using System.Collections.Generic;
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
        // issue #81: set when the device is released (idle/lock/suspend) while a song was loaded.
        // SDL frees the decoded music on teardown, so resume must reload from this path + seek back
        // (NAudio can just rebuild the mixer; SDL cannot). Consumed + cleared by ReloadFromDeviceRelease().
        private string _resumeReloadPath = string.Empty;
        private TimeSpan _resumeReloadPosition = TimeSpan.Zero;
        private static bool _isSDLAudioInitialized = false;
        // Mix_HookMusicFinished is ONE process-global hook. Installing it per-instance let the
        // last-constructed player (the prewarmed jingle player, since v1.5.10) steal it from the
        // main player, whose MediaEnded then never fired — music stopped after one song (issue #89).
        // Install the hook ONCE with a static callback that dispatches to every live instance;
        // each instance's OnMusicFinishedInternal gates on its own _isActive, and SDL2_mixer has
        // a single music stream, so at most one instance handles it.
        private static readonly object _instancesLock = new object();
        private static readonly List<SDL2MusicPlayer> _instances = new List<SDL2MusicPlayer>();
        private static SDL2Mixer.MusicFinishedCallback _staticMusicFinishedCallback; // rooted so GC never collects the native thunk
        private static bool _musicFinishedHookInstalled;
        private readonly ErrorHandlerService _errorHandler;
        private readonly SettingsService _settingsService;

        // Volume ramp state — SDL2 has no per-sample ramp, so we step via DispatcherTimer
        private DispatcherTimer _rampTimer;
        private double _rampTarget;
        private double _rampStartVolume;
        private DateTime _rampStartTime;
        private double _rampDuration;
        private const double RampIntervalMs = 16; // ~60 steps/sec

        // Only the MAIN player may release the process-wide SDL audio device (issue #81).
        // A transient jingle player is created with enableIdleTeardown:false and must not
        // close the device out from under the main player.
        private readonly bool _enableIdleTeardown;

        public event EventHandler MediaEnded;
        public event EventHandler<ExceptionEventArgs> MediaFailed;

        private static readonly ILogger Logger = LogManager.GetLogger();

        public SDL2MusicPlayer(ErrorHandlerService errorHandler = null, SettingsService settingsService = null, bool enableIdleTeardown = false)
        {
            _errorHandler = errorHandler;
            _settingsService = settingsService;
            _enableIdleTeardown = enableIdleTeardown;
            InitializeSDL();
            lock (_instancesLock)
            {
                _instances.Add(this);
                if (!_musicFinishedHookInstalled)
                {
                    _staticMusicFinishedCallback = OnMusicFinishedStatic;
                    SDL2Mixer.Mix_HookMusicFinished(Marshal.GetFunctionPointerForDelegate(_staticMusicFinishedCallback));
                    _musicFinishedHookInstalled = true;
                }
            }
        }

        // SDL2 invokes this on its audio thread whenever the (single, global) music stream ends.
        // Route to every live instance; the _isActive gate inside OnMusicFinishedInternal ensures
        // only the player that was actually playing dispatches MediaEnded.
        private static void OnMusicFinishedStatic()
        {
            SDL2MusicPlayer[] players;
            lock (_instancesLock)
            {
                players = _instances.ToArray();
            }
            foreach (var player in players)
            {
                player.OnMusicFinishedInternal();
            }
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

        // Closes the SDL audio device so Windows can sleep (idle/lock/suspend). Frees all
        // Mix_* handles that would dangle once the device is gone; next Load() reopens via InitializeSDL().
        private void TearDownSDLAudio(TimeSpan idleFor)
        {
            if (!_isSDLAudioInitialized) return;

            // Halt and free the currently-loaded (paused/stopped) song; all Mix_* handles dangle
            // once the device closes. The next Load() reloads it.
            if (_music != IntPtr.Zero)
            {
                SDL2Mixer.Mix_HaltMusic();
                SDL2Mixer.Mix_FreeMusic(_music);
                _music = IntPtr.Zero;
            }
            _isLoaded = false;
            _isActive = false;
            _source = string.Empty;

            // Free any preloaded handle too.
            if (_preloadedMusic != IntPtr.Zero)
            {
                SDL2Mixer.Mix_FreeMusic(_preloadedMusic);
                _preloadedMusic = IntPtr.Zero;
                _preloadedPath = string.Empty;
            }

            SDL2Mixer.Mix_CloseAudio();
            _isSDLAudioInitialized = false;

            Logger.Debug($"SDL2 audio device closed so Windows can sleep");
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

                // issue #81: if the device was released and the music freed, reload it here (seeking to
                // the stashed position). ReloadFromDeviceRelease calls back into Play(pos), so return after.
                if (_music == IntPtr.Zero && !string.IsNullOrEmpty(_resumeReloadPath))
                {
                    ReloadFromDeviceRelease();
                    return;
                }

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

        public void Resume(Action onReady = null)
        {
            // issue #81: if the device was released and the music freed, reload from the stash + seek
            // back rather than resuming a nonexistent stream (which would come back silent).
            if (_music == IntPtr.Zero && !string.IsNullOrEmpty(_resumeReloadPath))
            {
                ReloadFromDeviceRelease();
                onReady?.Invoke();
                return;
            }

            if (!IsActive && _isLoaded)
            {
                SDL2Mixer.Mix_ResumeMusic();
                _isActive = true;
            }
            onReady?.Invoke();
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

        // --- IAudioDeviceHolder (issue #81) ---

        public bool IsAudioDeviceOpen => _isSDLAudioInitialized;

        public string AudioDeviceLabel => "MainPlayer(SDL2)";

        // Opens the process-wide SDL audio device ahead of first play. Idempotent — InitializeSDL()
        // is guarded by the static _isSDLAudioInitialized, so this is a cheap no-op when the shared
        // device is already open (the usual case, since SDL2 opens once for the whole process).
        public void PrewarmAudioDevice()
        {
            try
            {
                if (_isDisposed || _isSDLAudioInitialized) return;
                InitializeSDL();
            }
            catch (Exception ex)
            {
                Logger.Warn($"SDL2 PrewarmAudioDevice failed: {ex.Message}");
            }
        }

        // Releases the process-wide SDL audio device immediately (idle/lock/suspend). Only the
        // teardown-enabled (main) instance may close it — a secondary jingle player must not, or
        // it would kill audio for the main player. Idempotent; reopens lazily on next Load()/Play().
        public void ReleaseAudioDevice()
        {
            try
            {
                if (!_enableIdleTeardown) return;
                if (_isDisposed || !_isSDLAudioInitialized) return;

                // issue #81: if a song was loaded, stash its path + position so resume can reload it
                // (TearDownSDLAudio frees _music and clears _source/_isLoaded). Keep _isLoaded logically
                // true afterward so the playback service still routes resume to this player; Play()/Resume()
                // transparently reload from the stash when _music is null.
                bool hadSong = _isLoaded && !string.IsNullOrEmpty(_source);
                if (hadSong)
                {
                    _resumeReloadPath = _source;
                    _resumeReloadPosition = CurrentTime ?? TimeSpan.Zero;
                }

                TearDownSDLAudio(TimeSpan.Zero);

                if (hadSong)
                {
                    _isLoaded = true; // logical "loaded" — real reload deferred to next Play()/Resume()
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"SDL2 ReleaseAudioDevice failed: {ex.Message}");
            }
        }

        // issue #81: reloads a song that was freed by ReleaseAudioDevice, seeking back to the stashed
        // position. Returns true if a reload happened. Reopens the device lazily via Load().
        private bool ReloadFromDeviceRelease()
        {
            if (_music != IntPtr.Zero || string.IsNullOrEmpty(_resumeReloadPath)) return false;

            var path = _resumeReloadPath;
            var pos = _resumeReloadPosition;
            _resumeReloadPath = string.Empty;
            _resumeReloadPosition = TimeSpan.Zero;

            Load(path);
            Play(pos);
            return true;
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

                lock (_instancesLock)
                {
                    _instances.Remove(this);
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

