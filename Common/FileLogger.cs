using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UniPlaySong.Common;

namespace UniPlaySong.Common
{
    // Simple file-based logger with conditional debug logging
    public class FileLogger
    {
        private readonly string _logFilePath;
        private readonly object _lockObject = new object();
        private bool _initialized = false;

        // When null or returns true, debug logging is enabled
        public static Func<bool> GlobalDebugEnabled { get; set; }

        public static bool IsDebugLoggingEnabled => GlobalDebugEnabled == null || GlobalDebugEnabled();

        // Instance-level debug check
        public Func<bool> IsDebugEnabled { get; set; }

        public FileLogger(string extensionPath)
        {
            // Try multiple fallback locations
            var possiblePaths = new List<string>();
            
            // First, try the provided extension path
            if (!string.IsNullOrEmpty(extensionPath) && Directory.Exists(extensionPath))
            {
                possiblePaths.Add(Path.Combine(extensionPath, Constants.LogFileName));
            }
            
            // Fallback to Playnite extensions directory
            var playniteAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.PlayniteFolderName, Constants.PlayniteExtensionsFolderName);
            if (Directory.Exists(playniteAppData))
            {
                // Try to find our extension folder
                var extensionFolders = Directory.GetDirectories(playniteAppData, Constants.ExtensionFolderName + "*");
                if (extensionFolders.Length > 0)
                {
                    possiblePaths.Add(Path.Combine(extensionFolders[0], Constants.LogFileName));
                }
            }
            
            // Final fallback to Playnite AppData
            possiblePaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.PlayniteFolderName, Constants.LogFileName));
            
            // Use the first path that we can write to, or the last one as final fallback
            var pathCount = possiblePaths.Count;
            _logFilePath = pathCount > 0 ? possiblePaths[0] : possiblePaths[pathCount - 1];
            
            // Try to create the directory if it doesn't exist
            try
            {
                var logDir = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                
                // Write an initial test entry to verify we can write
                var testEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] FileLogger initialized. Log file: {_logFilePath}\n";
                File.AppendAllText(_logFilePath, testEntry);
                _initialized = true;
            }
            catch (Exception)
            {
                // If we can't write, try the final fallback
                var fallbackIndex = possiblePaths.Count - 1;
                _logFilePath = possiblePaths[fallbackIndex];
                try
                {
                    var logDir = Path.GetDirectoryName(_logFilePath);
                    if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }
                    var testEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] FileLogger initialized (fallback). Log file: {_logFilePath}\n";
                    File.AppendAllText(_logFilePath, testEntry);
                    _initialized = true;
                }
                catch
                {
                    _initialized = false;
                }
            }
        }

        public void Log(string level, string message, Exception exception = null)
        {
            if (!_initialized)
            {
                return; // Don't try to log if initialization failed
            }
            
            try
            {
                lock (_lockObject)
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var logEntry = $"[{timestamp}] [{level}] {message}";
                    
                    if (exception != null)
                    {
                        logEntry += $"\nException: {exception.GetType().Name}: {exception.Message}";
                        logEntry += $"\nStack Trace: {exception.StackTrace}";
                    }
                    
                    logEntry += Environment.NewLine;
                    
                    File.AppendAllText(_logFilePath, logEntry);
                }
            }
            catch (Exception ex)
            {
                // Try to write error to Debug output as last resort
                System.Diagnostics.Debug.WriteLine($"FileLogger failed to write: {ex.Message}");
            }
        }

        public void Info(string message)
        {
            Log("INFO", message);
        }

        public void Debug(string message)
        {
            // Only log DEBUG messages if debug logging is explicitly enabled
            // This reduces log file size for end users while allowing verbose logging for troubleshooting
            if (IsDebugEnabled != null && !IsDebugEnabled())
            {
                return;
            }
            Log("DEBUG", message);
        }

        public void Warn(string message)
        {
            Log("WARN", message);
        }

        public void Error(string message, Exception exception = null)
        {
            Log("ERROR", message, exception);
        }
    }
}

