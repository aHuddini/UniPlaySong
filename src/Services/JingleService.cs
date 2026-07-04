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

        // Achievement/trophy unlocked — fired via URI by external plugins (e.g. Playnite Achievements).
        // Achievement is the MASTER/fallback sound; the five rarity tiers each have their own optional
        // sound and fall back to Achievement when not set. See ACHIEVEMENT_SOUND_INTEGRATION.md.
        Achievement,            // master / fallback (used when a rarity has no sound of its own)
        AchievementCommon,      // commonachievement     (bronze)
        AchievementUncommon,    // uncommonachievement   (silver)
        AchievementRare,        // rareachievement       (gold)
        AchievementUltraRare,   // ultrarareachievement  (platinum)
        AchievementCapstone     // capstoneachievement   (perfect / 100%)
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

        private IMusicPlayer _jinglePlayer;      // regular jingles (completion/abandoned) — PERSISTENT, reused
        private IMusicPlayer _externalPlayer;    // external notification sounds (achievement/URI)
        private VisualizationDataProvider _savedVizProvider;
        // Whether _jinglePlayer was built with the NAudio (Live-Effects) backend. Lets us reuse the
        // persistent jingle player across fires and only rebuild it when the backend actually changes.
        private bool _jinglePlayerUsesLiveEffects;
        // Reports whether jingles currently want the Live-Effects (NAudio) backend, so we know when a
        // rebuild is needed. Null -> assume no change (always reuse an existing player).
        private readonly Func<bool> _jingleWantsLiveEffects;

        public JingleService(
            IMusicPlaybackService playbackService,
            Func<IMusicPlayer> createJinglePlayer,
            ErrorHandlerService errorHandler,
            FileLogger fileLogger = null,
            AudioDeviceRegistry deviceRegistry = null,
            Func<IMusicPlayer> createLightweightPlayer = null,
            Func<bool> jingleWantsLiveEffects = null)
        {
            _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
            _createJinglePlayer = createJinglePlayer ?? throw new ArgumentNullException(nameof(createJinglePlayer));
            _errorHandler = errorHandler;
            _fileLogger = fileLogger;
            _deviceRegistry = deviceRegistry;
            // Falls back to the regular factory if a lightweight one isn't supplied (keeps behavior
            // for any caller that doesn't wire it), but the composition root always supplies SDL2.
            _createLightweightPlayer = createLightweightPlayer ?? createJinglePlayer;
            _jingleWantsLiveEffects = jingleWantsLiveEffects;
        }

        // Plays the configured jingle for a given event. No-op if the feature
        // toggle is off, the sound type resolves to nothing, or the file is missing.
        public void PlayForEvent(JingleEvent evt, UniPlaySongSettings settings)
        {
            try
            {
                // Per-rarity achievement events resolve to a FILE PATH via the selected sound pack
                // (Theme / PA Starter / Custom), falling back to the master sound. They bypass the
                // CelebrationSoundType config struct entirely and go straight to the lightweight path.
                if (IsRarityAchievementEvent(evt))
                {
                    if (settings?.EnableAchievementSound != true) return;

                    // Pack chain first (Theme/PA Starter/Custom) — yields a file path directly.
                    var rarityPath = ResolveAchievementRarityPath(RarityOf(evt), settings);
                    if (!string.IsNullOrEmpty(rarityPath))
                    {
                        PlayExternalSound(rarityPath, settings);
                        return;
                    }

                    // Nothing from the pack -> fall back to the master default sound (which may be a
                    // system beep, a bundled jingle, or a custom file).
                    var master = MasterAchievementConfig(settings);
                    if (!master.HasValue) return;
                    var mcfg = master.Value;
                    if (mcfg.SoundType == CelebrationSoundType.SystemBeep)
                    {
                        System.Media.SystemSounds.Asterisk.Play();
                        return;
                    }
                    var masterPath = ResolveConfigPath(mcfg);
                    if (!string.IsNullOrEmpty(masterPath)) PlayExternalSound(masterPath, settings);
                    return;
                }

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

                string path = ResolveConfigPath(config);
                if (string.IsNullOrEmpty(path)) return;

                // External notification sounds (achievement/URI, all rarities) take the dedicated
                // lightweight path; regular celebration jingles (completion/abandoned) keep the
                // existing effects-capable path untouched.
                if (IsAchievementEvent(evt))
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

                // Master / fallback achievement sound. Per-rarity events never reach here — they're
                // resolved to a file path via the sound pack in PlayForEvent (path-based, not config).
                case JingleEvent.Achievement:
                    return MasterAchievementConfig(settings);

                default:
                    return null;
            }
        }

        // Resolves a config to a playable file path (BundledJingle -> bundled path, CustomFile ->
        // the file if it exists). Returns null for SystemBeep or when nothing resolves.
        private static string ResolveConfigPath(JingleSoundConfig config)
        {
            if (config.SoundType == CelebrationSoundType.BundledJingle)
                return BundledJingleService.ResolveJinglePath(config.JingleFilename);

            if (config.SoundType == CelebrationSoundType.CustomFile
                && !string.IsNullOrWhiteSpace(config.CustomFilePath)
                && System.IO.File.Exists(config.CustomFilePath))
                return config.CustomFilePath;

            return null;
        }

        // The five per-rarity achievement events (excludes the master Achievement event).
        private static bool IsRarityAchievementEvent(JingleEvent evt)
        {
            switch (evt)
            {
                case JingleEvent.AchievementCommon:
                case JingleEvent.AchievementUncommon:
                case JingleEvent.AchievementRare:
                case JingleEvent.AchievementUltraRare:
                case JingleEvent.AchievementCapstone:
                    return true;
                default:
                    return false;
            }
        }

        // Maps a rarity achievement event to its lowercase rarity key.
        private static string RarityOf(JingleEvent evt)
        {
            switch (evt)
            {
                case JingleEvent.AchievementCommon:    return "common";
                case JingleEvent.AchievementUncommon:  return "uncommon";
                case JingleEvent.AchievementRare:      return "rare";
                case JingleEvent.AchievementUltraRare: return "ultrarare";
                case JingleEvent.AchievementCapstone:  return "capstone";
                default:                               return null;
            }
        }

        // Resolves a rarity to a sound-file path via the selected pack. Returns null when the pack
        // yields nothing for this rarity (caller then falls back to the master sound):
        //   Theme         -> theme audio/Achievements/{rarity}.* ?? PA Starter {rarity}
        //   PAStarterPack -> PA Starter {rarity}
        //   Custom        -> {rarity}AchievementSoundPath (if set + exists) ?? PA Starter {rarity}
        private string ResolveAchievementRarityPath(string rarity, UniPlaySongSettings settings)
        {
            if (string.IsNullOrEmpty(rarity)) return null;

            switch (settings.AchievementSoundPack)
            {
                case AchievementSoundPack.Theme:
                    var themePath = Common.PlayniteThemeHelper.FindThemeAchievementSound(rarity);
                    return !string.IsNullOrEmpty(themePath)
                        ? themePath
                        : BundledJingleService.GetPAStarterPackPath(rarity);

                case AchievementSoundPack.Custom:
                    var custom = CustomRarityPath(rarity, settings);
                    return !string.IsNullOrWhiteSpace(custom) && System.IO.File.Exists(custom)
                        ? custom
                        : BundledJingleService.GetPAStarterPackPath(rarity);

                case AchievementSoundPack.PAStarterPack:
                default:
                    return BundledJingleService.GetPAStarterPackPath(rarity);
            }
        }

        private static string CustomRarityPath(string rarity, UniPlaySongSettings settings)
        {
            switch (rarity)
            {
                case "common":    return settings.CommonAchievementSoundPath;
                case "uncommon":  return settings.UncommonAchievementSoundPath;
                case "rare":      return settings.RareAchievementSoundPath;
                case "ultrarare": return settings.UltraRareAchievementSoundPath;
                case "capstone":  return settings.CapstoneAchievementSoundPath;
                default:          return null;
            }
        }

        // True for the master achievement event and all five rarity events — these all use the
        // dedicated lightweight (SDL2) external-sound path, never the effects-capable jingle path.
        private static bool IsAchievementEvent(JingleEvent evt)
        {
            switch (evt)
            {
                case JingleEvent.Achievement:
                case JingleEvent.AchievementCommon:
                case JingleEvent.AchievementUncommon:
                case JingleEvent.AchievementRare:
                case JingleEvent.AchievementUltraRare:
                case JingleEvent.AchievementCapstone:
                    return true;
                default:
                    return false;
            }
        }

        // The master/fallback achievement sound, or null when it's disabled. A rarity with no sound
        // of its own resolves to this; if the master is also off, the whole event is a no-op.
        private JingleSoundConfig? MasterAchievementConfig(UniPlaySongSettings settings)
        {
            if (settings?.EnableAchievementSound != true) return null;
            return new JingleSoundConfig
            {
                SoundType = settings.AchievementSoundType,
                JingleFilename = settings.SelectedAchievementJingle,
                CustomFilePath = settings.AchievementSoundPath
            };
        }

        private void OnJingleEnded(object sender, EventArgs e)
        {
            // Keep the persistent jingle player (and its open audio device) alive for reuse — do NOT
            // dispose here. Just stop playback and restore the main player's viz provider.
            try { _jinglePlayer?.Stop(); } catch { }
            if (_savedVizProvider != null)
            {
                VisualizationDataProvider.Current = _savedVizProvider;
                _savedVizProvider = null;
            }
            _playbackService?.ResumeFromJingle();
        }

        // Plays a jingle file. Pauses the main music via IMusicPlaybackService,
        // spawns a dedicated player via the factory delegate, saves the viz
        // provider (NAudio case) so it can be restored on cleanup, wires
        // MediaEnded to trigger resume of the main music.
        private void Play(string filePath, UniPlaySongSettings settings)
        {
            // Pause the main music instantly (dedicated Jingle source, preserves position)
            _playbackService?.PauseForJingle();

            try
            {
                // Reuse the PERSISTENT jingle player across fires. Disposing it per-jingle (the old
                // behavior) tore down the NAudio audio device every time, so the next jingle paid a
                // ~700ms cold device-reopen whenever nothing else (e.g. Spotify) kept the endpoint warm.
                // We only rebuild when the backend must change (Live Effects toggled between fires).
                bool wantEffects = _jingleWantsLiveEffects?.Invoke() ?? _jinglePlayerUsesLiveEffects;
                if (_jinglePlayer != null && wantEffects != _jinglePlayerUsesLiveEffects)
                    DisposeJinglePlayer();

                if (_jinglePlayer == null)
                {
                    // Save the main player's viz provider before we create the jingle's player.
                    // If the jingle player is NAudio, its Load() will overwrite the static
                    // VisualizationDataProvider.Current with its own provider.
                    _savedVizProvider = VisualizationDataProvider.Current;

                    _jinglePlayer = _createJinglePlayer();
                    _jinglePlayerUsesLiveEffects = wantEffects;
                    _jinglePlayer.MediaEnded += OnJingleEnded;
                }
                else
                {
                    // Reusing: stop the previous jingle but keep the player + its open device alive.
                    _jinglePlayer.Stop();
                }

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

        // Builds the persistent jingle player and opens its audio device ahead of the first jingle,
        // so that first completion/abandoned sound doesn't pay the ~700ms cold endpoint-open. Safe to
        // call repeatedly (no-op if the player already exists with the right backend). Call at startup
        // and after idle-exit (issue #81). Never plays anything.
        public void PrewarmJinglePlayer()
        {
            try
            {
                bool wantEffects = _jingleWantsLiveEffects?.Invoke() ?? _jinglePlayerUsesLiveEffects;

                // Rebuild if the backend changed since last time; otherwise reuse.
                if (_jinglePlayer != null && wantEffects != _jinglePlayerUsesLiveEffects)
                    DisposeJinglePlayer();

                if (_jinglePlayer == null)
                {
                    _jinglePlayer = _createJinglePlayer();
                    _jinglePlayerUsesLiveEffects = wantEffects;
                    _jinglePlayer.MediaEnded += OnJingleEnded;
                }

                // Open the device now (no song). NAudio builds its persistent layer; SDL2 is a no-op
                // when its shared device is already open.
                (_jinglePlayer as IAudioDeviceHolder)?.PrewarmAudioDevice();
                _fileLogger?.Debug($"[Jingle] Prewarmed jingle player ({_jinglePlayer?.GetType().Name}, liveEffects={wantEffects})");
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"PrewarmJinglePlayer failed: {ex.Message}");
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
