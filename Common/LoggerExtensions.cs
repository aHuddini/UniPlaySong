using Playnite.SDK;

namespace UniPlaySong.Common
{
    /// <summary>
    /// Extension methods for ILogger to provide conditional debug logging
    /// </summary>
    public static class LoggerExtensions
    {
        /// <summary>
        /// Logs a debug message with a prefix only if debug logging is enabled in settings.
        /// Centralizes the conditional debug logging pattern used throughout the codebase.
        /// </summary>
        /// <param name="logger">The logger instance</param>
        /// <param name="prefix">The component prefix (e.g., "Amplify", "PreciseTrim")</param>
        /// <param name="message">The debug message to log</param>
        public static void DebugIf(this ILogger logger, string prefix, string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                logger.Debug($"[{prefix}] {message}");
            }
        }

        /// <summary>
        /// Logs a debug message with exception and prefix only if debug logging is enabled.
        /// </summary>
        /// <param name="logger">The logger instance</param>
        /// <param name="prefix">The component prefix</param>
        /// <param name="ex">The exception to log</param>
        /// <param name="message">The debug message to log</param>
        public static void DebugIf(this ILogger logger, string prefix, System.Exception ex, string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                logger.Debug(ex, $"[{prefix}] {message}");
            }
        }
    }
}
