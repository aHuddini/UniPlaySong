using System;
using Playnite.SDK;
using UniPlaySong.Audio;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Events that can trigger a jingle. Wire each one to a settings-driven
    // sound configuration via GetConfigForEvent.
    public enum JingleEvent
    {
        Completion,   // Game marked Completed (or Beaten, if CelebrateBeaten is on)
        Abandoned     // Game marked Abandoned
    }

    // Owns jingle playback lifecycle: creates a dedicated player for each fire,
    // coordinates pause/resume of the main music via IMusicPlaybackService,
    // and preserves the NAudio visualization provider across the jingle window.
    // UniPlaySong detects the Playnite event (OnItemUpdated); this service
    // decides whether/what/how to play.
    public class JingleService
    {
        private readonly IMusicPlaybackService _playbackService;
        private readonly Func<IMusicPlayer> _createJinglePlayer;
        private readonly ErrorHandlerService _errorHandler;
        private readonly FileLogger _fileLogger;

        private IMusicPlayer _jinglePlayer;
        private VisualizationDataProvider _savedVizProvider;

        public JingleService(
            IMusicPlaybackService playbackService,
            Func<IMusicPlayer> createJinglePlayer,
            ErrorHandlerService errorHandler,
            FileLogger fileLogger = null)
        {
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
            _createJinglePlayer = createJinglePlayer ?? throw new ArgumentNullException(nameof(createJinglePlayer));
            _errorHandler = errorHandler;
            _fileLogger = fileLogger;
        }

        // Plays the configured jingle for a given event. No-op if the feature
        // toggle is off, the sound type resolves to nothing, or the file is missing.
        public void PlayForEvent(JingleEvent evt, UniPlaySongSettings settings)
        {
            // Implementation arrives in Milestone 3.
        }

        private void OnJingleEnded(object sender, EventArgs e)
        {
            DisposeJinglePlayer();
            _playbackService?.ResumeFromJingle();
        }

        // Plays a jingle file. Pauses the main music via IMusicPlaybackService,
        // spawns a dedicated player via the factory delegate, saves the viz
        // provider (NAudio case) so it can be restored on cleanup, wires
        // MediaEnded to trigger resume of the main music.
        internal void Play(string filePath, UniPlaySongSettings settings)
        {
            // Stop any previous jingle that might still be playing
            DisposeJinglePlayer();

            // Pause the main music instantly (dedicated Jingle source, preserves position)
            _playbackService?.PauseForJingle();

            try
            {
                // Save the main player's viz provider before we create the jingle's player.
                // If the jingle player is NAudio, its Load() will overwrite the static
                // VisualizationDataProvider.Current with its own provider.
                _savedVizProvider = VisualizationDataProvider.Current;

                _jinglePlayer = _createJinglePlayer();
                _jinglePlayer.MediaEnded += OnJingleEnded;
                _jinglePlayer.Load(filePath);
                _jinglePlayer.Volume = settings.MusicVolume / 100.0;
                _jinglePlayer.Play();
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"Jingle playback failed: {ex.Message}");
                DisposeJinglePlayer();
                _playbackService?.ResumeFromJingle();
            }
        }

        private void DisposeJinglePlayer()
        {
            if (_jinglePlayer != null)
            {
                _jinglePlayer.MediaEnded -= OnJingleEnded;
                _jinglePlayer.Stop();
                _jinglePlayer.Close();
                (_jinglePlayer as IDisposable)?.Dispose();
                _jinglePlayer = null;

                // Restore the main player's visualization provider
                if (_savedVizProvider != null)
                {
                    VisualizationDataProvider.Current = _savedVizProvider;
                    _savedVizProvider = null;
                }
            }
        }

        // Disposes any in-flight jingle player. Call from plugin shutdown.
        public void Cleanup()
        {
            DisposeJinglePlayer();
        }
    }
}
