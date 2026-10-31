using Playnite.SDK;

namespace UniPlaySong.Common
{
    // Permanent tripwire for PropertyChanged loops. Two-way bindings that ping-pong (e.g. an inverse radio pair through
    // InverseBooleanConverter) re-enter setters until the UI stack overflows — uncatchable, so nothing gets logged.
    // UniPlaySongSettings and UniPlaySongSettingsViewModel keep a depth counter on OnPropertyChanged and call this
    // when it trips: it names the property + full stack, and the caller returns without re-raising, breaking the
    // loop before it overflows. Logging is capped so a loop can't flood extensions.log.
    public static class CrashProbe
    {
        private static readonly ILogger _log = global::UniPlaySong.Common.GatedLogger.Get();
        private static int _trips;

        public static void PropertyLoop(string property, int depth)
        {
            if (_trips++ >= 8) return;
            _log.Error(
                $"[UPS CrashProbe] PropertyChanged re-entrancy on '{property}' (depth={depth}, trip #{_trips}) " +
                $"— loop broken to avoid StackOverflow.\n{new System.Diagnostics.StackTrace(true)}");
        }
    }
}
