using System;
using System.Windows.Media;
using NAudio.Wave;

using Playnite.SDK;
using UniPlaySong.Audio;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    /// <summary>
    /// NAudio-based music player with live effects support.
    /// Implements IMusicPlayer for integration with MusicPlaybackService.
    /// Used when LiveEffectsEnabled is true in settings.
    /// </summary>
    public class NAudioMusicPlayer : IMusicPlayer, IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "NAudioPlayer";
        private FileLogger _fileLogger;

        private AudioFileReader _audioFile;
        private WaveOutEvent _outputDevice;
        private EffectsChain _effectsChain;
        private VisualizationDataProvider _visualizationProvider;
        private SmoothVolumeSampleProvider _volumeProvider;
        private readonly SettingsService _settingsService;
        private bool _isDisposed;

        // Logical pause state. WaveOutEvent stays running to avoid stale-buffer blips.
        // The fader sets volume to 0 before "pausing" — the audio thread outputs silence.
        // Position is saved on pause and restored on resume so the song doesn't drift.
        private bool _logicallyPaused;
        private TimeSpan _pausedPosition;

        public event EventHandler MediaEnded;
        public event EventHandler<ExceptionEventArgs> MediaFailed;

        public double Volume
        {
            get => _volumeProvider?.Volume ?? 0;
            set
            {
                if (_volumeProvider != null)
                {
                    _fileLogger?.Debug($"[NAudio] Volume SET: {_volumeProvider.Volume:F4} → {value:F4} (instant, cancels ramp)");
                    _volumeProvider.Volume = (float)value;
                }
            }
        }

        public bool IsLoaded { get; private set; }

        // WaveOutEvent never pauses — it stays Playing while logically paused (volume=0).
        // IsActive returns true for both playing and logically-paused states so
        // MusicPlaybackService takes the fader.Resume() path instead of musicPlayer.Play().
        public bool IsActive => _outputDevice?.PlaybackState == PlaybackState.Playing;

        public TimeSpan? CurrentTime => _audioFile?.CurrentTime;

        public string Source { get; private set; }

        public NAudioMusicPlayer(SettingsService settingsService, FileLogger fileLogger = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _fileLogger = fileLogger;
        }

        public void PreLoad(string filePath)
        {
            // Preloading not implemented in v1 - placeholder for future dual-player implementation
            // For now, this is a no-op. The file will be loaded fresh when Load() is called.
        }

        public void Load(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Logger.Warn($"[{LogPrefix}] Load called with null or empty file path");
                return;
            }

            try
            {
                Close();

                // Loading music file

                _audioFile = new AudioFileReader(filePath);
                _effectsChain = new EffectsChain(_audioFile, _settingsService);
                int fftSize = _settingsService.Current?.VizFftSize ?? 1024;
                _visualizationProvider = new VisualizationDataProvider(_effectsChain, fftSize, _settingsService.Current);
                VisualizationDataProvider.Current = _visualizationProvider;
                _volumeProvider = new SmoothVolumeSampleProvider(_visualizationProvider);

                _outputDevice = new WaveOutEvent();
                _outputDevice.PlaybackStopped += OnPlaybackStopped;
                _outputDevice.Init(_volumeProvider);

                Source = filePath;
                IsLoaded = true;

                // Loaded successfully
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Failed to load: {filePath}");
                Close();
                MediaFailed?.Invoke(this, null);
            }
        }

        public void Play()
        {
            try
            {
                // If the audio has reached the end, seek back to the beginning before playing
                // This is necessary for looping - NAudio doesn't auto-reset position on Play()
                if (_audioFile != null && _audioFile.CurrentTime >= _audioFile.TotalTime - TimeSpan.FromMilliseconds(100))
                {
                    _audioFile.CurrentTime = TimeSpan.Zero;
                }
                _outputDevice?.Play();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Failed to play");
                MediaFailed?.Invoke(this, null);
            }
        }

        public void Play(TimeSpan startFrom)
        {
            try
            {
                if (_audioFile != null && startFrom > TimeSpan.Zero)
                {
                    _audioFile.CurrentTime = startFrom;
                }
                _outputDevice?.Play();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Failed to play from {startFrom}");
                MediaFailed?.Invoke(this, null);
            }
        }

        // Logical pause — WaveOutEvent keeps running, outputting silence (volume is already 0
        // from the fader's fade-out). This avoids stale pre-rendered buffers that cause audio
        // blips when WaveOutEvent.Pause()/Play() is used.
        // Saves current position so resume can seek back (song advances at vol=0 otherwise).
        public void Pause()
        {
            _pausedPosition = _audioFile?.CurrentTime ?? TimeSpan.Zero;
            _fileLogger?.Debug($"[NAudio] Pause() — logical pause, pos={_pausedPosition}, playbackState={_outputDevice?.PlaybackState}, vol={_volumeProvider?.Volume:F4}");
            _logicallyPaused = true;
        }

        // Logical resume — seeks back to saved position, then the fader ramps volume up.
        public void Resume()
        {
            _fileLogger?.Debug($"[NAudio] Resume() — seeking to {_pausedPosition}, playbackState={_outputDevice?.PlaybackState}, vol={_volumeProvider?.Volume:F4}, isRamping={_volumeProvider?.IsRamping}");
            if (_audioFile != null && _logicallyPaused)
            {
                _audioFile.CurrentTime = _pausedPosition;
            }
            _logicallyPaused = false;
        }

        public void Stop()
        {
            try
            {
                if (_outputDevice != null)
                {
                    // Remove handler BEFORE stopping to prevent MediaEnded being fired
                    _outputDevice.PlaybackStopped -= OnPlaybackStopped;
                    _outputDevice.Stop();
                    // Re-attach handler for future playback
                    _outputDevice.PlaybackStopped += OnPlaybackStopped;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Failed to stop");
            }
        }

        public void Close()
        {
            try
            {
                if (_outputDevice != null)
                {
                    _outputDevice.PlaybackStopped -= OnPlaybackStopped;
                    _outputDevice.Stop();
                    _outputDevice.Dispose();
                    _outputDevice = null;
                }

                _effectsChain = null;
                if (VisualizationDataProvider.Current == _visualizationProvider)
                    VisualizationDataProvider.Current = null;
                _visualizationProvider?.Dispose();
                _visualizationProvider = null;
                _volumeProvider = null;

                if (_audioFile != null)
                {
                    _audioFile.Dispose();
                    _audioFile = null;
                }

                IsLoaded = false;
                Source = null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Error during close");
            }
        }

        public void SetVolumeRamp(double targetVolume, double durationSeconds)
        {
            _fileLogger?.Debug($"[NAudio] SetVolumeRamp({targetVolume:F4}, {durationSeconds:F3}s) — before: {_volumeProvider?.DiagSnapshot}");
            _volumeProvider?.SetTargetWithRamp((float)targetVolume, (float)durationSeconds);
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Logger.Error(e.Exception, $"[{LogPrefix}] Playback stopped with error");
                MediaFailed?.Invoke(this, null);
            }
            else if (_audioFile != null && _audioFile.CurrentTime >= _audioFile.TotalTime - TimeSpan.FromMilliseconds(100))
            {
                // Song reached the end (with small tolerance for timing)
                MediaEnded?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                Close();
                _isDisposed = true;
            }
        }
    }
}
