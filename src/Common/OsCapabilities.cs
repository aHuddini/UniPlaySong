using System;

namespace UniPlaySong.Common
{
    // OS feature gates. Process Loopback Capture (ActivateAudioInterfaceAsync +
    // AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS) requires Windows 10 build 20348 or later.
    public static class OsCapabilities
    {
        public static bool SupportsProcessLoopback => GetBuildNumber() >= 20348;

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
