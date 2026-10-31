using System;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Centralized error handling and logging service Provides consistent error handling, logging, and
    // user-friendly error messages
    public class ErrorHandlerService
    {
        private readonly ILogger _logger;
        private readonly FileLogger _fileLogger;
        private readonly IPlayniteAPI _playniteApi;

        public ErrorHandlerService(ILogger logger, FileLogger fileLogger, IPlayniteAPI playniteApi)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileLogger = fileLogger;
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
        }

        // context names the operation for the log and message (e.g. "downloading music"); showUserMessage shows an error dialog.
        public void Try(Action action, string context = null, bool showUserMessage = false)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                HandleError(ex, context, showUserMessage);
            }
        }

        // Returns defaultValue on error.
        public T Try<T>(Func<T> func, T defaultValue = default, string context = null, bool showUserMessage = false)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                HandleError(ex, context, showUserMessage);
                return defaultValue;
            }
        }

        // Logs to both loggers; the dialog shows userFriendlyMessage, else a message derived from the exception type.
        public void HandleError(Exception ex, string context = null, bool showUserMessage = false, string userFriendlyMessage = null)
        {
            // Log to Playnite logger
            var logMessage = string.IsNullOrEmpty(context) 
                ? $"Error: {ex.Message}" 
                : $"Error in {context}: {ex.Message}";
            
            _logger.Error(ex, logMessage);

            // Log to file logger (detailed)
            _fileLogger?.Error($"Error Details - Context: {context ?? "Unknown"}, Message: {ex.Message}, StackTrace: {ex.StackTrace}", ex);

            // Show user-friendly message if requested
            if (showUserMessage)
            {
                var message = userFriendlyMessage ?? GetUserFriendlyMessage(ex, context);
                _playniteApi.Dialogs.ShowErrorMessage(message, "UniPlaySong");
            }
        }

        // Converts technical exceptions to user-friendly messages
        private string GetUserFriendlyMessage(Exception ex, string context)
        {
            // Handle specific exception types
            if (ex is System.IO.FileNotFoundException)
                return "File not found. Please check that the file exists and try again.";
            
            if (ex is UnauthorizedAccessException)
                return "Access denied. Please check file permissions and try again.";
            
            if (ex is System.IO.IOException)
                return "File operation failed. The file may be in use by another program.";
            
            if (ex is TimeoutException)
                return "Operation timed out. Please try again.";
            
            if (ex is ArgumentException || ex is ArgumentNullException)
                return "Invalid input. Please check your settings and try again.";

            // Generic message with context
            if (!string.IsNullOrEmpty(context))
                return $"An error occurred while {context.ToLower()}. Please check the logs for details.";
            
            return "An unexpected error occurred. Please check the logs for details.";
        }

        // fileOnly skips the Playnite logger and writes only to the file logger.
        public void LogInfo(string message, bool fileOnly = false)
        {
            if (!fileOnly)
                _logger.Debug(message);
            _fileLogger?.Info(message);
        }

        // fileOnly skips the Playnite logger and writes only to the file logger.
        public void LogWarning(string message, bool fileOnly = false)
        {
            if (!fileOnly)
                _logger.Warn(message);
            _fileLogger?.Warn(message);
        }
    }
}

