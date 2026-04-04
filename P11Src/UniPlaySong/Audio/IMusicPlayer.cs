namespace UniPlaySong.Audio;

interface IMusicPlayer : IAsyncDisposable
{
    // Lifecycle
    void Load(string filePath);
    void Play();
    void Play(TimeSpan startFrom);
    void Pause();
    void Resume();
    void Stop();
    void Close();

    // Volume
    void SetVolume(double volume);
    void SetVolumeRamp(double targetVolume, TimeSpan duration);
    bool IsVolumeRampComplete { get; }

    // State
    bool IsPlaying { get; }
    bool IsPaused { get; }
    bool IsLoaded { get; }
    string? CurrentFilePath { get; }
    TimeSpan CurrentTime { get; }
    TimeSpan TotalTime { get; }

    // Events
    event Action? OnSongEnded;
    event Action<Exception>? OnError;
}
