using System.Collections.Generic;
using Playnite.SDK.Models;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    /// <summary>
    /// High-level service for managing music playback for games
    /// </summary>
    public interface IMusicPlaybackService
    {
        /// <summary>
        /// Plays music for a game (console-like preview experience)
        /// </summary>
        void PlayGameMusic(Game game);

        /// <summary>
        /// Plays music for a game with settings check
        /// </summary>
        void PlayGameMusic(Game game, UniPlaySongSettings settings);
        
        /// <summary>
        /// Plays music for a game with settings check and optional force reload
        /// </summary>
        void PlayGameMusic(Game game, UniPlaySongSettings settings, bool forceReload);

        /// <summary>
        /// Stops current playback
        /// </summary>
        void Stop();

        /// <summary>
        /// Pauses current playback
        /// </summary>
        void Pause();

        /// <summary>
        /// Resumes paused playback
        /// </summary>
        void Resume();

        /// <summary>
        /// Gets available songs for a game
        /// </summary>
        List<string> GetAvailableSongs(Game game);

        /// <summary>
        /// Sets the volume (0.0 to 1.0)
        /// </summary>
        void SetVolume(double volume);

        /// <summary>
        /// Gets current volume
        /// </summary>
        double GetVolume();

        /// <summary>
        /// Whether music is currently playing
        /// </summary>
        bool IsPlaying { get; }
        
        /// <summary>
        /// Whether music is currently paused
        /// </summary>
        bool IsPaused { get; }
        
        /// <summary>
        /// Whether a media file is currently loaded (matches PlayniteSound's IsLoaded)
        /// </summary>
        bool IsLoaded { get; }
        
        /// <summary>
        /// Loads and plays a specific audio file
        /// </summary>
        void LoadAndPlayFile(string filePath);

        /// <summary>
        /// Loads and plays a specific audio file from a given position
        /// </summary>
        /// <param name="filePath">Path to the audio file</param>
        /// <param name="startFrom">Position to start playback from</param>
        void LoadAndPlayFileFrom(string filePath, System.TimeSpan startFrom);

        /// <summary>
        /// Plays a preview file at a specific volume using the same audio backend.
        /// Stops current playback, loads the file, sets volume directly (no fading), and plays.
        /// </summary>
        /// <param name="filePath">Path to the audio file</param>
        /// <param name="volume">Volume level (0.0 to 1.0)</param>
        void PlayPreview(string filePath, double volume);

        /// <summary>
        /// Event fired when music stops (for native music restoration)
        /// </summary>
        event System.Action<UniPlaySongSettings> OnMusicStopped;

        /// <summary>
        /// Event fired when music starts (for native music suppression)
        /// </summary>
        event System.Action<UniPlaySongSettings> OnMusicStarted;

        /// <summary>
        /// Event fired when a song reaches its natural end (before looping/randomizing).
        /// Used by batch download to queue next random game's music.
        /// </summary>
        event System.Action OnSongEnded;

        /// <summary>
        /// When true, suppresses the default loop/restart behavior when a song ends.
        /// Set by external handlers (like batch download) that want to take over playback.
        /// </summary>
        bool SuppressAutoLoop { get; set; }
    }
}

