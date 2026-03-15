using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using UniPlaySong.Services;

namespace DashboardTestApp
{
    // Standalone dashboard playback service for testing.
    // Uses NAudio WaveOutEvent directly (no SettingsService dependency).
    public class TestDashboardPlaybackService : IDashboardPlaybackService
    {
        private WaveOutEvent _outputDevice;
        private AudioFileReader _audioFile;
        private bool _isActive;
        private bool _isRadioMode;
        private bool _isSystemPaused;
        private bool _isUserPaused;
        private List<string> _radioPool;
        private List<string> _playlist;
        private int _playlistIndex;
        private Random _random = new Random();
        private string _currentSongPath;
        private double _volume = 0.5;

        public bool IsPlaying => _outputDevice?.PlaybackState == PlaybackState.Playing && !_isUserPaused && !_isSystemPaused;
        public bool IsPaused => _isUserPaused || _isSystemPaused;
        public bool IsActive => _isActive;
        public bool IsRadioMode => _isRadioMode;
        public TimeSpan? CurrentTime => _audioFile?.CurrentTime;
        public TimeSpan? TotalTime => _audioFile?.TotalTime;
        public string CurrentSongPath => _currentSongPath;

        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Max(0, Math.Min(1.0, value));
                if (_outputDevice != null) _outputDevice.Volume = (float)_volume;
            }
        }

        public event Action OnPlaybackStateChanged;
        public event Action OnSongEnded;
        public event Action<string> OnSongChanged;

        public void Play(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            try
            {
                // Stop previous
                if (_audioFile != null)
                {
                    _outputDevice?.Stop();
                    _audioFile?.Dispose();
                    _audioFile = null;
                }

                if (_outputDevice == null)
                {
                    _outputDevice = new WaveOutEvent();
                    _outputDevice.PlaybackStopped += OnPlaybackStopped;
                }

                _audioFile = new AudioFileReader(filePath);
                _outputDevice.Init(_audioFile);
                _outputDevice.Volume = (float)_volume;
                _outputDevice.Play();

                _currentSongPath = filePath;
                _isActive = true;
                _isUserPaused = false;
                _isSystemPaused = false;

                System.Diagnostics.Debug.WriteLine($"[TestDashboard] Playing: {Path.GetFileName(filePath)} vol={_volume:F2}");

                OnSongChanged?.Invoke(filePath);
                OnPlaybackStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TestDashboard] Error playing: {ex.Message}");
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

                _outputDevice?.Stop();
                _audioFile?.Dispose();
                _audioFile = null;
                _isActive = false;
                _isUserPaused = false;
                _isSystemPaused = false;

                OnPlaybackStateChanged?.Invoke();
                System.Diagnostics.Debug.WriteLine("[TestDashboard] Stopped");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TestDashboard] Error stopping: {ex.Message}");
            }
        }

        public void Pause()
        {
            _isUserPaused = true;
            _outputDevice?.Pause();
            OnPlaybackStateChanged?.Invoke();
            System.Diagnostics.Debug.WriteLine("[TestDashboard] Paused");
        }

        public void Resume()
        {
            _isUserPaused = false;
            _outputDevice?.Play();
            OnPlaybackStateChanged?.Invoke();
            System.Diagnostics.Debug.WriteLine("[TestDashboard] Resumed");
        }

        public void StartRadio(List<string> songPool)
        {
            if (songPool == null || songPool.Count == 0) return;
            _radioPool = songPool;
            _isRadioMode = true;
            _playlist = null;
            Play(_radioPool[_random.Next(_radioPool.Count)]);
            System.Diagnostics.Debug.WriteLine($"[TestDashboard] Radio started, {songPool.Count} songs");
        }

        public void StopRadio()
        {
            _isRadioMode = false;
            _radioPool = null;
            Stop();
        }

        public void SkipNext()
        {
            if (_isRadioMode && _radioPool != null && _radioPool.Count > 0)
                Play(_radioPool[_random.Next(_radioPool.Count)]);
            else if (_playlist != null && _playlistIndex < _playlist.Count - 1)
            {
                _playlistIndex++;
                Play(_playlist[_playlistIndex]);
            }
        }

        public void PauseForSystem()
        {
            if (!_isActive || _isSystemPaused) return;
            _isSystemPaused = true;
            _outputDevice?.Pause();
            OnPlaybackStateChanged?.Invoke();
        }

        public void ResumeFromSystem()
        {
            if (!_isActive || !_isSystemPaused) return;
            _isSystemPaused = false;
            if (!_isUserPaused) _outputDevice?.Play();
            OnPlaybackStateChanged?.Invoke();
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            // Song ended naturally
            if (_audioFile != null && _audioFile.CurrentTime >= _audioFile.TotalTime - TimeSpan.FromMilliseconds(500))
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    if (_isRadioMode && _radioPool != null && _radioPool.Count > 0)
                        Play(_radioPool[_random.Next(_radioPool.Count)]);
                    else if (_playlist != null && _playlistIndex < _playlist.Count - 1)
                    {
                        _playlistIndex++;
                        Play(_playlist[_playlistIndex]);
                    }
                    else
                    {
                        OnSongEnded?.Invoke();
                        Stop();
                    }
                }));
            }
        }

        public void Dispose()
        {
            _outputDevice?.Stop();
            _audioFile?.Dispose();
            _outputDevice?.Dispose();
            _audioFile = null;
            _outputDevice = null;
        }
    }
}
