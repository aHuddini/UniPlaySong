using System;
using System.IO;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Dedicated audio preview player for the Amplify feature.
    /// Uses MusicPlayer (WPF MediaPlayer) for consistent volume behavior with the rest of the app.
    /// Completely separate from the main SDL2 playback service.
    /// </summary>
    public class AmplifyPreviewPlayer : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private MusicPlayer _player;
        private bool _isDisposed;

        private static void LogDebug(string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                Logger.Debug($"[AmplifyPreview] {message}");
            }
        }

        public AmplifyPreviewPlayer()
        {
            LogDebug("AmplifyPreviewPlayer created");
        }

        /// <summary>
        /// Play an audio file at the specified volume.
        /// Volume is 0.0 to 1.0, matching the add-on's volume setting scale.
        /// </summary>
        /// <param name="filePath">Path to the audio file</param>
        /// <param name="volume">Volume level (0.0 to 1.0)</param>
        public void Play(string filePath, double volume)
        {
            if (_isDisposed) return;

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Logger.Warn($"AmplifyPreviewPlayer: Cannot play - file not found: {filePath}");
                return;
            }

            try
            {
                // Stop any current playback first
                Stop();

                // Clamp volume to valid range
                volume = Math.Max(0.0, Math.Min(1.0, volume));

                LogDebug($"Playing {Path.GetFileName(filePath)} at volume {volume:F2}");

                // Create new player instance (same pattern as DownloadDialogViewModel)
                _player = new MusicPlayer();

                // Set volume BEFORE loading (important for WPF MediaPlayer)
                _player.Volume = volume;

                // Load and play
                _player.Load(filePath);
                _player.Play();

                LogDebug($"Playback started successfully at volume {volume:F2}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "AmplifyPreviewPlayer: Error starting playback");
                Stop();
            }
        }

        /// <summary>
        /// Stop current playback and release resources
        /// </summary>
        public void Stop()
        {
            if (_isDisposed) return;

            try
            {
                if (_player != null)
                {
                    _player.Stop();
                    _player.Close();
                    _player = null;
                }

                LogDebug("Playback stopped");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "AmplifyPreviewPlayer: Error stopping playback");
            }
        }

        /// <summary>
        /// Whether audio is currently playing
        /// </summary>
        public bool IsPlaying => _player?.IsActive ?? false;

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            LogDebug("Disposing AmplifyPreviewPlayer");
            Stop();
        }
    }
}
