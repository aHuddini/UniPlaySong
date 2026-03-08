using System;
using System.IO;
using UniPlaySong.Common;

namespace UniPlaySong.Downloaders
{
    // Dedicated logger for download operations (writes to downloader.log)
    public class DownloaderLogger
    {
        private static DownloaderLogger _instance;
        private static readonly object _instanceLock = new object();

        private readonly string _logFilePath;
        private readonly object _writeLock = new object();
        private bool _initialized;

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

        // Initialize once during plugin startup with extension path
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

        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("WARN", message);
        public void Error(string message) => Write("ERROR", message);

        // Only logs when debug logging is enabled
        public void Debug(string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                Write("DEBUG", message);
            }
        }

        // Writes a session separator (call at start of batch/manual download)
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
