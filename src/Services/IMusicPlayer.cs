using System;
using System.Windows.Media;

namespace UniPlaySong.Services
{
    // Interface for low-level music playback
    public interface IMusicPlayer
    {
        event EventHandler MediaEnded;
        event EventHandler<ExceptionEventArgs> MediaFailed;

        double Volume { get; set; } // 0.0 to 1.0

        bool IsLoaded { get; }
        bool IsActive { get; }
        TimeSpan? CurrentTime { get; }
        TimeSpan? TotalTime { get; }
        string Source { get; }

        // Preloads a media file into a separate player for seamless switching
        void PreLoad(string filePath);

        // Loads a media file (uses preloaded player if available)
        void Load(string filePath);

        void Play();

        // Starts playback from a specific position (default(TimeSpan) = from beginning)
        void Play(TimeSpan startFrom);

        void Stop();
        void Pause();

        // Resumes playback from the paused position.
        // The onReady callback (if provided) fires when the player is actually
        // ready to produce audio. For most players this is synchronous (fires before
        // Resume returns). For NAudio+GME it can be deferred to a background thread
        // because gme_seek() on a long track can take many seconds; doing that work
        // on the UI thread would freeze Playnite. Callers that want to start a
        // fade-in should do it from onReady, not after Resume returns.
        void Resume(Action onReady = null);

        void Close();

        // Sets a volume ramp from current volume to target over the given duration.
        // NAudio: per-sample interpolation on the audio thread (no stepping artifacts).
        // SDL2/WPF: DispatcherTimer stepping with exponential curve (preserves current behavior).
        void SetVolumeRamp(double targetVolume, double durationSeconds);
    }
}

