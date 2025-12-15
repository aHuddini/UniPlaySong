using System;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Centralized error handling and logging service
    /// Provides consistent error handling, logging, and user-friendly error messages
    /// </summary>
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

        /// <summary>
        /// Executes an action with error handling
        /// </summary>
        /// <param name="action">The action to execute</param>
        /// <param name="context">Context description for logging (e.g., "downloading music")</param>
        /// <param name="showUserMessage">Whether to show a user-friendly error message dialog</param>
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

        /// <summary>
        /// Executes a function with error handling, returns default value on error
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="func">The function to execute</param>
        /// <param name="defaultValue">Value to return on error</param>
        /// <param name="context">Context description for logging</param>
        /// <param name="showUserMessage">Whether to show a user-friendly error message dialog</param>
        /// <returns>The function result, or defaultValue on error</returns>
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

        /// <summary>
        /// Handles an exception with logging and optional user notification
        /// </summary>
        /// <param name="ex">The exception to handle</param>
        /// <param name="context">Context description for logging</param>
        /// <param name="showUserMessage">Whether to show a user-friendly error message dialog</param>
        /// <param name="userFriendlyMessage">Custom user-friendly message (optional)</param>
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

        /// <summary>
        /// Converts technical exceptions to user-friendly messages
        /// </summary>
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

        /// <summary>
        /// Logs an informational message
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="fileOnly">If true, only log to file logger (not Playnite logger)</param>
        public void LogInfo(string message, bool fileOnly = false)
        {
            if (!fileOnly)
                _logger.Info(message);
            _fileLogger?.Info(message);
        }

        /// <summary>
        /// Logs a warning message
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="fileOnly">If true, only log to file logger (not Playnite logger)</param>
        public void LogWarning(string message, bool fileOnly = false)
        {
            if (!fileOnly)
                _logger.Warn(message);
            _fileLogger?.Warn(message);
        }
    }
}

