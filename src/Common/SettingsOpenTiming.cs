using System.Diagnostics;

namespace UniPlaySong.Common
{
    // Debug-log timeline of opening the settings window. Each line shows the step's own time and a monotonic
    // timestamp (t=), so lines from the view model and the view can be lined up. Writes only with debug logging on.
    internal sealed class SettingsOpenTiming
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        private readonly string _phase;
        private long _last;

        private SettingsOpenTiming(string phase)
        {
            _phase = phase;
            _last = Clock.ElapsedMilliseconds;
        }

        public static SettingsOpenTiming Start(string phase) => new SettingsOpenTiming(phase);

        public void Step(string name)
        {
            if (!FileLogger.IsDebugLoggingEnabled) return;
            long now = Clock.ElapsedMilliseconds;
            GatedLogger.Get().Debug($"[Perf] Settings {_phase}: {name} {now - _last} ms (t={now})");
            _last = now;
        }
    }
}
