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
        void Resume();
        void Close();
    }
}

