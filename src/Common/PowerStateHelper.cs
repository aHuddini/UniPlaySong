using System;
using System.Runtime.InteropServices;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    // v1.5.3 — explicit Windows power-state opt-out.
    //
    // UPS holds a persistent audio device open between songs (a deliberate
    // v1.3.3 trade-off that eliminates ~70ms UI freeze on game switches).
    // The cost: Windows considers an active audio session running and blocks
    // system sleep, surfacing in powercfg /requests under [DRIVER].
    //
    // Calling SetThreadExecutionState(ES_CONTINUOUS) without any of the keep-
    // alive flags (ES_SYSTEM_REQUIRED, ES_DISPLAY_REQUIRED, ES_AWAYMODE_REQUIRED)
    // is the documented Win32 way to clear any previous "keep awake" assertions
    // this thread/process has made. It doesn't override the audio session's
    // implicit hold — but it does ensure UPS itself is not contributing any
    // explicit hint on top of that. Cheap, side-effect-free, always-on
    // belt-and-suspenders against issue #81.
    public static class PowerStateHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        // ES_CONTINUOUS = 0x80000000 — informs the system that the state being set
        // should remain in effect until the next call that uses ES_CONTINUOUS and
        // one of the other state flags is cleared. Passed alone (without
        // ES_SYSTEM_REQUIRED etc.), it clears any prior keep-alive assertion.
        private const uint ES_CONTINUOUS = 0x80000000;

        private static readonly ILogger Logger = LogManager.GetLogger();

        // Call this when UPS doesn't need to keep the system awake (always, in
        // current design — UPS never explicitly wants to block sleep). Safe to
        // call repeatedly; idempotent.
        public static void OptOutOfKeepAwake()
        {
            try
            {
                var prev = SetThreadExecutionState(ES_CONTINUOUS);
                // prev is the previous state — we log it at debug level for diagnostics
                // but only the first time, to avoid log spam from repeated calls.
                if (!_loggedOnce)
                {
                    _loggedOnce = true;
                    Logger.Debug($"[PowerStateHelper] OptOutOfKeepAwake: previous state=0x{prev:X8}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[PowerStateHelper] SetThreadExecutionState failed: {ex.Message}");
            }
        }

        // volatile: OptOutOfKeepAwake may be reached from more than one thread
        // (both backends init on the UI thread today, but SDL2 callbacks run on
        // background threads). Worst case of a race here is logging the one-time
        // diagnostic twice — harmless — but volatile keeps the read/write honest.
        private static volatile bool _loggedOnce;
    }
}
