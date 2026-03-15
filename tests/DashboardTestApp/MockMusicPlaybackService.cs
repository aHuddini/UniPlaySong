using System;
using System.Collections.Generic;
using Playnite.SDK.Models;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace DashboardTestApp
{
    public class MockMusicPlaybackService : IMusicPlaybackService
    {
        private HashSet<PauseSource> _pauseSources = new HashSet<PauseSource>();

        public bool IsPlaying { get; set; }
        public bool IsPaused => _pauseSources.Count > 0;
        public bool IsLoaded { get; set; }
        public TimeSpan? CurrentTime { get; set; }
        public string CurrentSongPath { get; set; }
        public Game CurrentGame { get; set; }
        public int CurrentGameSongCount { get; set; }
        public bool IsPlayingDefaultMusic { get; set; }
        public bool IsPlayingBundledPreset { get; set; }
        public bool IsPlayingPoolBasedDefault { get; set; }
        public bool SuppressAutoLoop { get; set; }

        public HashSet<PauseSource> ActivePauseSources => _pauseSources;

        public event Action<global::UniPlaySong.UniPlaySongSettings> OnMusicStopped;
        public event Action<global::UniPlaySong.UniPlaySongSettings> OnMusicStarted;
        public event Action OnSongEnded;
        public event Action OnPlaybackStateChanged;
        public event Action OnSongCountChanged;
        public event Action<string> OnSongChanged;

        public void AddPauseSource(PauseSource source)
        {
            _pauseSources.Add(source);
            System.Diagnostics.Debug.WriteLine($"[MockMain] AddPauseSource({source})");
            OnPlaybackStateChanged?.Invoke();
        }

        public void RemovePauseSource(PauseSource source)
        {
            _pauseSources.Remove(source);
            System.Diagnostics.Debug.WriteLine($"[MockMain] RemovePauseSource({source})");
            OnPlaybackStateChanged?.Invoke();
        }

        public void ConvertPauseSource(PauseSource from, PauseSource to) { _pauseSources.Remove(from); _pauseSources.Add(to); }
        public void AddPauseSourceImmediate(PauseSource source) => AddPauseSource(source);
        public void RemovePauseSourceImmediate(PauseSource source) => RemovePauseSource(source);
        public void PlayGameMusic(Game game) { }
        public void PlayGameMusic(Game game, global::UniPlaySong.UniPlaySongSettings settings) { }
        public void PlayGameMusic(Game game, global::UniPlaySong.UniPlaySongSettings settings, bool forceReload) { }
        public void Stop() { IsPlaying = false; }
        public void Pause() { }
        public void Resume() { }
        public void PauseImmediate() { }
        public void ResumeImmediate() { }
        public void PauseForJingle() { }
        public void ResumeFromJingle() { }
        public List<string> GetAvailableSongs(Game game) => new List<string>();
        public void SetVolume(double volume) { }
        public double GetVolume() => 0.5;
        public void SetVolumeMultiplier(double multiplier) { }
        public void SetIdleVolumeMultiplier(double multiplier) { }
        public void SetDefaultSongPoolProvider(Func<global::UniPlaySong.DefaultMusicSource, global::UniPlaySong.UniPlaySongSettings, List<string>> provider) { }
        public void SetFilterActiveProvider(Func<bool> provider) { }
        public void SetRadioSongPoolProvider(Func<global::UniPlaySong.RadioMusicSource, global::UniPlaySong.UniPlaySongSettings, List<string>> provider) { }
        public void StartRadioPlayback(global::UniPlaySong.UniPlaySongSettings settings) { }
        public void StopRadioMode() { }
        public void ClearLastDefaultMusicPath() { }
        public void LoadAndPlayFile(string filePath) { }
        public void LoadAndPlayFileFrom(string filePath, TimeSpan startFrom) { }
        public void PlayPreview(string filePath, double volume) { }
        public void SkipToNextSong() { }
        public void RestartCurrentSong() { }
        public void RefreshSongCount() { }
        public void MarkInitializationComplete() { }
    }
}
