using Playnite.SDK;

namespace UniPlaySong.Common
{
    // Permanent tripwire (added v1.6.8). The settings-dialog reopen crash was a WPF StackOverflow
    // (0xc00000fd): the HintsDatabase radio pair's two-way bindings ping-ponged through
    // InverseBooleanConverter and re-entered property setters until the 1 MB UI stack blew —
    // uncatchable, so nothing was ever logged. The root cause is fixed (OneWay inverse radio +
    // equality-guarded setter), but UniPlaySongSettings and UniPlaySongSettingsViewModel keep a
    // depth counter on OnPropertyChanged and call this if a notification loop ever trips again:
    // it names the offending property + full stack, and the caller returns without re-raising,
    // breaking the loop before it overflows. Logging is capped so a loop can't flood extensions.log.
    public static class CrashProbe
    {
        private static readonly ILogger _log = LogManager.GetLogger();
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
