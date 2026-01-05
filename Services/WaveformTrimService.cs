using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Models.WaveformTrim;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Service for waveform generation and precise audio trimming using NAudio and FFmpeg
    /// </summary>
    public class WaveformTrimService : IWaveformTrimService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "PreciseTrim";
        private readonly ErrorHandlerService _errorHandler;
        private readonly IMusicPlaybackService _playbackService;
        private readonly string _backupBasePath;

        // Target number of samples for waveform display
        private const int TargetWaveformSamples = 1000;

        public WaveformTrimService(
            ErrorHandlerService errorHandler = null,
            IMusicPlaybackService playbackService = null,
            string backupBasePath = null)
        {
            _errorHandler = errorHandler;
            _playbackService = playbackService;
            _backupBasePath = backupBasePath;
        }

        /// <summary>
        /// Generate waveform data for display
        /// </summary>
        public async Task<WaveformData> GenerateWaveformAsync(string audioFilePath, CancellationToken token = default)
        {
            Logger.DebugIf(LogPrefix,$"GenerateWaveformAsync started for: {Path.GetFileName(audioFilePath)}");
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(audioFilePath))
                    {
                        Logger.Warn($"Audio file not found: {audioFilePath}");
                        return null;
                    }

                    using (var reader = new AudioFileReader(audioFilePath))
                    {
                        var totalSamples = reader.Length / (reader.WaveFormat.BitsPerSample / 8);
                        var channels = reader.WaveFormat.Channels;
                        var samplesPerChannel = totalSamples / channels;

                        // Calculate how many source samples to average per output sample
                        var samplesPerPoint = (int)Math.Ceiling((double)samplesPerChannel / TargetWaveformSamples);
                        var outputSamples = new float[TargetWaveformSamples];

                        // Read samples in chunks and find max amplitude for each output point
                        var buffer = new float[samplesPerPoint * channels];
                        int outputIndex = 0;

                        while (outputIndex < TargetWaveformSamples)
                        {
                            token.ThrowIfCancellationRequested();

                            int samplesRead = reader.Read(buffer, 0, buffer.Length);
                            if (samplesRead == 0) break;

                            // Find max absolute value in this chunk (averaging channels)
                            float maxAbs = 0;
                            for (int i = 0; i < samplesRead; i += channels)
                            {
                                float sample = 0;
                                for (int c = 0; c < channels && (i + c) < samplesRead; c++)
                                {
                                    sample += Math.Abs(buffer[i + c]);
                                }
                                sample /= channels;
                                if (sample > maxAbs) maxAbs = sample;
                            }

                            outputSamples[outputIndex] = maxAbs;
                            outputIndex++;
                        }

                        // Normalize to 0-1 range
                        float globalMax = 0;
                        for (int i = 0; i < outputSamples.Length; i++)
                        {
                            if (outputSamples[i] > globalMax) globalMax = outputSamples[i];
                        }

                        if (globalMax > 0)
                        {
                            for (int i = 0; i < outputSamples.Length; i++)
                            {
                                outputSamples[i] /= globalMax;
                            }
                        }

                        Logger.DebugIf(LogPrefix,$"Waveform generated: {outputSamples.Length} samples, duration={reader.TotalTime:mm\\:ss\\.fff}, rate={reader.WaveFormat.SampleRate}Hz, channels={channels}");

                        return new WaveformData
                        {
                            Samples = outputSamples,
                            Duration = reader.TotalTime,
                            SampleRate = reader.WaveFormat.SampleRate,
                            Channels = channels,
                            FilePath = audioFilePath
                        };
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.DebugIf(LogPrefix,"Waveform generation cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error generating waveform for: {audioFilePath}");
                    return null;
                }
            }, token);
        }

        /// <summary>
        /// Get audio duration
        /// </summary>
        public async Task<TimeSpan> GetAudioDurationAsync(string audioFilePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(audioFilePath))
                    {
                        return TimeSpan.Zero;
                    }

                    using (var reader = new AudioFileReader(audioFilePath))
                    {
                        return reader.TotalTime;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error getting duration for: {audioFilePath}");
                    return TimeSpan.Zero;
                }
            });
        }

        /// <summary>
        /// Apply trim using FFmpeg (uses reflection to get FFmpeg path - prefer the overload that accepts ffmpegPath directly)
        /// </summary>
        public async Task<bool> ApplyTrimAsync(string inputPath, TrimWindow trimWindow, string suffix, CancellationToken token = default)
        {
            // Try to get FFmpeg path from settings via reflection (fallback method)
            var ffmpegPath = GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                Logger.Error("FFmpeg path not configured - use the overload that accepts ffmpegPath directly");
                Logger.DebugIf(LogPrefix,"ApplyTrimAsync (no ffmpegPath) - GetFFmpegPath() returned null or empty");
                return false;
            }
            return await ApplyTrimAsync(inputPath, trimWindow, suffix, ffmpegPath, token);
        }

        /// <summary>
        /// Apply trim using FFmpeg with explicit FFmpeg path
        /// </summary>
        public async Task<bool> ApplyTrimAsync(string inputPath, TrimWindow trimWindow, string suffix, string ffmpegPath, CancellationToken token = default)
        {
            Logger.DebugIf(LogPrefix,$"ApplyTrimAsync started - input: {Path.GetFileName(inputPath)}, suffix: {suffix}");
            Logger.DebugIf(LogPrefix,$"TrimWindow: start={trimWindow.StartTime:mm\\:ss\\.fff}, end={trimWindow.EndTime:mm\\:ss\\.fff}, duration={trimWindow.Duration:mm\\:ss\\.fff}");
            Logger.DebugIf(LogPrefix,$"FFmpeg path (passed in): {ffmpegPath}");
            try
            {
                if (!trimWindow.IsValid)
                {
                    Logger.Warn("Invalid trim window");
                    Logger.DebugIf(LogPrefix,$"TrimWindow validation failed: StartTime={trimWindow.StartTime}, EndTime={trimWindow.EndTime}, TotalDuration={trimWindow.TotalDuration}");
                    return false;
                }

                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    Logger.Error("FFmpeg path is null or empty");
                    Logger.DebugIf(LogPrefix,"ApplyTrimAsync - ffmpegPath parameter is null or empty");
                    return false;
                }

                var directory = Path.GetDirectoryName(inputPath);
                var fileName = Path.GetFileNameWithoutExtension(inputPath);
                var extension = Path.GetExtension(inputPath);

                // Build output filename with suffix
                var outputFileName = $"{fileName}{suffix}";
                var finalOutputPath = Path.Combine(directory, $"{outputFileName}{extension}");
                var tempPath = Path.Combine(directory, $"{fileName}.ptrim.tmp{extension}");

                Logger.DebugIf(LogPrefix,$"Output path: {finalOutputPath}");
                Logger.DebugIf(LogPrefix,$"Temp path: {tempPath}");
                Logger.Info($"Precise trim: {fileName} [{trimWindow.StartTime:mm\\:ss\\.fff} - {trimWindow.EndTime:mm\\:ss\\.fff}]");

                // Stop playback if this file is playing
                _playbackService?.Stop();

                // Preserve original file
                string preservedOriginalPath = await PreserveOriginalAsync(inputPath, directory, fileName, extension);

                // Build FFmpeg command for precise trim
                // Using -ss before -i for fast seeking, -to for end time
                // Use format with period for decimal separator regardless of locale
                var startSeconds = trimWindow.StartTime.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
                var durationSeconds = trimWindow.Duration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);

                // Determine codec based on extension
                var codecArgs = GetCodecArgs(extension);

                // FFmpeg command: seek to start, limit duration, copy or re-encode
                var args = $"-y -ss {startSeconds} -i \"{inputPath}\" -t {durationSeconds} {codecArgs} \"{tempPath}\"";

                Logger.DebugIf(LogPrefix,$"FFmpeg path: {ffmpegPath}");
                Logger.DebugIf(LogPrefix,$"FFmpeg args: {args}");

                var success = await RunFFmpegAsync(ffmpegPath, args, token);

                if (success && File.Exists(tempPath))
                {
                    Logger.DebugIf(LogPrefix,"FFmpeg completed successfully, temp file exists");
                    // Delete original (it's preserved in PreservedOriginals)
                    if (File.Exists(inputPath))
                    {
                        Logger.DebugIf(LogPrefix,$"Deleting original: {inputPath}");
                        File.Delete(inputPath);
                    }

                    // Move temp to final output
                    Logger.DebugIf(LogPrefix,$"Moving temp to final: {finalOutputPath}");
                    File.Move(tempPath, finalOutputPath);

                    Logger.Info($"Precise trim completed: {Path.GetFileName(finalOutputPath)}");
                    Logger.DebugIf(LogPrefix,"ApplyTrimAsync completed successfully");
                    return true;
                }
                else
                {
                    Logger.DebugIf(LogPrefix,$"FFmpeg failed or temp file missing. success={success}, tempExists={File.Exists(tempPath)}");
                    // Cleanup temp file on failure
                    if (File.Exists(tempPath))
                    {
                        Logger.DebugIf(LogPrefix,"Cleaning up temp file");
                        try { File.Delete(tempPath); } catch { }
                    }

                    // Restore original if preserved
                    if (!string.IsNullOrEmpty(preservedOriginalPath) && File.Exists(preservedOriginalPath))
                    {
                        try
                        {
                            Logger.DebugIf(LogPrefix,"Restoring original file from preserved backup");
                            File.Copy(preservedOriginalPath, inputPath, true);
                            Logger.Info("Restored original file after failed trim");
                        }
                        catch (Exception restoreEx)
                        {
                            Logger.Error(restoreEx, "Failed to restore original file");
                        }
                    }

                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                Logger.DebugIf(LogPrefix,"Trim operation cancelled by user");
                Logger.Info("Trim operation cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error applying trim to: {inputPath}");
                Logger.DebugIf(LogPrefix,$"ApplyTrimAsync exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Preserve original file to PreservedOriginals folder
        /// </summary>
        private async Task<string> PreserveOriginalAsync(string inputPath, string directory, string fileName, string extension)
        {
            Logger.DebugIf(LogPrefix,$"PreserveOriginalAsync - preserving: {fileName}{extension}");
            return await Task.Run(() =>
            {
                try
                {
                    // Determine preserved originals directory
                    string preservedOriginalsDir;
                    if (!string.IsNullOrEmpty(_backupBasePath))
                    {
                        preservedOriginalsDir = Path.Combine(_backupBasePath, Constants.PreservedOriginalsFolderName);
                        Logger.DebugIf(LogPrefix,$"Using configured backup path: {preservedOriginalsDir}");
                    }
                    else
                    {
                        var parentDir = Directory.GetParent(directory)?.FullName ?? directory;
                        preservedOriginalsDir = Path.Combine(parentDir, Constants.PreservedOriginalsFolderName);
                        Logger.DebugIf(LogPrefix,$"Using parent directory backup path: {preservedOriginalsDir}");
                    }

                    // Preserve relative structure (game folder name)
                    var gameFolderName = Path.GetFileName(directory);
                    var gamePreservedDir = Path.Combine(preservedOriginalsDir, gameFolderName);
                    Directory.CreateDirectory(gamePreservedDir);

                    var preservedPath = Path.Combine(gamePreservedDir, $"{fileName}{extension}");

                    // Copy original to preserved folder (don't move yet - FFmpeg needs to read it)
                    if (!File.Exists(preservedPath))
                    {
                        File.Copy(inputPath, preservedPath, false);
                        Logger.DebugIf(LogPrefix,$"Preserved original to: {preservedPath}");
                    }
                    else
                    {
                        Logger.DebugIf(LogPrefix,$"Preserved original already exists: {preservedPath}");
                    }

                    return preservedPath;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error preserving original file");
                    Logger.DebugIf(LogPrefix,$"PreserveOriginalAsync exception: {ex.Message}");
                    return null;
                }
            });
        }

        /// <summary>
        /// Get codec arguments based on file extension
        /// </summary>
        private string GetCodecArgs(string extension)
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

        /// <summary>
        /// Get FFmpeg path from settings (via Application.Current.Properties)
        /// </summary>
        private string GetFFmpegPath()
        {
            try
            {
                if (System.Windows.Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                {
                    var plugin = System.Windows.Application.Current.Properties["UniPlaySongPlugin"];
                    var settingsProperty = plugin?.GetType().GetProperty("Settings");
                    var settings = settingsProperty?.GetValue(plugin);
                    var ffmpegPathProperty = settings?.GetType().GetProperty("FFmpegPath");
                    var ffmpegPath = ffmpegPathProperty?.GetValue(settings) as string;
                    return ffmpegPath;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting FFmpeg path from settings");
            }
            return null;
        }

        /// <summary>
        /// Run FFmpeg with given arguments
        /// </summary>
        private async Task<bool> RunFFmpegAsync(string ffmpegPath, string args, CancellationToken token)
        {
            Logger.DebugIf(LogPrefix,$"RunFFmpegAsync starting - ffmpegPath: {ffmpegPath}");
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

                    Logger.DebugIf(LogPrefix,"Starting FFmpeg process...");
                    using (var process = Process.Start(processInfo))
                    {
                        if (process == null)
                        {
                            Logger.Error("Failed to start FFmpeg process");
                            Logger.DebugIf(LogPrefix,"Process.Start returned null");
                            return false;
                        }

                        Logger.DebugIf(LogPrefix,$"FFmpeg process started with PID: {process.Id}");

                        // Read output asynchronously to prevent deadlock
                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();

                        // Wait for process with timeout
                        var timeout = 120000; // 2 minutes
                        Logger.DebugIf(LogPrefix,"Waiting for FFmpeg to complete...");
                        if (!process.WaitForExit(timeout))
                        {
                            try { process.Kill(); } catch { }
                            Logger.Error("FFmpeg process timed out");
                            Logger.DebugIf(LogPrefix,"FFmpeg process timed out after 2 minutes");
                            return false;
                        }

                        token.ThrowIfCancellationRequested();

                        var stdout = outputTask.Result;
                        var stderr = errorTask.Result;

                        Logger.DebugIf(LogPrefix,$"FFmpeg exit code: {process.ExitCode}");
                        if (!string.IsNullOrEmpty(stderr))
                        {
                            // FFmpeg outputs progress info to stderr even on success
                            // Only log first 500 chars to avoid flooding
                            var stderrPreview = stderr.Length > 500 ? stderr.Substring(0, 500) + "..." : stderr;
                            Logger.DebugIf(LogPrefix,$"FFmpeg stderr: {stderrPreview}");
                        }

                        if (process.ExitCode != 0)
                        {
                            Logger.Error($"FFmpeg failed with exit code {process.ExitCode}: {stderr}");
                            Logger.DebugIf(LogPrefix,$"FFmpeg FAILED - full stderr logged to error log");
                            return false;
                        }

                        Logger.DebugIf(LogPrefix,"FFmpeg completed successfully");
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.DebugIf(LogPrefix,"FFmpeg operation cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error running FFmpeg");
                    Logger.DebugIf(LogPrefix,$"FFmpeg exception: {ex.Message}");
                    return false;
                }
            }, token);
        }

        /// <summary>
        /// Validate FFmpeg is available
        /// </summary>
        public bool ValidateFFmpegAvailable(string ffmpegPath)
        {
            Logger.DebugIf(LogPrefix,$"ValidateFFmpegAvailable - path: {ffmpegPath}");
            var result = FFmpegHelper.IsAvailable(ffmpegPath);
            Logger.DebugIf(LogPrefix,$"FFmpeg validation result: {result}");
            return result;
        }

        /// <summary>
        /// Check if file has already been precise-trimmed
        /// </summary>
        public bool IsAlreadyTrimmed(string filePath, string suffix)
        {
            if (string.IsNullOrWhiteSpace(suffix)) return false;

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var result = fileName.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) >= 0;
            if (result)
            {
                Logger.DebugIf(LogPrefix,$"File already trimmed: {fileName}");
            }
            return result;
        }
    }
}
