using System;
using System.Windows.Media;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Interface for low-level music playback
    /// </summary>
    public interface IMusicPlayer
    {
        event EventHandler MediaEnded;
        event EventHandler<ExceptionEventArgs> MediaFailed;

        /// <summary>
        /// Volume level (0.0 to 1.0)
        /// </summary>
        double Volume { get; set; }

        bool IsLoaded { get; }
        bool IsActive { get; }
        TimeSpan? CurrentTime { get; }
        string Source { get; }

        /// <summary>
        /// Preloads a media file into a separate player for seamless switching
        /// </summary>
        void PreLoad(string filePath);

        /// <summary>
        /// Loads a media file (uses preloaded player if available)
        /// </summary>
        void Load(string filePath);

        void Play();

        /// <summary>
        /// Starts playback from a specific position
        /// </summary>
        /// <param name="startFrom">Position to start playback from. Use default(TimeSpan) to start from beginning.</param>
        void Play(TimeSpan startFrom);

        void Stop();
        void Pause();
        void Resume();
        void Close();
    }
}

