using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Playnite;

namespace UniPlaySong.Audio;

class NAudioMusicPlayer : IMusicPlayer
{
    private static readonly ILogger _logger = LogManager.GetLogger();

    private readonly object _stateLock = new();
    private WaveOutEvent? _outputDevice;
    private MixingSampleProvider? _mixer;
    private WaveStream? _currentReader;
    private SmoothVolumeSampleProvider? _volumeProvider;
    private ISampleProvider? _mixerInput;

    private volatile bool _isPlaying;
    private volatile bool _isPaused;
    private volatile bool _isLoaded;
    private volatile bool _isInMixer;
    private bool _disposed;

    public bool IsPlaying => _isPlaying;
    public bool IsPaused => _isPaused;
    public bool IsLoaded => _isLoaded;
    public string? CurrentFilePath { get; private set; }

    public bool IsVolumeRampComplete => _volumeProvider is not { IsRamping: true };

    public TimeSpan CurrentTime
    {
        get
        {
            try { return _currentReader?.CurrentTime ?? TimeSpan.Zero; }
            catch { return TimeSpan.Zero; }
        }
    }

    public TimeSpan TotalTime
    {
        get
        {
            try { return _currentReader?.TotalTime ?? TimeSpan.Zero; }
            catch { return TimeSpan.Zero; }
        }
    }

    public event Action? OnSongEnded;
    public event Action<Exception>? OnError;

    public NAudioMusicPlayer()
    {
        InitializePersistentMixer();
    }

    private void InitializePersistentMixer()
    {
        try
        {
            _logger.Info("Persistent mixer: initializing (44100Hz stereo)");
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))
            {
                ReadFully = true
            };
            _outputDevice = new WaveOutEvent();
            _outputDevice.Init(_mixer);
            _outputDevice.Play();
            _logger.Info("Persistent mixer: running");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Persistent mixer: FAILED to initialize");
            OnError?.Invoke(ex);
        }
    }

    public void Load(string filePath)
    {
        lock (_stateLock)
        {
            RemoveFromMixer();
            DisposeCurrentReader();

            try
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                _logger.Info($"Load: {Path.GetFileName(filePath)} (format={ext})");
                _currentReader = CreateReader(filePath);
                _logger.Info($"Load: reader created (sampleRate={_currentReader.WaveFormat.SampleRate}, channels={_currentReader.WaveFormat.Channels}, duration={_currentReader.TotalTime.TotalSeconds:F1}s)");
                var resampled = EnsureCorrectFormat(_currentReader);
                _volumeProvider = new SmoothVolumeSampleProvider(resampled);
                _mixerInput = new EofDetectingSampleProvider(_volumeProvider, () =>
                {
                    _isPlaying = false;
                    _isInMixer = false;
                    _logger.Info($"EOF: {Path.GetFileName(filePath)}");
                    OnSongEnded?.Invoke();
                });

                CurrentFilePath = filePath;
                _isLoaded = true;
                _isPlaying = false;
                _isPaused = false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Load FAILED: {filePath}");
                CurrentFilePath = null;
                _isLoaded = false;
                OnError?.Invoke(ex);
            }
        }
    }

    public void Play()
    {
        lock (_stateLock)
        {
            if (!_isLoaded || _mixerInput == null || _mixer == null) return;
            _logger.Info($"Play: {Path.GetFileName(CurrentFilePath)}");
            AddToMixer();
            _isPlaying = true;
            _isPaused = false;
        }
    }

    public void Play(TimeSpan startFrom)
    {
        lock (_stateLock)
        {
            if (!_isLoaded || _currentReader == null) return;
            _logger.Info($"Play: {Path.GetFileName(CurrentFilePath)} from {startFrom.TotalSeconds:F1}s");
            try
            {
                _currentReader.CurrentTime = startFrom;
            }
            catch
            {
                _logger.Warn($"Play: seek to {startFrom.TotalSeconds:F1}s failed — playing from start");
            }
            Play();
        }
    }

    public void Pause()
    {
        lock (_stateLock)
        {
            if (!_isPlaying) return;
            _logger.Info($"Pause: {Path.GetFileName(CurrentFilePath)} at {CurrentTime.TotalSeconds:F1}s");
            RemoveFromMixer();
            _isPlaying = false;
            _isPaused = true;
        }
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            if (!_isPaused || !_isLoaded) return;
            _logger.Info($"Resume: {Path.GetFileName(CurrentFilePath)}");
            AddToMixer();
            _isPlaying = true;
            _isPaused = false;
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            _logger.Info($"Stop: {Path.GetFileName(CurrentFilePath)}");
            RemoveFromMixer();
            _isPlaying = false;
            _isPaused = false;
        }
    }

    public void Close()
    {
        lock (_stateLock)
        {
            _logger.Info($"Close: {Path.GetFileName(CurrentFilePath)}");
            RemoveFromMixer();
            DisposeCurrentReader();
            CurrentFilePath = null;
            _isLoaded = false;
            _isPlaying = false;
            _isPaused = false;
        }
    }

    public void SetVolume(double volume)
    {
        if (_volumeProvider != null)
            _volumeProvider.Volume = (float)Math.Clamp(volume, 0.0, 1.0);
    }

    public void SetVolumeRamp(double targetVolume, TimeSpan duration)
    {
        _volumeProvider?.SetTargetWithRamp(
            (float)Math.Clamp(targetVolume, 0.0, 1.0),
            (float)duration.TotalSeconds);
    }

    // Direct reader dispatch — skip AudioFileReader's trial-and-error
    private static WaveStream CreateReader(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ogg" => new OggFileReader(path),
            ".mp3" => new Mp3FileReader(path),
            ".wav" => new WaveFileReader(path),
            ".flac" => new MediaFoundationReader(path),
            _ => new AudioFileReader(path)
        };
    }

    // Ensure audio is 44100Hz stereo IEEE float for the mixer
    private static ISampleProvider EnsureCorrectFormat(WaveStream reader)
    {
        ISampleProvider sampleProvider;

        if (reader is ISampleProvider sp && reader.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            sampleProvider = sp;
        }
        else
        {
            sampleProvider = reader.ToSampleProvider();
        }

        if (sampleProvider.WaveFormat.SampleRate != 44100)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 44100);
        }

        if (sampleProvider.WaveFormat.Channels == 1)
        {
            sampleProvider = new MonoToStereoSampleProvider(sampleProvider);
        }

        return sampleProvider;
    }

    private void AddToMixer()
    {
        if (_isInMixer || _mixerInput == null || _mixer == null) return;
        _mixer.AddMixerInput(_mixerInput);
        _isInMixer = true;
    }

    private void RemoveFromMixer()
    {
        if (!_isInMixer || _mixerInput == null || _mixer == null) return;
        _mixer.RemoveMixerInput(_mixerInput);
        _isInMixer = false;
    }

    private void DisposeCurrentReader()
    {
        _currentReader?.Dispose();
        _currentReader = null;
        _volumeProvider = null;
        _mixerInput = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_stateLock)
        {
            RemoveFromMixer();
            DisposeCurrentReader();
        }

        _outputDevice?.Stop();
        _outputDevice?.Dispose();
        _outputDevice = null;
        _mixer = null;

        await ValueTask.CompletedTask;
    }

    // Detects EOF by watching for partial buffer reads from the source.
    // MixingSampleProvider auto-removes inputs on partial read, so we fire the event.
    private class EofDetectingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly Action _onEof;
        private bool _eofFired;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public EofDetectingSampleProvider(ISampleProvider source, Action onEof)
        {
            _source = source;
            _onEof = onEof;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (read < count && !_eofFired)
            {
                _eofFired = true;
                _onEof();
            }
            return read;
        }
    }
}
