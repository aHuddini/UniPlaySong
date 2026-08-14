using Playnite.SDK;
using Playnite.SDK.Events;
using UniPlaySong.Common;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Services
{
    // Handles external playback commands received via Playnite URI protocol.
    // URI format: playnite://uniplaysong/{command}[/{argument}]
    public class ExternalControlService
    {
        private readonly IMusicPlaybackService _playbackService;
        private readonly IActiveMediaService _activeMedia;
        private readonly IPlayniteAPI _api;
        private readonly JingleService _jingleService;
        private readonly System.Func<UniPlaySongSettings> _getSettings;
        private readonly System.Func<Spotify.SpotifyControlService> _getSpotify;
        private const string NotificationPrefix = "UniPlaySong_ExtCtrl";

        // Debounce window for ControlUp's controller-detected event (see HandleControlUp).
        //
        // 250ms, not the original 1000ms: that was sized for connect-event bursts only, from when the
        // URI was the sole entry point. The hotkey now reaches the same handler, and a full second of
        // deafness swallowed a deliberate hotkey press that landed shortly after a connect — plugging
        // a controller in and immediately pressing the hotkey is ordinary use, not a burst. 250ms is
        // still far longer than the millisecond-scale repeats a flaky USB port or Bluetooth re-pair
        // produces, and short enough that no press a person makes on purpose is lost.
        //
        // Not tied to the sound's length (~2s for the bundled clip): PlayExternalSound stops the
        // previous sound before starting the next, so a retrigger cuts the first off by design rather
        // than overlapping it.
        private const int ControlUpDebounceMs = 250;
        private readonly System.Diagnostics.Stopwatch _controlUpLastFire = new System.Diagnostics.Stopwatch();
        private readonly object _controlUpDebounceLock = new object();

        public ExternalControlService(
            IMusicPlaybackService playbackService,
            IActiveMediaService activeMedia,
            IPlayniteAPI api,
            JingleService jingleService = null,
            System.Func<UniPlaySongSettings> getSettings = null,
            System.Func<Spotify.SpotifyControlService> getSpotify = null)
        {
            _playbackService = playbackService;
            _activeMedia = activeMedia;
            _api = api;
            _jingleService = jingleService;
            _getSettings = getSettings;
            _getSpotify = getSpotify;
        }

        public void HandleCommand(PlayniteUriEventArgs args)
        {
            if (args.Arguments == null || args.Arguments.Length == 0)
            {
                Notify("No command specified");
                return;
            }

            var command = args.Arguments[0].ToLowerInvariant();

            switch (command)
            {
                // When Spotify is the active radio source, the URI pause/play must reach SPOTIFY (via the manual-pause hold, so
                // radio recompute and the external-audio detector won't auto-resume it) — UPS's own player is silent in that
                // mode. Integrations like FullReel rely on pause meaning "whatever UPS is playing stays quiet".
                case "play":
                    {
                        var spotify = _getSpotify?.Invoke();
                        if (spotify != null && spotify.IsSpotifyActive)
                            spotify.ManualResume();
                        _playbackService.NotifyManualStart();
                        // Explicit external play also clears a stale FocusLoss — with the caller's window (e.g. FullReel's WebView2)
                        // holding Win32 focus, OnApplicationActivate never fires for the main window, so Resume() alone would leave
                        // FocusLoss pinning playback paused.
                        _playbackService.RemovePauseSource(Models.PauseSource.FocusLoss);
                        _playbackService.Resume();
                    }
                    break;

                case "pause":
                    {
                        var spotify = _getSpotify?.Invoke();
                        if (spotify != null && spotify.IsSpotifyActive)
                            spotify.ManualPause();
                        _playbackService.Pause();
                    }
                    break;

                case "playpausetoggle":
                    _activeMedia.PlayPause();
                    break;

                case "next":
                case "skip":
                    // "skip" is a back-compat alias for "next" — source-aware since UPS's
                    // Next() calls SkipToNextSong() anyway, so behavior is unchanged for UPS.
                    _activeMedia.Next();
                    break;

                case "previous":
                    _activeMedia.Previous();
                    break;

                case "togglemute":
                    _activeMedia.ToggleMute();
                    break;

                case "restart":
                    _playbackService.RestartCurrentSong();
                    break;

                case "stop":
                    _playbackService.Stop();
                    break;

                case "volume":
                    HandleVolume(args.Arguments);
                    break;

                // Achievement/trophy unlock sound — fired by external plugins (e.g. Playnite
                // Achievements) via playnite://uniplaysong/playniteachievements/{rarity}, where
                // {rarity} is common | uncommon | rare | ultrarare | hidden | capstone. Namespaced under the
                // source plugin so other integrations can add their own path later. All rarities play the same achievement
                // sound for now (per-rarity override sounds are a planned follow-up). Plays on the dedicated jingle player, so
                // it works over a running game and no-ops when the achievement-sound setting is off.
                case "playniteachievements":
                    HandlePlayniteAchievement(args.Arguments);
                    break;

                // Controller detected — fired by ControlUp via playnite://uniplaysong/controlup/detecttrigger.
                // Namespaced under the source plugin, same as playniteachievements, so ControlUp can add
                // more events later without a second convention. Plays on the lightweight player and
                // no-ops when the ControlUp sound setting is off.
                case "controlup":
                    HandleControlUp(args.Arguments);
                    break;

                default:
                    Notify($"Unknown command \"{command}\"");
                    break;
            }
        }

        private void HandleVolume(string[] arguments)
        {
            if (arguments.Length < 2)
            {
                Notify("Volume requires a value (0-100)");
                return;
            }

            if (!int.TryParse(arguments[1], out int volume))
            {
                Notify($"Invalid volume value \"{arguments[1]}\"");
                return;
            }

            if (volume < 0 || volume > 100)
            {
                Notify("Volume must be between 0 and 100");
                return;
            }

            _playbackService.SetVolume(volume / Constants.VolumeDivisor);
        }

        // In-process entry point for other plugins (see UniPlaySong.TriggerExternalEvent). Routes to
        // the SAME handlers as the URI, so the debounce, the settings gate, and the unknown-event
        // behavior are shared rather than reimplemented — a second code path here would drift from
        // the URI's on the first change either one received.
        //
        // Returns whether the source was recognised, so a caller can fall back to the URI when
        // talking to an older UniPlaySong that lacks this method.
        public bool HandleExternalEvent(string source, string eventName)
        {
            switch ((source ?? string.Empty).ToLowerInvariant())
            {
                case "controlup":
                    HandleControlUp(new[] { "controlup", eventName });
                    return true;

                case "playniteachievements":
                    HandlePlayniteAchievement(new[] { "playniteachievements", eventName });
                    return true;

                default:
                    return false;
            }
        }

        private void HandleControlUp(string[] arguments)
        {
            // arguments[0] == "controlup"; arguments[1] == the event segment. Only "detecttrigger"
            // today — an unknown or missing segment no-ops rather than playing the wrong sound, so a
            // future ControlUp that adds events stays safe against this build.
            // ?. on the element too: the in-process entry point can pass a null event name, where the
            // URI path could only ever produce non-null segments.
            var evt = arguments != null && arguments.Length > 1
                ? arguments[1]?.ToLowerInvariant()
                : null;

            if (evt != "detecttrigger") return;

            // Controller connect events burst: a flaky USB port, a controller waking from sleep, or
            // Bluetooth re-pairing can fire this several times in a row, and ControlUp deliberately
            // doesn't throttle its side. Without this guard one physical reconnect becomes a stutter
            // of overlapping dings. Stopwatch, not DateTime — a clock change (NTP, DST) must not
            // swallow a real fire or wave a burst through.
            lock (_controlUpDebounceLock)
            {
                if (_controlUpLastFire.IsRunning
                    && _controlUpLastFire.ElapsedMilliseconds < ControlUpDebounceMs)
                    return;

                _controlUpLastFire.Restart();
            }

            _jingleService?.PlayForEvent(JingleEvent.ControllerDetected, _getSettings?.Invoke());
        }

        private void HandlePlayniteAchievement(string[] arguments)
        {
            // arguments[0] == "playniteachievements"; arguments[1] (optional) == the rarity tier
            // segment (Playnite Achievements' command names, lowercased):
            //   commonachievement | uncommonachievement | rareachievement | ultrarareachievement |
            //   hidden | capstoneachievement
            // Each maps to its own JingleEvent; the event resolves to that rarity's sound, or falls
            // back to the master achievement sound when the rarity has none. An unknown or missing
            // tier plays the master sound, so a newer PA that adds a tier still works.
            var tier = arguments != null && arguments.Length > 1
                ? arguments[1]?.ToLowerInvariant()
                : null;

            JingleEvent evt;
            switch (tier)
            {
                case "commonachievement":     evt = JingleEvent.AchievementCommon;    break;
                case "uncommonachievement":   evt = JingleEvent.AchievementUncommon;  break;
                case "rareachievement":       evt = JingleEvent.AchievementRare;      break;
                case "ultrarareachievement":  evt = JingleEvent.AchievementUltraRare; break;
                // PA sends "hidden" for this tier, not "hiddenachievement" — the developer named it
                // that way, so it does not follow the {rarity}achievement pattern of the others.
                case "hidden":                evt = JingleEvent.AchievementHidden;    break;
                case "capstoneachievement":   evt = JingleEvent.AchievementCapstone;  break;
                default:                      evt = JingleEvent.Achievement;          break; // master fallback
            }

            _jingleService?.PlayForEvent(evt, _getSettings?.Invoke());
        }

        private void Notify(string message)
        {
            _api.Notifications.Add(new NotificationMessage(
                NotificationPrefix,
                $"UniPlaySong: {message}",
                NotificationType.Info));
        }
    }
}
