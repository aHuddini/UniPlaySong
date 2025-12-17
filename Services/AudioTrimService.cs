using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Service for trimming leading silence from audio files using FFmpeg
    /// </summary>
    public class AudioTrimService : ITrimService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly ErrorHandlerService _errorHandler;
        private readonly IMusicPlaybackService _playbackService;
        private readonly string _backupBasePath;

        public AudioTrimService(ErrorHandlerService errorHandler = null, IMusicPlaybackService playbackService = null, string backupBasePath = null)
        {
            _errorHandler = errorHandler;
            _playbackService = playbackService;
            _backupBasePath = backupBasePath;
        }

        public bool ValidateFFmpegAvailable(string ffmpegPath)
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

        /// <summary>
        /// Checks if a file is already trimmed (has the trim suffix).
        /// </summary>
        /// <param name="filePath">The file path to check.</param>
        /// <param name="suffix">The trim suffix to look for.</param>
        /// <returns>True if the file is already trimmed; otherwise, false.</returns>
        private bool IsFileAlreadyTrimmed(string filePath, string suffix)
        {
            if (string.IsNullOrWhiteSpace(suffix)) return false;
            
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            // Check for exact suffix or combined suffixes (e.g., "-trimmed" or "-normalized-trimmed")
            return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                   fileName.IndexOf("-trimmed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Strips existing suffixes from filename and returns base name
        /// </summary>
        private string StripSuffixes(string fileName, string trimSuffix = "-trimmed", string normalizeSuffix = "-normalized")
        {
            var result = fileName;
            
            // Remove combined suffixes (order matters - check both orders)
            if (result.EndsWith($"{normalizeSuffix}{trimSuffix}", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(0, result.Length - (normalizeSuffix.Length + trimSuffix.Length));
            }
            else if (result.EndsWith($"{trimSuffix}{normalizeSuffix}", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(0, result.Length - (trimSuffix.Length + normalizeSuffix.Length));
            }
            // Remove individual suffixes
            else if (result.EndsWith(normalizeSuffix, StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(0, result.Length - normalizeSuffix.Length);
            }
            else if (result.EndsWith(trimSuffix, StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(0, result.Length - trimSuffix.Length);
            }
            
            return result;
        }

        /// <summary>
        /// Checks if filename has a specific suffix (including in combined form)
        /// </summary>
        private bool HasSuffix(string fileName, string suffix)
        {
            return fileName.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Builds output filename with appropriate suffix based on existing suffixes.
        /// Suffix order reflects chronological order of operations:
        /// - If normalized first, then trimmed: "song-normalized-trimmed"
        /// - If trimmed first, then normalized: "song-trimmed-normalized"
        /// </summary>
        private string BuildOutputFileName(string baseFileName, string newSuffix, string trimSuffix = "-trimmed", string normalizeSuffix = "-normalized")
        {
            // Strip all existing suffixes to get base name
            var cleanName = StripSuffixes(baseFileName, trimSuffix, normalizeSuffix);
            
            // Check what suffixes already exist (before stripping)
            bool hasTrimmed = HasSuffix(baseFileName, trimSuffix);
            bool hasNormalized = HasSuffix(baseFileName, normalizeSuffix);
            
            // Build new filename with suffixes reflecting chronological order of operations
            if (newSuffix == trimSuffix)
            {
                // Adding trim operation
                if (hasNormalized && !hasTrimmed)
                {
                    // File was normalized first, now trimming: "song-normalized-trimmed"
                    return $"{cleanName}{normalizeSuffix}{trimSuffix}";
                }
                // If already trimmed, return as-is (should be skipped)
                else if (hasTrimmed)
                {
                    return baseFileName; // Shouldn't happen if skip logic works
                }
                // Just add trim (no existing operations)
                else
                {
                    return $"{cleanName}{trimSuffix}";
                }
            }
            else if (newSuffix == normalizeSuffix)
            {
                // Adding normalization operation
                if (hasTrimmed && !hasNormalized)
                {
                    // File was trimmed first, now normalizing: "song-trimmed-normalized"
                    return $"{cleanName}{trimSuffix}{normalizeSuffix}";
                }
                // If already normalized, return as-is (should be skipped)
                else if (hasNormalized)
                {
                    return baseFileName; // Shouldn't happen if skip logic works
                }
                // Just add normalization (no existing operations)
                else
                {
                    return $"{cleanName}{normalizeSuffix}";
                }
            }
            
            // Fallback: just append new suffix
            return $"{cleanName}{newSuffix}";
        }

        /// <summary>
        /// Detects leading silence in an audio file using FFmpeg silencedetect filter
        /// </summary>
        /// <param name="filePath">Path to the audio file</param>
        /// <param name="settings">Trim settings</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Silence end time in seconds if found at start, null otherwise</returns>
        private async Task<double?> DetectLeadingSilenceAsync(
            string filePath,
            TrimSettings settings,
            CancellationToken cancellationToken)
        {
            try
            {
                var args = $"-i \"{filePath}\" -af silencedetect=noise={settings.SilenceThreshold:F1}dB:d={settings.SilenceDuration:F2} -f null -";

                var processInfo = new ProcessStartInfo
                {
                    FileName = settings.FFmpegPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = new Process())
                {
                    process.StartInfo = processInfo;
                    process.Start();

                    string standardError = null;

                    await Task.Run(() =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { process.Kill(); } catch { }
                            throw new OperationCanceledException();
                        }

                        standardError = process.StandardError.ReadToEnd();
                        process.StandardOutput.ReadToEnd(); // Consume stdout

                        if (!process.WaitForExit(300000)) // 5 minute timeout
                        {
                            try { process.Kill(); } catch { }
                            throw new TimeoutException("FFmpeg silence detection timed out after 5 minutes");
                        }

                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException();
                        }
                    }, cancellationToken);

                    if (process.ExitCode != 0)
                    {
                        Logger.Error($"FFmpeg silence detection failed (exit code {process.ExitCode}):\nError: {standardError}");
                        return null;
                    }

                    // Parse silence detection output
                    // Format: [silencedetect @ 0x...] silence_start: 0.0
                    //         [silencedetect @ 0x...] silence_end: 5.234 | silence_duration: 5.234
                    var silenceStartPattern = new Regex(@"silence_start:\s*([\d.]+)");
                    var silenceEndPattern = new Regex(@"silence_end:\s*([\d.]+)");
                    var silenceDurationPattern = new Regex(@"silence_duration:\s*([\d.]+)");

                    var silenceStartMatch = silenceStartPattern.Match(standardError);
                    var silenceEndMatch = silenceEndPattern.Match(standardError);
                    var silenceDurationMatch = silenceDurationPattern.Match(standardError);

                    if (silenceStartMatch.Success && silenceEndMatch.Success)
                    {
                        var silenceStart = double.Parse(silenceStartMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                        var silenceEnd = double.Parse(silenceEndMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                        var silenceDuration = silenceEnd - silenceStart;

                        // Check if silence starts at the beginning (within 0.1 seconds of start)
                        if (silenceStart <= 0.1)
                        {
                            Logger.Info($"Detected leading silence: {silenceDuration:F3} seconds (start: {silenceStart:F3}, end: {silenceEnd:F3})");
                            // Return silence end time (not duration) so we can add buffer
                            return silenceEnd;
                        }
                        else
                        {
                            Logger.Debug($"Silence detected but not at start: starts at {silenceStart:F3} seconds");
                            return null;
                        }
                    }
                    else if (silenceDurationMatch.Success)
                    {
                        // Fallback: use duration directly if available
                        var duration = double.Parse(silenceDurationMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                        if (silenceStartMatch.Success)
                        {
                            var silenceStart = double.Parse(silenceStartMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                            if (silenceStart <= 0.1)
                            {
                                // Calculate end time from start + duration
                                var silenceEnd = silenceStart + duration;
                                Logger.Info($"Detected leading silence: {duration:F3} seconds (end: {silenceEnd:F3})");
                                return silenceEnd;
                            }
                        }
                    }

                    Logger.Debug($"No leading silence detected in file: {Path.GetFileName(filePath)}");
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Silence detection cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error detecting silence: {filePath}");
                return null;
            }
        }

        /// <summary>
        /// Determines if a file should be skipped based on trim settings
        /// </summary>
        private bool ShouldSkipFile(string filePath, TrimSettings settings, double? silenceEndTime)
        {
            // Check if already trimmed
            if (settings.SkipAlreadyTrimmed && IsFileAlreadyTrimmed(filePath, settings.TrimSuffix))
            {
                Logger.Info($"Skipping already-trimmed file: {filePath}");
                return true;
            }

            // Check if silence is too short to trim
            // silenceEndTime is the end time, so duration is approximately silenceEndTime (since start is ~0)
            if (silenceEndTime.HasValue && silenceEndTime.Value < settings.MinSilenceToTrim)
            {
                Logger.Info($"Skipping file with short silence ({silenceEndTime.Value:F3}s < {settings.MinSilenceToTrim:F3}s): {filePath}");
                return true;
            }

            // Check if no silence detected
            if (!silenceEndTime.HasValue)
            {
                Logger.Info($"Skipping file with no leading silence: {filePath}");
                return true;
            }

            return false;
        }

        public async Task<bool> TrimFileAsync(
            string filePath,
            TrimSettings settings,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Logger.Error($"File does not exist: {filePath}");
                return false;
            }

            var fileName = Path.GetFileName(filePath);

            // Stop music playback to prevent file locking issues
            try
            {
                if (_playbackService != null && _playbackService.IsPlaying)
                {
                    Logger.Info($"Stopping music playback before trimming: {fileName}");
                    _playbackService.Stop();
                    await Task.Delay(200, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Error stopping playback before trim: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(settings.FFmpegPath) || !ValidateFFmpegAvailable(settings.FFmpegPath))
            {
                Logger.Error($"FFmpeg not available at: {settings.FFmpegPath}");
                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = fileName,
                    Status = "Error: FFmpeg not available"
                });
                return false;
            }

            try
            {
                // Step 1: Detect leading silence
                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = fileName,
                    Status = "Detecting silence..."
                });

                var silenceEndTime = await DetectLeadingSilenceAsync(filePath, settings, cancellationToken);

                // Step 2: Check if should skip
                if (ShouldSkipFile(filePath, settings, silenceEndTime))
                {
                    var skipReason = IsFileAlreadyTrimmed(filePath, settings.TrimSuffix) 
                        ? "Skipped (already trimmed)" 
                        : silenceEndTime.HasValue && silenceEndTime.Value < settings.MinSilenceToTrim
                            ? $"Skipped (silence too short: {silenceEndTime.Value:F3}s)"
                            : "Skipped (no leading silence)";

                    progress?.Report(new NormalizationProgress
                    {
                        CurrentFile = fileName,
                        Status = skipReason
                    });
                    return true; // Successfully skipped
                }

                // Step 3: Trim the file
                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = fileName,
                    Status = "Trimming silence..."
                });

                var success = await ApplyTrimAsync(filePath, settings, silenceEndTime.Value, cancellationToken);

                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = fileName,
                    Status = success ? "Completed" : "Failed"
                });

                return success;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error trimming file: {filePath}");
                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = fileName,
                    Status = $"Error: {ex.Message}"
                });
                return false;
            }
        }

        /// <summary>
        /// Applies trim operation using FFmpeg
        /// </summary>
        private async Task<bool> ApplyTrimAsync(
            string filePath,
            TrimSettings settings,
            double silenceEndTime,
            CancellationToken cancellationToken)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath);
                
                string finalOutputPath;
                string preservedOriginalPath = null;
                
                if (settings.DoNotPreserveOriginals)
                {
                    // Space saver mode: Replace original file directly, but still add suffix if file was already processed
                    // Check if file has existing suffixes and preserve them
                    var trimSuffix = settings.TrimSuffix ?? "-trimmed";
                    var normalizeSuffix = "-normalized";
                    var outputFileName = BuildOutputFileName(fileName, trimSuffix, trimSuffix, normalizeSuffix);
                    finalOutputPath = Path.Combine(directory, $"{outputFileName}{extension}");
                }
                else
                {
                    // Preservation mode: Create trimmed file with suffix
                    // Check if file already has "-normalized" suffix and append "-trimmed" appropriately
                    var trimSuffix = settings.TrimSuffix ?? "-trimmed";
                    var normalizeSuffix = "-normalized"; // Default normalization suffix
                    var outputFileName = BuildOutputFileName(fileName, trimSuffix, trimSuffix, normalizeSuffix);
                    finalOutputPath = Path.Combine(directory, $"{outputFileName}{extension}");
                    
                    // Determine preserved originals directory
                    string preservedOriginalsDir;
                    if (!string.IsNullOrEmpty(_backupBasePath))
                    {
                        preservedOriginalsDir = Path.Combine(_backupBasePath, Constants.PreservedOriginalsFolderName);
                    }
                    else
                    {
                        var parentDir = Directory.GetParent(directory)?.FullName ?? directory;
                        preservedOriginalsDir = Path.Combine(parentDir, Constants.PreservedOriginalsFolderName);
                    }
                    
                    // Preserve relative structure in preserved originals folder (game folder name)
                    var gameFolderName = Path.GetFileName(directory);
                    var gamePreservedDir = Path.Combine(preservedOriginalsDir, gameFolderName);
                    Directory.CreateDirectory(gamePreservedDir);
                    preservedOriginalPath = Path.Combine(gamePreservedDir, $"{fileName}{extension}");
                }
                
                var tempPath = Path.Combine(directory, $"{fileName}.trimmed.tmp{extension}");

                // Use FFmpeg's silenceremove filter - designed specifically for removing silence
                // This is better than -ss because it:
                // 1. Automatically finds the best trim point
                // 2. Ensures we're into actual audio before stopping (start_duration parameter)
                // 3. Works with audio filters to avoid clicks
                // 
                // Parameters:
                // - start_periods=1: Remove silence from the start until non-silence is detected
                // - start_duration: Minimum duration of non-silence required before stopping trim
                //   Use a very small value (0.01s) to avoid cutting into gradual fade-ins
                // - start_threshold: dB level below which audio is considered silence
                // - stop_periods=-1: Don't trim from the end (we only want to trim leading silence)
                // 
                // Note: start_duration should be small to avoid cutting into gradual fade-ins
                // The filter will still ensure we're past the silence, just with minimal buffer
                var startDurationSeconds = 0.005; // Very small duration (5ms) - minimal buffer to ensure we're past silence without cutting into audio
                var thresholdDb = settings.SilenceThreshold;
                var filter = $"silenceremove=start_periods=1:start_duration={startDurationSeconds:F2}:start_threshold={thresholdDb:F1}dB:stop_periods=-1";
                
                // Use audio filter approach - requires re-encoding but ensures quality and avoids clicks
                // For MP3, use libmp3lame with high quality settings (q:a 0 = highest quality)
                // For other formats, try to preserve codec if possible
                var extensionLower = extension.ToLower();
                string codecArgs;
                if (extensionLower == ".mp3")
                {
                    codecArgs = "-c:a libmp3lame -q:a 0"; // Highest quality MP3
                }
                else if (extensionLower == ".flac")
                {
                    codecArgs = "-c:a flac"; // Lossless FLAC
                }
                else if (extensionLower == ".wav")
                {
                    codecArgs = "-c:a pcm_s16le"; // Uncompressed WAV
                }
                else
                {
                    // For other formats, try to copy if possible, otherwise use aac
                    codecArgs = "-c:a copy";
                }
                
                var args = $"-i \"{filePath}\" -af \"{filter}\" {codecArgs} -y \"{tempPath}\"";
                
                Logger.Info($"Trim command: {settings.FFmpegPath} {args}");
                Logger.Info($"Using silenceremove filter: start_duration={startDurationSeconds:F2}s, threshold={thresholdDb:F1}dB");

                var processInfo = new ProcessStartInfo
                {
                    FileName = settings.FFmpegPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (var process = new Process())
                {
                    process.StartInfo = processInfo;
                    process.Start();

                    string standardOutput = null;
                    string standardError = null;

                    await Task.Run(() =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { process.Kill(); } catch { }
                            throw new OperationCanceledException();
                        }

                        standardOutput = process.StandardOutput.ReadToEnd();
                        standardError = process.StandardError.ReadToEnd();

                        if (!process.WaitForExit(300000)) // 5 minute timeout
                        {
                            try { process.Kill(); } catch { }
                            throw new TimeoutException("FFmpeg trim timed out after 5 minutes");
                        }

                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException();
                        }
                    }, cancellationToken);

                    if (process.ExitCode != 0)
                    {
                        Logger.Error($"FFmpeg trim failed (exit code {process.ExitCode}):\nOutput: {standardOutput}\nError: {standardError}");
                        try { File.Delete(tempPath); } catch { }
                        return false;
                    }
                    
                    Logger.Debug($"Trim process completed successfully. Output: {standardOutput}");

                    if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                    {
                        Logger.Error($"Trimmed file is missing or empty: {tempPath}");
                        try { File.Delete(tempPath); } catch { }
                        return false;
                    }

                    try
                    {
                        if (settings.DoNotPreserveOriginals)
                        {
                            // Space saver mode: Replace original file directly
                            File.Delete(filePath);
                            File.Move(tempPath, finalOutputPath);
                            Logger.Info($"Successfully trimmed file (replaced original): {finalOutputPath}");
                        }
                        else
                        {
                            // Preservation mode: Create trimmed file with suffix, move original to preserved folder
                            if (File.Exists(finalOutputPath))
                            {
                                File.Delete(finalOutputPath);
                            }
                            File.Move(tempPath, finalOutputPath);
                            Logger.Info($"Successfully created trimmed file: {finalOutputPath}");
                            
                            // Now move original file to preserved originals folder
                            if (File.Exists(preservedOriginalPath))
                            {
                                File.Delete(preservedOriginalPath);
                            }
                            File.Move(filePath, preservedOriginalPath);
                            Logger.Info($"Moved original file to preserved originals: {preservedOriginalPath}");
                        }
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to move trimmed file to final location: {finalOutputPath}");
                        // Clean up temp file
                        try { File.Delete(tempPath); } catch { }
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Trim cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error applying trim: {filePath}");
                return false;
            }
        }

        public async Task<NormalizationResult> TrimBulkAsync(
            IEnumerable<string> filePaths,
            TrimSettings settings,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken)
        {
            var result = new NormalizationResult();
            var files = filePaths?.ToList() ?? new List<string>();
            result.TotalFiles = files.Count;

            if (result.TotalFiles == 0)
            {
                return result;
            }

            Logger.Info($"Starting bulk trim: {result.TotalFiles} files");

            for (int i = 0; i < files.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Info("Trim cancelled by user");
                    break;
                }

                var filePath = files[i];
                result.CurrentIndex = i + 1;

                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = Path.GetFileName(filePath),
                    CurrentIndex = i + 1,
                    TotalFiles = files.Count,
                    SuccessCount = result.SuccessCount,
                    FailureCount = result.FailureCount,
                    Status = $"Processing {i + 1} of {files.Count}..."
                });

                try
                {
                    var success = await TrimFileAsync(filePath, settings, progress, cancellationToken);
                    if (success)
                    {
                        result.SuccessCount++;
                    }
                    else
                    {
                        result.FailureCount++;
                        result.FailedFiles.Add(filePath);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error in bulk trim for file: {filePath}");
                    result.FailureCount++;
                    result.FailedFiles.Add(filePath);
                }
            }

            result.IsComplete = true;
            progress?.Report(new NormalizationProgress
            {
                IsComplete = true,
                TotalFiles = result.TotalFiles,
                SuccessCount = result.SuccessCount,
                FailureCount = result.FailureCount,
                Status = $"Complete: {result.SuccessCount} succeeded, {result.FailureCount} failed"
            });

            Logger.Info($"Bulk trim complete: {result.SuccessCount} succeeded, {result.FailureCount} failed");
            return result;
        }
    }
}

