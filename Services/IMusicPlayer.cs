using System;
using System.Windows.Media;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Interface for low-level music playback
    /// </summary>
    public interface IMusicPlayer
    {
        /// <summary>
        /// Raised when the current media ends
        /// </summary>
        event EventHandler MediaEnded;

        /// <summary>
        /// Raised when media playback fails
        /// </summary>
        event EventHandler<ExceptionEventArgs> MediaFailed;

        /// <summary>
        /// Volume level (0.0 to 1.0)
        /// </summary>
        double Volume { get; set; }

        /// <summary>
        /// Whether a media file is currently loaded
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// Whether media is currently playing
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Current playback position
        /// </summary>
        TimeSpan? CurrentTime { get; }

        /// <summary>
        /// Source file path of currently loaded media
        /// </summary>
        string Source { get; }

        /// <summary>
        /// Preloads a media file into a separate player for seamless switching
        /// </summary>
        void PreLoad(string filePath);

        /// <summary>
        /// Loads a media file (uses preloaded player if available)
        /// </summary>
        void Load(string filePath);

        /// <summary>
        /// Starts playback
        /// </summary>
        void Play();

        /// <summary>
        /// Starts playback from a specific position
        /// </summary>
        /// <param name="startFrom">Position to start playback from. Use default(TimeSpan) to start from beginning.</param>
        void Play(TimeSpan startFrom);

        /// <summary>
        /// Stops playback
        /// </summary>
        void Stop();

        /// <summary>
        /// Pauses playback
        /// </summary>
        void Pause();

        /// <summary>
        /// Resumes playback
        /// </summary>
        void Resume();

        /// <summary>
        /// Closes and releases resources
        /// </summary>
        void Close();
    }
}

