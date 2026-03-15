using System;
using System.Collections.Generic;
using System.IO;
using UniPlaySong.Audio;
using UniPlaySong.Common;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    // Independent playback service for the Music Dashboard.
    // Owns its own NAudioMusicPlayer instance, decoupled from the main playback service.
    // Coordinates with main service via PauseSource.Dashboard.
    public class DashboardPlaybackService : IDashboardPlaybackService
    {
        private readonly SettingsService _settingsService;
        private readonly IMusicPlaybackService _mainService;
        private readonly FileLogger _fileLogger;

        private NAudioMusicPlayer _player;
        private VisualizationDataProvider _savedMainVizProvider;
        private bool _isActive;
        private bool _isRadioMode;
        private bool _isSystemPaused;
        private bool _isUserPaused;
        private List<string> _radioPool;
        private List<string> _playlist;
        private int _playlistIndex;
        private Random _random = new Random();
        private string _currentSongPath;

        public bool IsPlaying => _player?.IsLoaded == true && !_isSystemPaused && !_isUserPaused;
        public bool IsPaused => (_isSystemPaused || _isUserPaused) && _player?.IsLoaded == true;
        public bool IsActive => _isActive;
        public bool IsRadioMode => _isRadioMode;
        public TimeSpan? CurrentTime => _player?.CurrentTime;
        public TimeSpan? TotalTime => _player?.TotalTime;
        public string CurrentSongPath => _currentSongPath;

        public double Volume
        {
            get => _player?.Volume ?? 1.0;
            set { if (_player != null) _player.Volume = value; }
        }

        public event Action OnPlaybackStateChanged;
        public event Action OnSongEnded;
        public event Action<string> OnSongChanged;

        public DashboardPlaybackService(
            SettingsService settingsService,
            IMusicPlaybackService mainService,
            FileLogger fileLogger = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _mainService = mainService;
            _fileLogger = fileLogger;
        }

        private void EnsurePlayer()
        {
            if (_player != null) return;

            _player = new NAudioMusicPlayer(_settingsService, _fileLogger);
            _player.MediaEnded += OnMediaEnded;
            _fileLogger?.Debug("DashboardPlayback: NAudioMusicPlayer created");
        }

        public void Play(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            try
            {
                EnsurePlayer();
                ActivateDashboard();

                _player.Load(filePath);
                _player.Volume = (_settingsService.Current?.MusicVolume ?? 50) / Constants.VolumeDivisor;
                _player.Play();
                _currentSongPath = filePath;
                _isSystemPaused = false;
                _isUserPaused = false;

                OnSongChanged?.Invoke(filePath);
                OnPlaybackStateChanged?.Invoke();

                _fileLogger?.Debug($"DashboardPlayback: Playing {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DashboardPlayback: Error playing: {ex.Message}");
            }
        }

        public void PlayList(List<string> songPaths, int startIndex = 0)
        {
            if (songPaths == null || songPaths.Count == 0) return;

            _playlist = songPaths;
            _playlistIndex = Math.Max(0, Math.Min(startIndex, songPaths.Count - 1));
            _isRadioMode = false;
            Play(_playlist[_playlistIndex]);
        }

        public void Stop()
        {
            try
            {
                _isRadioMode = false;
                _playlist = null;
                _currentSongPath = null;

                ReleasePlayer();
                DeactivateDashboard();

                OnPlaybackStateChanged?.Invoke();
                _fileLogger?.Debug("DashboardPlayback: Stopped");
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DashboardPlayback: Error stopping: {ex.Message}");
            }
        }

        private void ReleasePlayer()
        {
            try
            {
                if (_player != null)
                {
                    _player.MediaEnded -= OnMediaEnded;
                    _player.Close();
                    (_player as IDisposable)?.Dispose();
                    _player = null;
                    _fileLogger?.Debug("DashboardPlayback: Player released");
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DashboardPlayback: Error releasing player: {ex.Message}");
            }
        }

        private double _savedVolume;

        public void Pause()
        {
            try
            {
                _isUserPaused = true;
                _savedVolume = _player?.Volume ?? 0.5;
                if (_player != null) _player.Volume = 0;
                _player?.Pause();
                OnPlaybackStateChanged?.Invoke();
            }
            catch { }
        }

        public void Resume()
        {
            try
            {
                _isUserPaused = false;
                _player?.Resume();
                if (_player != null) _player.Volume = _savedVolume;
                OnPlaybackStateChanged?.Invoke();
            }
            catch { }
        }

        public void StartRadio(List<string> songPool)
        {
            if (songPool == null || songPool.Count == 0) return;

            _radioPool = songPool;
            _isRadioMode = true;
            _playlist = null;

            var index = _random.Next(_radioPool.Count);
            Play(_radioPool[index]);

            _fileLogger?.Debug($"DashboardPlayback: Radio started with {songPool.Count} songs");
        }

        public void StopRadio()
        {
            _isRadioMode = false;
            _radioPool = null;
            Stop();
        }

        public void SkipNext()
        {
            try
            {
                if (_isRadioMode && _radioPool != null && _radioPool.Count > 0)
                {
                    var index = _random.Next(_radioPool.Count);
                    Play(_radioPool[index]);
                }
                else if (_playlist != null && _playlistIndex < _playlist.Count - 1)
                {
                    _playlistIndex++;
                    Play(_playlist[_playlistIndex]);
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DashboardPlayback: Error skipping: {ex.Message}");
            }
        }

        public void SkipPrevious()
        {
            try
            {
                if (_playlist != null && _playlistIndex > 0)
                {
                    _playlistIndex--;
                    Play(_playlist[_playlistIndex]);
                }
                else if (_isRadioMode && _radioPool != null && _radioPool.Count > 0)
                {
                    // Radio mode: just play another random song
                    var index = _random.Next(_radioPool.Count);
                    Play(_radioPool[index]);
                }
                else
                {
                    // Single song: restart from beginning
                    if (_player?.IsLoaded == true)
                    {
                        _player.Stop();
                        _player.Play();
                    }
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DashboardPlayback: Error going previous: {ex.Message}");
            }
        }

        public void PauseForSystem()
        {
            if (!_isActive || _isSystemPaused) return;
            try
            {
                _isSystemPaused = true;
                if (!_isUserPaused)
                    _savedVolume = _player?.Volume ?? 0.5;
                if (_player != null) _player.Volume = 0;
                _player?.Pause();
                OnPlaybackStateChanged?.Invoke();
                _fileLogger?.Debug("DashboardPlayback: System pause");
            }
            catch { }
        }

        public void ResumeFromSystem()
        {
            if (!_isActive || !_isSystemPaused) return;
            try
            {
                _isSystemPaused = false;
                if (!_isUserPaused)
                {
                    _player?.Resume();
                    if (_player != null) _player.Volume = _savedVolume;
                }
                OnPlaybackStateChanged?.Invoke();
                _fileLogger?.Debug("DashboardPlayback: System resume");
            }
            catch { }
        }

        private void ActivateDashboard()
        {
            if (_isActive) return;

            _savedMainVizProvider = VisualizationDataProvider.Current;
            _mainService?.AddPauseSource(PauseSource.Dashboard);
            _isActive = true;

            _fileLogger?.Debug("DashboardPlayback: Activated — main player paused");
        }

        private void DeactivateDashboard()
        {
            if (!_isActive) return;

            VisualizationDataProvider.Current = _savedMainVizProvider;
            _savedMainVizProvider = null;
            _mainService?.RemovePauseSource(PauseSource.Dashboard);
            _isActive = false;
            _isSystemPaused = false;
            _isUserPaused = false;

            _fileLogger?.Debug("DashboardPlayback: Deactivated — main player resumed");
        }

        private void OnMediaEnded(object sender, EventArgs e)
        {
            try
            {
                if (_isRadioMode && _radioPool != null && _radioPool.Count > 0)
                {
                    var index = _random.Next(_radioPool.Count);
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        Play(_radioPool[index]);
                    }));
                }
                else if (_playlist != null && _playlistIndex < _playlist.Count - 1)
                {
                    _playlistIndex++;
                    var nextPath = _playlist[_playlistIndex];
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        Play(nextPath);
                    }));
                }
                else
                {
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        OnSongEnded?.Invoke();
                        Stop();
                    }));
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DashboardPlayback: Error in MediaEnded: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                ReleasePlayer();
                DeactivateDashboard();
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"DashboardPlayback: Error disposing: {ex.Message}");
            }
        }
    }
}
