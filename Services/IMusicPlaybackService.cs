using System.Collections.Generic;
using Playnite.SDK.Models;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    // High-level music playback service for games
    public interface IMusicPlaybackService
    {
        void PlayGameMusic(Game game);
        void PlayGameMusic(Game game, UniPlaySongSettings settings);
        void PlayGameMusic(Game game, UniPlaySongSettings settings, bool forceReload);
        void Stop();
        void Pause();
        void Resume();

        void AddPauseSource(PauseSource source); // pauses if first source
        void RemovePauseSource(PauseSource source); // resumes if all sources cleared

        List<string> GetAvailableSongs(Game game);
        void SetVolume(double volume);
        double GetVolume();

        // Sets a volume multiplier on top of base volume (for Playnite fullscreen BackgroundVolume)
        void SetVolumeMultiplier(double multiplier);

        bool IsPlaying { get; }
        bool IsPaused { get; }
        bool IsLoaded { get; }

        void LoadAndPlayFile(string filePath);
        void LoadAndPlayFileFrom(string filePath, System.TimeSpan startFrom);

        void PlayPreview(string filePath, double volume); // no fading

        event System.Action<UniPlaySongSettings> OnMusicStopped;
        event System.Action<UniPlaySongSettings> OnMusicStarted;
        event System.Action OnSongEnded;
        event System.Action OnPlaybackStateChanged;

        bool SuppressAutoLoop { get; set; } // suppresses auto-loop on song end

        void SkipToNextSong();
        int CurrentGameSongCount { get; }
        event System.Action OnSongCountChanged;
        void RefreshSongCount();
        string CurrentSongPath { get; }
        Game CurrentGame { get; }
        event System.Action<string> OnSongChanged;

        bool IsPlayingDefaultMusic { get; } // true if playing default/fallback music
        bool IsPlayingBundledPreset { get; } // true if the current default music is a bundled preset (show metadata)

        // Called when app init is complete; processes any deferred playback if window state allows
        void MarkInitializationComplete();
    }
}

