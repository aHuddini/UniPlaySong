using System;
using System.Collections.Generic;

namespace UniPlaySong.Services
{
    public interface IDashboardPlaybackService
    {
        void Play(string filePath);
        void PlayList(List<string> songPaths, int startIndex = 0);
        void Stop();
        void Pause();
        void Resume();

        bool IsPlaying { get; }
        bool IsPaused { get; }
        bool IsActive { get; }
        bool IsRadioMode { get; }
        TimeSpan? CurrentTime { get; }
        TimeSpan? TotalTime { get; }
        string CurrentSongPath { get; }
        double Volume { get; set; }

        void StartRadio(List<string> songPool);
        void StopRadio();
        void SkipNext();
        void SkipPrevious();

        void PauseForSystem();
        void ResumeFromSystem();

        event Action OnPlaybackStateChanged;
        event Action OnSongEnded;
        event Action<string> OnSongChanged;

        void Dispose();
    }
}
