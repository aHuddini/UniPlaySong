using System;
using System.Diagnostics;
using System.IO;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    /// <summary>
    /// Helper class for FFmpeg validation and common operations
    /// </summary>
    public static class FFmpegHelper
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        /// <summary>
        /// Validates that FFmpeg is available and executable at the specified path
        /// </summary>
        /// <param name="ffmpegPath">Path to the FFmpeg executable</param>
        /// <returns>True if FFmpeg is available and working; otherwise, false</returns>
        public static bool IsAvailable(string ffmpegPath)
        {
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                return false;
            }

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit(5000); // 5 second timeout
                        return process.ExitCode == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error validating FFmpeg");
            }

            return false;
        }
    }
}
