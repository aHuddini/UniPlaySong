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
        private const string NotificationPrefix = "UniPlaySong_ExtCtrl";

        public ExternalControlService(IMusicPlaybackService playbackService, IActiveMediaService activeMedia, IPlayniteAPI api)
        {
            _playbackService = playbackService;
            _activeMedia = activeMedia;
            _api = api;
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
                case "play":
                    _playbackService.NotifyManualStart();
                    _playbackService.Resume();
                    break;

                case "pause":
                    _playbackService.Pause();
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

        private void Notify(string message)
        {
            _api.Notifications.Add(new NotificationMessage(
                NotificationPrefix,
                $"UniPlaySong: {message}",
                NotificationType.Info));
        }
    }
}
