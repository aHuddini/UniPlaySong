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
                // When Spotify is the active radio source, the URI pause/play must reach SPOTIFY
                // (via the manual-pause hold, so radio recompute and the external-audio detector
                // won't auto-resume it) — UPS's own player is silent in that mode. Integrations
                // like FullReel rely on pause meaning "whatever UPS is playing stays quiet".
                case "play":
                    {
                        var spotify = _getSpotify?.Invoke();
                        if (spotify != null && spotify.IsSpotifyActive)
                            spotify.ManualResume();
                        _playbackService.NotifyManualStart();
                        // Explicit external play also clears a stale FocusLoss — with the caller's
                        // window (e.g. FullReel's WebView2) holding Win32 focus, OnApplicationActivate
                        // never fires for the main window, so Resume() alone would leave FocusLoss
                        // pinning playback paused.
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
                // {rarity} is common | uncommon | rare | ultrarare | capstone. Namespaced under the
                // source plugin so other integrations can add their own path later. All rarities play
                // the same achievement sound for now (per-rarity override sounds are a planned
                // follow-up). Plays on the dedicated jingle player, so it works over a running game
                // and no-ops when the achievement-sound setting is off.
                case "playniteachievements":
                    HandlePlayniteAchievement(args.Arguments);
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

        private void HandlePlayniteAchievement(string[] arguments)
        {
            // arguments[0] == "playniteachievements"; arguments[1] (optional) == the rarity tier
            // segment (Playnite Achievements' command names, lowercased):
            //   commonachievement | uncommonachievement | rareachievement | ultrarareachievement | capstoneachievement
            // Each maps to its own JingleEvent; the event resolves to that rarity's sound, or falls
            // back to the master achievement sound when the rarity has none. An unknown or missing
            // tier plays the master sound, so a newer PA that adds a tier still works.
            var tier = arguments != null && arguments.Length > 1
                ? arguments[1].ToLowerInvariant()
                : null;

            JingleEvent evt;
            switch (tier)
            {
                case "commonachievement":     evt = JingleEvent.AchievementCommon;    break;
                case "uncommonachievement":   evt = JingleEvent.AchievementUncommon;  break;
                case "rareachievement":       evt = JingleEvent.AchievementRare;      break;
                case "ultrarareachievement":  evt = JingleEvent.AchievementUltraRare; break;
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
