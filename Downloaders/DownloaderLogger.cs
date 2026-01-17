using System;
using System.IO;
using UniPlaySong.Common;

namespace UniPlaySong.Downloaders
{
    /// <summary>
    /// Dedicated logger for download operations. Writes to downloader.log in the extension folder.
    /// Keeps download-related logging separate from main extension log to reduce noise.
    /// </summary>
    public class DownloaderLogger
    {
        private static DownloaderLogger _instance;
        private static readonly object _instanceLock = new object();

        private readonly string _logFilePath;
        private readonly object _writeLock = new object();
        private bool _initialized;

        /// <summary>
        /// Gets the singleton instance of DownloaderLogger.
        /// </summary>
        public static DownloaderLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new DownloaderLogger();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Initializes the singleton with a specific extension path.
        /// Should be called once during plugin initialization.
        /// </summary>
        public static void Initialize(string extensionPath)
        {
            lock (_instanceLock)
            {
                _instance = new DownloaderLogger(extensionPath);
            }
        }

        private DownloaderLogger() : this(null) { }

        private DownloaderLogger(string extensionPath)
        {
            // Determine log file path
            if (!string.IsNullOrEmpty(extensionPath) && Directory.Exists(extensionPath))
            {
                _logFilePath = Path.Combine(extensionPath, Constants.DownloaderLogFileName);
            }
            else
            {
                // Fallback to Playnite AppData
                var playniteAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Constants.PlayniteFolderName,
                    Constants.PlayniteExtensionsFolderName);

                if (Directory.Exists(playniteAppData))
                {
                    var extensionFolders = Directory.GetDirectories(playniteAppData, Constants.ExtensionFolderName + "*");
                    if (extensionFolders.Length > 0)
                    {
                        _logFilePath = Path.Combine(extensionFolders[0], Constants.DownloaderLogFileName);
                    }
                }

                // Final fallback
                if (string.IsNullOrEmpty(_logFilePath))
                {
                    _logFilePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        Constants.PlayniteFolderName,
                        Constants.DownloaderLogFileName);
                }
            }

            // Initialize log file
            try
            {
                var logDir = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                // Rotate log if it's too large (> 5MB)
                if (File.Exists(_logFilePath))
                {
                    var fileInfo = new FileInfo(_logFilePath);
                    if (fileInfo.Length > 5 * 1024 * 1024)
                    {
                        var backupPath = _logFilePath + ".old";
                        if (File.Exists(backupPath))
                        {
                            File.Delete(backupPath);
                        }
                        File.Move(_logFilePath, backupPath);
                    }
                }

                _initialized = true;
            }
            catch
            {
                _initialized = false;
            }
        }

        private void Write(string level, string message)
        {
            if (!_initialized) return;

            try
            {
                lock (_writeLock)
                {
                    var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    var logEntry = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(_logFilePath, logEntry);
                }
            }
            catch
            {
                // Silently fail - logging shouldn't break functionality
            }
        }

        /// <summary>
        /// Logs an info message (always logged).
        /// </summary>
        public void Info(string message) => Write("INFO", message);

        /// <summary>
        /// Logs a warning message (always logged).
        /// </summary>
        public void Warn(string message) => Write("WARN", message);

        /// <summary>
        /// Logs an error message (always logged).
        /// </summary>
        public void Error(string message) => Write("ERROR", message);

        /// <summary>
        /// Logs a debug message (only when debug logging is enabled).
        /// </summary>
        public void Debug(string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                Write("DEBUG", message);
            }
        }

        /// <summary>
        /// Writes a session separator with timestamp.
        /// Call at start of batch download or manual download session.
        /// </summary>
        public void StartSession(string sessionType)
        {
            if (!_initialized) return;

            try
            {
                lock (_writeLock)
                {
                    var separator = $"{Environment.NewLine}{'='.ToString().PadRight(60, '=')}{Environment.NewLine}";
                    var header = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {sessionType} Session Started{Environment.NewLine}";
                    File.AppendAllText(_logFilePath, separator + header);
                }
            }
            catch { }
        }

        /// <summary>
        /// Writes a session end marker.
        /// </summary>
        public void EndSession(string summary)
        {
            if (!_initialized) return;

            try
            {
                lock (_writeLock)
                {
                    var footer = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Session Complete: {summary}{Environment.NewLine}";
                    var separator = $"{'='.ToString().PadRight(60, '=')}{Environment.NewLine}";
                    File.AppendAllText(_logFilePath, footer + separator);
                }
            }
            catch { }
        }
    }
}
