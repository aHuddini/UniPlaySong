using System.Collections.Generic;
using Playnite.SDK.Models;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    /// <summary>High-level music playback service for games</summary>
    public interface IMusicPlaybackService
    {
        void PlayGameMusic(Game game);
        void PlayGameMusic(Game game, UniPlaySongSettings settings);
        void PlayGameMusic(Game game, UniPlaySongSettings settings, bool forceReload);
        void Stop();
        void Pause();
        void Resume();

        /// <summary>Add pause source; pauses if first source</summary>
        void AddPauseSource(PauseSource source);

        /// <summary>Remove pause source; resumes if all cleared</summary>
        void RemovePauseSource(PauseSource source);

        List<string> GetAvailableSongs(Game game);
        void SetVolume(double volume);
        double GetVolume();

        /// <summary>
        /// Sets a volume multiplier applied on top of the base volume.
        /// Used to respect Playnite's fullscreen BackgroundVolume setting.
        /// </summary>
        void SetVolumeMultiplier(double multiplier);

        bool IsPlaying { get; }
        bool IsPaused { get; }
        bool IsLoaded { get; }

        void LoadAndPlayFile(string filePath);
        void LoadAndPlayFileFrom(string filePath, System.TimeSpan startFrom);

        /// <summary>Play preview at specific volume (no fading)</summary>
        void PlayPreview(string filePath, double volume);

        event System.Action<UniPlaySongSettings> OnMusicStopped;
        event System.Action<UniPlaySongSettings> OnMusicStarted;
        event System.Action OnSongEnded;
        event System.Action OnPlaybackStateChanged;

        /// <summary>When true, suppresses auto-loop on song end</summary>
        bool SuppressAutoLoop { get; set; }

        void SkipToNextSong();
        int CurrentGameSongCount { get; }
        event System.Action OnSongCountChanged;
        void RefreshSongCount();
        string CurrentSongPath { get; }
        Game CurrentGame { get; }
        event System.Action<string> OnSongChanged;

        /// <summary>True if playing default/fallback music (not game-specific)</summary>
        bool IsPlayingDefaultMusic { get; }

        /// <summary>
        /// Called when application initialization is complete (after CheckInitialWindowState).
        /// Processes any deferred playback request if window state allows.
        /// </summary>
        void MarkInitializationComplete();
    }
}

