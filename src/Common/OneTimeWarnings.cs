using System;
using System.Collections.Generic;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    // User-facing warnings, each shown at most once per session as a Playnite notification and withdrawn when its
    // condition clears. Used by the plugin's "why is it silent" checks and by the playback service.
    public sealed class OneTimeWarnings
    {
        private static readonly ILogger Logger = GatedLogger.Get();

        private readonly IPlayniteAPI _api;
        private readonly FileLogger _fileLogger;
        private readonly HashSet<string> _shownWarnings = new HashSet<string>(StringComparer.Ordinal);

        public OneTimeWarnings(IPlayniteAPI api, FileLogger fileLogger)
        {
            _api = api;
            _fileLogger = fileLogger;
        }

        // Surfaces a warning to the user once per session, keyed by id.
        //
        // Once per session, deliberately: every caller sits on a path that re-runs on each game
        // select, so notifying every time would bury the notification list under duplicates of a
        // message that only needs reading once.
        //
        // A notification rather than a log line alone, because the conditions these describe are
        // undiagnosable from the interface - the settings all read normally and audio simply does
        // not arrive - and someone in that position does not know there is a log.
        public void Show(string id, string message)
        {
            if (string.IsNullOrEmpty(id) || !_shownWarnings.Add(id)) return;

            _fileLogger?.Lifecycle($"User warning [{id}]: {message.Replace(Environment.NewLine, " ")}");

            try
            {
                _api?.Notifications?.Add(new NotificationMessage(id, message, NotificationType.Info));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to surface the '{id}' notification");
            }
        }

        // Takes a one-time warning back, and re-arms its latch so the same condition returning is
        // announced again rather than silently.
        public void Withdraw(string id)
        {
            if (!_shownWarnings.Remove(id)) return;

            try { _api?.Notifications?.Remove(id); } catch { }
            _fileLogger?.Debug($"Withdrew the '{id}' notification - the condition it described no longer holds");
        }
    }
}
