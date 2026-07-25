using System;

namespace UniPlaySong.Common
{
    // OS feature gates. Process Loopback Capture (ActivateAudioInterfaceAsync +
    // AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS) is DOCUMENTED as build 20348+, but the API is
    // actually present and usable since Windows 10 version 2004 (build 19041) — the header
    // just ships in the Win11 SDK. Shipped projects (OBS win-capture-audio, masonasons/
    // AudioCapture) capture per-process audio on 19041+ with this same API. Gate lowered to
    // 19041 to allow the attempt on Windows 10 2004+; capture start still fails gracefully
    // (SpotifyLoopbackClient.Start returns false → coordinator stays dry) if a given machine's
    // process-loopback path doesn't actually work, so lowering the floor can't double audio.
    public static class OsCapabilities
    {
        public static bool SupportsProcessLoopback => GetBuildNumber() >= 19041;

        private static int GetBuildNumber()
        {
            try
            {
                var v = Environment.OSVersion.Version;   // Build in v.Build on .NET Framework
                return v.Build;
            }
            catch { return 0; }
        }
    }
}
