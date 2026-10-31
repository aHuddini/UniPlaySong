using Playnite.SDK;

namespace UniPlaySong.Common
{
    // Conditional debug logging extensions for ILogger
    public static class LoggerExtensions
    {
        // Logs "[prefix] message" only when debug logging is enabled; prefix is the component, e.g. "Amplify".
        public static void DebugIf(this ILogger logger, string prefix, string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                logger.Debug($"[{prefix}] {message}");
            }
        }

        public static void DebugIf(this ILogger logger, string prefix, System.Exception ex, string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                logger.Debug(ex, $"[{prefix}] {message}");
            }
        }
    }
}
