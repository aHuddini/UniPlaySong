using System;
using System.Windows.Media;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Playnite.SDK;
using UniPlaySong.Audio;

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

        private AudioFileReader _audioFile;
        private WaveOutEvent _outputDevice;
        private EffectsChain _effectsChain;
        private VisualizationDataProvider _visualizationProvider;
        private VolumeSampleProvider _volumeProvider;
        private readonly SettingsService _settingsService;
        private bool _isDisposed;

        public event EventHandler MediaEnded;
        public event EventHandler<ExceptionEventArgs> MediaFailed;

        public double Volume
        {
            get => _volumeProvider?.Volume ?? 0;
            set
            {
                if (_volumeProvider != null)
                    _volumeProvider.Volume = (float)value;
            }
        }

        public bool IsLoaded { get; private set; }

        public bool IsActive => _outputDevice?.PlaybackState == PlaybackState.Playing;

        public TimeSpan? CurrentTime => _audioFile?.CurrentTime;

        public string Source { get; private set; }

        public NAudioMusicPlayer(SettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
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

                Logger.Debug($"[{LogPrefix}] Loading: {System.IO.Path.GetFileName(filePath)}");

                _audioFile = new AudioFileReader(filePath);
                _effectsChain = new EffectsChain(_audioFile, _settingsService);
                _visualizationProvider = new VisualizationDataProvider(_effectsChain);
                VisualizationDataProvider.Current = _visualizationProvider;
                _volumeProvider = new VolumeSampleProvider(_visualizationProvider);

                _outputDevice = new WaveOutEvent();
                _outputDevice.PlaybackStopped += OnPlaybackStopped;
                _outputDevice.Init(_volumeProvider);

                Source = filePath;
                IsLoaded = true;

                Logger.Debug($"[{LogPrefix}] Loaded successfully: {_audioFile.TotalTime:mm\\:ss}, {_audioFile.WaveFormat.SampleRate}Hz, {_audioFile.WaveFormat.Channels}ch");
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
                    Logger.Debug($"[{LogPrefix}] Audio at end, seeking to beginning for loop");
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

        public void Pause()
        {
            try
            {
                _outputDevice?.Pause();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Failed to pause");
            }
        }

        public void Resume()
        {
            try
            {
                _outputDevice?.Play();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{LogPrefix}] Failed to resume");
                MediaFailed?.Invoke(this, null);
            }
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
                Logger.Debug($"[{LogPrefix}] Media ended");
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
