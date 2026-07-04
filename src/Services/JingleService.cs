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
        Abandoned,    // Game marked Abandoned
        Achievement   // Achievement/trophy unlocked — fired via URI by external plugins (e.g. Playnite Achievements)
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
        // Separate, deliberately lightweight factory for EXTERNAL notification sounds (achievement
        // unlocks fired via URI, etc.). Always a plain SDL2 player — never the NAudio Live-Effects
        // pipeline, whose per-fire persistent-layer setup added ~130ms latency and whose reverb/viz
        // machinery is pointless for a short notification "ding" fired over a running game. Kept
        // wholly separate from the regular jingle path so the completion/abandoned system is unaffected.
        private readonly Func<IMusicPlayer> _createLightweightPlayer;
        private readonly ErrorHandlerService _errorHandler;
        private readonly FileLogger _fileLogger;
        private readonly AudioDeviceRegistry _deviceRegistry; // issue #81

        private IMusicPlayer _jinglePlayer;      // regular jingles (completion/abandoned)
        private IMusicPlayer _externalPlayer;    // external notification sounds (achievement/URI)
        private VisualizationDataProvider _savedVizProvider;

        public JingleService(
            IMusicPlaybackService playbackService,
            Func<IMusicPlayer> createJinglePlayer,
            ErrorHandlerService errorHandler,
            FileLogger fileLogger = null,
            AudioDeviceRegistry deviceRegistry = null,
            Func<IMusicPlayer> createLightweightPlayer = null)
        {
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
            _createJinglePlayer = createJinglePlayer ?? throw new ArgumentNullException(nameof(createJinglePlayer));
            _errorHandler = errorHandler;
            _fileLogger = fileLogger;
            _deviceRegistry = deviceRegistry;
            // Falls back to the regular factory if a lightweight one isn't supplied (keeps behavior
            // for any caller that doesn't wire it), but the composition root always supplies SDL2.
            _createLightweightPlayer = createLightweightPlayer ?? createJinglePlayer;
        }

        // Plays the configured jingle for a given event. No-op if the feature
        // toggle is off, the sound type resolves to nothing, or the file is missing.
        public void PlayForEvent(JingleEvent evt, UniPlaySongSettings settings)
        {
            try
            {
                var maybeConfig = GetConfigForEvent(evt, settings);
                if (!maybeConfig.HasValue) return;
                var config = maybeConfig.Value;

                // SystemBeep: doesn't touch jingle player machinery — plays the Windows
                // system sound directly and returns. Main music is unaffected (no pause).
                if (config.SoundType == CelebrationSoundType.SystemBeep)
                {
                    System.Media.SystemSounds.Asterisk.Play();
                    return;
                }

                string path = null;
                if (config.SoundType == CelebrationSoundType.BundledJingle)
                {
                    path = BundledJingleService.ResolveJinglePath(config.JingleFilename);
                }
                else if (config.SoundType == CelebrationSoundType.CustomFile)
                {
                    if (!string.IsNullOrWhiteSpace(config.CustomFilePath)
                        && System.IO.File.Exists(config.CustomFilePath))
                    {
                        path = config.CustomFilePath;
                    }
                }

                if (string.IsNullOrEmpty(path)) return;

                // External notification sounds (achievement/URI) take the dedicated lightweight path;
                // regular celebration jingles (completion/abandoned) keep the existing effects-capable
                // path untouched.
                if (evt == JingleEvent.Achievement)
                    PlayExternalSound(path, settings);
                else
                    Play(path, settings);
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"JingleService.PlayForEvent({evt}) failed: {ex.Message}");
            }
        }

        private struct JingleSoundConfig
        {
            public CelebrationSoundType SoundType;
            public string JingleFilename;
            public string CustomFilePath;
        }

        // Returns null when the feature toggle for this event is off.
        private JingleSoundConfig? GetConfigForEvent(JingleEvent evt, UniPlaySongSettings settings)
        {
            switch (evt)
            {
                case JingleEvent.Completion:
                    if (settings?.EnableCompletionCelebration != true) return null;
                    return new JingleSoundConfig
                    {
                        SoundType = settings.CelebrationSoundType,
                        JingleFilename = settings.SelectedCelebrationJingle,
                        CustomFilePath = settings.CelebrationSoundPath
                    };

                case JingleEvent.Abandoned:
                    if (settings?.EnableAbandonedSound != true) return null;
                    return new JingleSoundConfig
                    {
                        SoundType = settings.AbandonedSoundType,
                        JingleFilename = settings.SelectedAbandonedJingle,
                        CustomFilePath = settings.AbandonedSoundPath
                    };

                case JingleEvent.Achievement:
                    if (settings?.EnableAchievementSound != true) return null;
                    return new JingleSoundConfig
                    {
                        SoundType = settings.AchievementSoundType,
                        JingleFilename = settings.SelectedAchievementJingle,
                        CustomFilePath = settings.AchievementSoundPath
                    };

                default:
                    return null;
            }
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
        private void Play(string filePath, UniPlaySongSettings settings)
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
                if (_jinglePlayer is IAudioDeviceHolder jh) _deviceRegistry?.Unregister(jh); // issue #81
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

        // Plays an EXTERNAL notification sound (achievement unlock via URI, etc.) on the dedicated
        // lightweight player. Intentionally NOT the regular jingle path:
        //   - no PauseForJingle/ResumeFromJingle — these fire over a running game where UPS music is
        //     already paused, so there's nothing to duck; the sound just plays alongside game audio.
        //   - no viz-provider save/restore — no Live Effects / visualizer involvement by design.
        //   - own player + own MediaEnded, fully isolated from the completion/abandoned jingle state.
        private void PlayExternalSound(string filePath, UniPlaySongSettings settings)
        {
            DisposeExternalPlayer(); // stop any still-playing external sound

            try
            {
                _externalPlayer = _createLightweightPlayer();
                _externalPlayer.MediaEnded += OnExternalEnded;
                _externalPlayer.Load(filePath);
                _externalPlayer.Volume = settings.MusicVolume / 100.0;
                _externalPlayer.Play();
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"External sound playback failed: {ex.Message}");
                DisposeExternalPlayer();
            }
        }

        private void OnExternalEnded(object sender, EventArgs e)
        {
            DisposeExternalPlayer();
        }

        private void DisposeExternalPlayer()
        {
            if (_externalPlayer != null)
            {
                if (_externalPlayer is IAudioDeviceHolder xh) _deviceRegistry?.Unregister(xh); // issue #81
                _externalPlayer.MediaEnded -= OnExternalEnded;
                _externalPlayer.Stop();
                _externalPlayer.Close();
                (_externalPlayer as IDisposable)?.Dispose();
                _externalPlayer = null;
            }
        }

        // Disposes any in-flight jingle/external players. Call from plugin shutdown.
        public void Cleanup()
        {
            DisposeJinglePlayer();
            DisposeExternalPlayer();
        }
    }
}
