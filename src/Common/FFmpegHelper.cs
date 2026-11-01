using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    // FFmpeg validation and common operations
    public static class FFmpegHelper
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();

        // True if the file exists and actually runs as FFmpeg.
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

        // Encoder arguments for re-encoding to the same format as the input (Trim and Amplify).
        public static string GetCodecArgs(string extension)
        {
            var ext = extension.ToLowerInvariant();
            switch (ext)
            {
                case ".mp3":
                    return "-c:a libmp3lame -q:a 0"; // High quality MP3
                case ".flac":
                    return "-c:a flac"; // Lossless FLAC
                case ".wav":
                    return "-c:a pcm_s16le"; // Uncompressed WAV
                case ".ogg":
                    return "-c:a libvorbis -q:a 6"; // Good quality OGG
                case ".m4a":
                case ".aac":
                    return "-c:a aac -b:a 256k"; // High quality AAC
                default:
                    return "-c:a copy"; // Try to copy codec
            }
        }

        // Copies the original into PreservedOriginals\<game folder>\ before an edit. backupBasePath is the configured
        // backup folder, or null for the music folder's parent. Returns the preserved path, or null on failure. Copied,
        // not moved: FFmpeg still has to read the original.
        public static async Task<string> PreserveOriginalAsync(string backupBasePath, string inputPath, string directory,
            string fileName, string extension, string logPrefix)
        {
            Logger.DebugIf(logPrefix, $"PreserveOriginalAsync - preserving: {fileName}{extension}");
            return await Task.Run(() =>
            {
                try
                {
                    string preservedOriginalsDir;
                    if (!string.IsNullOrEmpty(backupBasePath))
                    {
                        preservedOriginalsDir = Path.Combine(backupBasePath, Constants.PreservedOriginalsFolderName);
                        Logger.DebugIf(logPrefix, $"Using configured backup path: {preservedOriginalsDir}");
                    }
                    else
                    {
                        var parentDir = Directory.GetParent(directory)?.FullName ?? directory;
                        preservedOriginalsDir = Path.Combine(parentDir, Constants.PreservedOriginalsFolderName);
                        Logger.DebugIf(logPrefix, $"Using parent directory backup path: {preservedOriginalsDir}");
                    }

                    // Preserve relative structure (game folder name)
                    var gameFolderName = Path.GetFileName(directory);
                    var gamePreservedDir = Path.Combine(preservedOriginalsDir, gameFolderName);
                    Directory.CreateDirectory(gamePreservedDir);

                    var preservedPath = Path.Combine(gamePreservedDir, $"{fileName}{extension}");

                    if (!File.Exists(preservedPath))
                    {
                        File.Copy(inputPath, preservedPath, false);
                        Logger.DebugIf(logPrefix, $"Preserved original to: {preservedPath}");
                    }
                    else
                    {
                        Logger.DebugIf(logPrefix, $"Preserved original already exists: {preservedPath}");
                    }

                    return preservedPath;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error preserving original file");
                    Logger.DebugIf(logPrefix, $"PreserveOriginalAsync exception: {ex.Message}");
                    return null;
                }
            });
        }

        // Runs FFmpeg with a 2-minute timeout; true on exit code 0. Output is read asynchronously so a full stderr
        // pipe can't deadlock the process.
        public static async Task<bool> RunAsync(string ffmpegPath, string args, CancellationToken token, string logPrefix)
        {
            Logger.DebugIf(logPrefix, $"RunFFmpegAsync starting - ffmpegPath: {ffmpegPath}");
            return await Task.Run(() =>
            {
                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = args,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    Logger.DebugIf(logPrefix, "Starting FFmpeg process...");
                    using (var process = Process.Start(processInfo))
                    {
                        if (process == null)
                        {
                            Logger.Error("Failed to start FFmpeg process");
                            Logger.DebugIf(logPrefix, "Process.Start returned null");
                            return false;
                        }

                        Logger.DebugIf(logPrefix, $"FFmpeg process started with PID: {process.Id}");

                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();

                        var timeout = 120000; // 2 minutes
                        Logger.DebugIf(logPrefix, "Waiting for FFmpeg to complete...");
                        if (!process.WaitForExit(timeout))
                        {
                            try { process.Kill(); } catch { }
                            Logger.Error("FFmpeg process timed out");
                            Logger.DebugIf(logPrefix, "FFmpeg process timed out after 2 minutes");
                            return false;
                        }

                        token.ThrowIfCancellationRequested();

                        var stdout = outputTask.Result;
                        var stderr = errorTask.Result;

                        Logger.DebugIf(logPrefix, $"FFmpeg exit code: {process.ExitCode}");
                        if (!string.IsNullOrEmpty(stderr))
                        {
                            // FFmpeg writes progress to stderr even on success; only the first 500 chars are logged
                            var stderrPreview = stderr.Length > 500 ? stderr.Substring(0, 500) + "..." : stderr;
                            Logger.DebugIf(logPrefix, $"FFmpeg stderr: {stderrPreview}");
                        }

                        if (process.ExitCode != 0)
                        {
                            Logger.Error($"FFmpeg failed with exit code {process.ExitCode}: {stderr}");
                            Logger.DebugIf(logPrefix, $"FFmpeg FAILED - full stderr logged to error log");
                            return false;
                        }

                        Logger.DebugIf(logPrefix, "FFmpeg completed successfully");
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.DebugIf(logPrefix, "FFmpeg operation cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error running FFmpeg");
                    Logger.DebugIf(logPrefix, $"FFmpeg exception: {ex.Message}");
                    return false;
                }
            }, token);
        }
    }
}
