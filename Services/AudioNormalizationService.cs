using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Service for normalizing audio files using FFmpeg loudnorm filter (two-pass)
    /// </summary>
    public class AudioNormalizationService : INormalizationService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly ErrorHandlerService _errorHandler;
        private readonly IMusicPlaybackService _playbackService;
        private readonly string _backupBasePath;

        public AudioNormalizationService(ErrorHandlerService errorHandler = null, IMusicPlaybackService playbackService = null, string backupBasePath = null)
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
        /// Checks if a file is already normalized (has the normalization suffix).
        /// </summary>
        /// <param name="filePath">The file path to check.</param>
        /// <param name="suffix">The normalization suffix to look for.</param>
        /// <returns>True if the file is already normalized; otherwise, false.</returns>
        private bool IsFileAlreadyNormalized(string filePath, string suffix)
        {
            if (string.IsNullOrWhiteSpace(suffix)) return false;

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            // Check if filename contains the normalization suffix
            return fileName.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Builds output filename by simply appending the normalization suffix.
        /// Simple approach: always append suffix to current filename.
        /// Examples:
        /// - "song.mp3" -> "song-normalized.mp3"
        /// - "song-trimmed.mp3" -> "song-trimmed-normalized.mp3"
        /// </summary>
        private string BuildNormalizedFileName(string baseFileName, string normalizeSuffix)
        {
            return $"{baseFileName}{normalizeSuffix}";
        }

        /// <summary>
        /// Gets the duration of an audio file in seconds using FFmpeg
        /// </summary>
        private async Task<double?> GetAudioDurationAsync(string filePath, string ffmpegPath, CancellationToken cancellationToken)
        {
            try
            {
                var args = $"-i \"{filePath}\" -hide_banner";

                var processInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
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

                    await Task.Run(async () =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { process.Kill(); } catch { }
                            throw new OperationCanceledException();
                        }

                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();

                        if (!process.WaitForExit(30000)) // 30 second timeout
                        {
                            try { process.Kill(); } catch { }
                            return;
                        }

                        await outputTask.ConfigureAwait(false);
                        standardError = await errorTask.ConfigureAwait(false);
                    }, cancellationToken);

                    // Parse duration from stderr (FFmpeg outputs file info to stderr)
                    // Format: Duration: 00:46:23.45, start: 0.000000, bitrate: 320 kb/s
                    var durationMatch = System.Text.RegularExpressions.Regex.Match(standardError, @"Duration:\s*(\d{2}):(\d{2}):(\d{2}\.\d{2})");
                    if (durationMatch.Success)
                    {
                        var hours = int.Parse(durationMatch.Groups[1].Value);
                        var minutes = int.Parse(durationMatch.Groups[2].Value);
                        var seconds = double.Parse(durationMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);

                        var totalSeconds = hours * 3600 + minutes * 60 + seconds;
                        Logger.Debug($"Detected audio duration: {totalSeconds:F2} seconds ({hours:D2}:{minutes:D2}:{seconds:F2})");
                        return totalSeconds;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Error detecting audio duration: {filePath}");
            }

            return null;
        }

        /// <summary>
        /// Sanitizes filename by removing or replacing problematic characters for FFmpeg
        /// </summary>
        private string SanitizeFilenameForFFmpeg(string fileName)
        {
            // Characters that commonly cause issues with FFmpeg command-line parsing
            var problematicChars = new[] { '[', ']', '(', ')', '{', '}', '\'', '"', '`', '&', '|', ';', '<', '>', '!', '$', '#', '%' };

            var sanitized = fileName;
            foreach (var ch in problematicChars)
            {
                sanitized = sanitized.Replace(ch, '_');
            }

            // Remove multiple consecutive underscores
            while (sanitized.Contains("__"))
            {
                sanitized = sanitized.Replace("__", "_");
            }

            return sanitized;
        }

        /// <summary>
        /// Checks if a filename contains problematic characters for FFmpeg
        /// </summary>
        private bool HasProblematicCharacters(string fileName)
        {
            var problematicChars = new[] { '[', ']', '(', ')', '{', '}', '\'', '"', '`', '&', '|', ';', '<', '>', '!', '$', '#', '%' };
            return problematicChars.Any(ch => fileName.Contains(ch));
        }

        /// <summary>
        /// Log normalization verification info (before/after loudness measurements)
        /// </summary>
        private void LogNormalizationVerification(string filePath, LoudnormMeasurements before, LoudnormMeasurements after, double targetLoudness)
        {
            try
            {
                Logger.Info($"=== Normalization Verification for: {Path.GetFileName(filePath)} ===");
                Logger.Info($"Target Loudness: {targetLoudness:F1} LUFS");
                Logger.Info($"Before Normalization:");
                Logger.Info($"  - Integrated Loudness (I): {before.MeasuredI:F3} LUFS");
                Logger.Info($"  - True Peak (TP): {before.MeasuredTP:F3} dBTP");
                Logger.Info($"  - Loudness Range (LRA): {before.MeasuredLRA:F3} LU");
                
                // After normalization, we can't easily measure without re-running analysis
                Logger.Info($"After Normalization (Target):");
                Logger.Info($"  - Integrated Loudness (I): {targetLoudness:F1} LUFS (target)");
                Logger.Info($"  - True Peak (TP): Should be within acceptable range");
                Logger.Info($"=== Change: {Math.Abs(before.MeasuredI - targetLoudness):F3} LUFS difference ===");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error logging normalization verification");
            }
        }

        public async Task<bool> NormalizeFileAsync(
            string filePath,
            NormalizationSettings settings,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Logger.Error($"File does not exist: {filePath}");
                return false;
            }

            if (settings.SkipAlreadyNormalized && IsFileAlreadyNormalized(filePath, settings.NormalizationSuffix))
            {
                Logger.Info($"Skipping already-normalized file: {filePath}");
                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = Path.GetFileName(filePath),
                    Status = "Skipped (already normalized)"
                });
                return true;
            }

            // Stop music playback to prevent file locking issues
            try
            {
                if (_playbackService != null && _playbackService.IsPlaying)
                {
                    Logger.Info($"Stopping music playback before normalizing: {Path.GetFileName(filePath)}");
                    _playbackService.Stop();
                    await Task.Delay(200, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Error stopping playback before normalization: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(settings.FFmpegPath) || !ValidateFFmpegAvailable(settings.FFmpegPath))
            {
                Logger.Error($"FFmpeg not available at: {settings.FFmpegPath}");
                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = Path.GetFileName(filePath),
                    Status = "Error: FFmpeg not available"
                });
                return false;
            }

            try
            {
                var fileName = Path.GetFileName(filePath);
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);

                // Check if filename has problematic characters and needs sanitization
                string workingFilePath = filePath;
                bool needsRename = HasProblematicCharacters(fileNameWithoutExt);

                if (needsRename)
                {
                    var directory = Path.GetDirectoryName(filePath);
                    var extension = Path.GetExtension(filePath);
                    var sanitizedName = SanitizeFilenameForFFmpeg(fileNameWithoutExt);
                    var sanitizedPath = Path.Combine(directory, $"{sanitizedName}{extension}");

                    Logger.Info($"Renaming file with problematic characters: '{fileName}' -> '{Path.GetFileName(sanitizedPath)}'");
                    progress?.Report(new NormalizationProgress
                    {
                        CurrentFile = fileName,
                        Status = "Sanitizing filename..."
                    });

                    try
                    {
                        File.Move(filePath, sanitizedPath);
                        workingFilePath = sanitizedPath;
                        fileName = Path.GetFileName(sanitizedPath);
                        Logger.Info($"File renamed successfully to: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, $"Failed to rename file: {ex.Message}. Attempting normalization with original name...");
                        workingFilePath = filePath;
                    }
                }

                // Detect audio duration to warn about long files
                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = fileName,
                    Status = "Detecting audio duration..."
                });

                var durationSeconds = await GetAudioDurationAsync(workingFilePath, settings.FFmpegPath, cancellationToken);
                if (durationSeconds.HasValue && durationSeconds.Value > 600) // Warn if > 10 minutes
                {
                    var durationMinutes = (int)(durationSeconds.Value / 60);
                    Logger.Warn($"Long audio file detected: {fileName} is {durationMinutes} minutes long. Normalization may take several minutes.");
                    progress?.Report(new NormalizationProgress
                    {
                        CurrentFile = fileName,
                        Status = $"Warning: Long file ({durationMinutes} min) - this may take a while..."
                    });

                    // Give user time to see the warning
                    await Task.Delay(2000, cancellationToken);
                }

                // First pass: Analyze audio (get before measurements)
                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = fileName,
                    Status = "Analyzing audio..."
                });

                var beforeMeasurements = await AnalyzeAudioAsync(workingFilePath, settings, cancellationToken);
                if (beforeMeasurements == null)
                {
                    Logger.Error($"Failed to analyze audio: {workingFilePath}");
                    progress?.Report(new NormalizationProgress
                    {
                        CurrentFile = fileName,
                        Status = "Error: Analysis failed"
                    });
                    return false;
                }

                Logger.Info($"Before normalization - File: {fileName}, Measured I: {beforeMeasurements.MeasuredI:F3} LUFS, Target: {settings.TargetLoudness:F1} LUFS");

                // Second pass: Apply normalization
                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = fileName,
                    Status = "Normalizing audio..."
                });

                var success = await ApplyNormalizationAsync(workingFilePath, settings, beforeMeasurements, cancellationToken);
                
                if (success)
                {
                    Logger.Info($"Normalization completed - File: {fileName}, Target achieved: {settings.TargetLoudness:F1} LUFS");
                }
                
                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = fileName,
                    Status = success ? "Completed" : "Failed"
                });

                return success;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error normalizing file: {filePath}");
                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = Path.GetFileName(filePath),
                    Status = $"Error: {ex.Message}"
                });
                return false;
            }
        }

        public async Task<NormalizationResult> NormalizeBulkAsync(
            IEnumerable<string> filePaths,
            NormalizationSettings settings,
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

            Logger.Info($"Starting bulk normalization: {result.TotalFiles} files");

            for (int i = 0; i < files.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Info("Normalization cancelled by user");
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
                    var success = await NormalizeFileAsync(filePath, settings, progress, cancellationToken);
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
                    Logger.Error(ex, $"Error in bulk normalization for file: {filePath}");
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

            Logger.Info($"Bulk normalization complete: {result.SuccessCount} succeeded, {result.FailureCount} failed");
            return result;
        }

        public async Task<NormalizationResult> RestoreFromBackupsAsync(
            IEnumerable<string> filePathsOrDirectories,
            string normalizationSuffix,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken)
        {
            var result = new NormalizationResult();
            var suffix = normalizationSuffix ?? "-normalized";
            
            string preservedOriginalsDir;
            if (!string.IsNullOrEmpty(_backupBasePath))
            {
                preservedOriginalsDir = Path.Combine(_backupBasePath, Constants.PreservedOriginalsFolderName);
            }
            else
            {
                preservedOriginalsDir = Path.Combine(Path.GetTempPath(), Constants.PreservedOriginalsFolderName);
            }
            
            var normalizedFiles = new List<string>();
            foreach (var path in filePathsOrDirectories ?? new List<string>())
            {
                if (File.Exists(path))
                {
                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(path);
                    if (fileNameWithoutExt.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedFiles.Add(path);
                    }
                }
                else if (Directory.Exists(path))
                {
                    var musicFiles = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f =>
                        {
                            if (!Constants.SupportedAudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                return false;
                            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(f);
                            return fileNameWithoutExt.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
                        })
                        .ToList();
                    normalizedFiles.AddRange(musicFiles);
                }
            }
            
            result.TotalFiles = normalizedFiles.Count;

            if (result.TotalFiles == 0)
            {
                return result;
            }

            Logger.Info($"Starting restoration: {result.TotalFiles} normalized files");

            for (int i = 0; i < normalizedFiles.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Info("Restore cancelled by user");
                    break;
                }

                var normalizedFilePath = normalizedFiles[i];
                result.CurrentIndex = i + 1;
                var fileName = Path.GetFileName(normalizedFilePath);

                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = fileName,
                    CurrentIndex = i + 1,
                    TotalFiles = normalizedFiles.Count,
                    SuccessCount = result.SuccessCount,
                    FailureCount = result.FailureCount,
                    Status = $"Processing {i + 1} of {normalizedFiles.Count}..."
                });

                try
                {
                    try
                    {
                        if (_playbackService != null && _playbackService.IsPlaying)
                        {
                            _playbackService.Stop();
                            await Task.Delay(200, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, $"Error stopping playback before restore: {ex.Message}");
                    }

                    var directory = Path.GetDirectoryName(normalizedFilePath);
                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(normalizedFilePath);
                    var extension = Path.GetExtension(normalizedFilePath);
                    
                    // Remove suffix to get original filename
                    var originalFileNameWithoutExt = fileNameWithoutExt.Substring(0, fileNameWithoutExt.Length - suffix.Length);
                    var originalFileName = $"{originalFileNameWithoutExt}{extension}";
                    
                    var gameFolderName = Path.GetFileName(directory);
                    var gamePreservedDir = Path.Combine(preservedOriginalsDir, gameFolderName);
                    var preservedOriginalPath = Path.Combine(gamePreservedDir, originalFileName);
                    var originalFilePath = Path.Combine(directory, originalFileName);

                    if (!File.Exists(preservedOriginalPath))
                    {
                        Logger.Warn($"Preserved original file not found: {preservedOriginalPath}");
                        result.FailureCount++;
                        result.FailedFiles.Add(normalizedFilePath);
                        progress?.Report(new NormalizationProgress
                        {
                            CurrentFile = fileName,
                            Status = "Original file not found in preserved originals"
                        });
                        continue;
                    }

                    try
                    {
                        File.Delete(normalizedFilePath);
                        File.Move(preservedOriginalPath, originalFilePath);
                        Logger.Info($"Restored original file: {originalFilePath} (from preserved originals: {preservedOriginalPath})");
                        result.SuccessCount++;
                        progress?.Report(new NormalizationProgress
                        {
                            CurrentFile = fileName,
                            Status = "Restored successfully"
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to restore file: {normalizedFilePath}");
                        result.FailureCount++;
                        result.FailedFiles.Add(normalizedFilePath);
                        progress?.Report(new NormalizationProgress
                        {
                            CurrentFile = fileName,
                            Status = $"Error: {ex.Message}"
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error in restore for file: {normalizedFilePath}");
                    result.FailureCount++;
                    result.FailedFiles.Add(normalizedFilePath);
                }
            }

            result.IsComplete = true;
            progress?.Report(new NormalizationProgress
            {
                IsComplete = true,
                TotalFiles = result.TotalFiles,
                SuccessCount = result.SuccessCount,
                FailureCount = result.FailureCount,
                Status = $"Complete: {result.SuccessCount} restored, {result.FailureCount} failed"
            });

            Logger.Info($"Restore complete: {result.SuccessCount} restored, {result.FailureCount} failed");
            return result;
        }

        /// <summary>
        /// First pass: Analyze audio to get loudness measurements
        /// </summary>
        private async Task<LoudnormMeasurements> AnalyzeAudioAsync(
            string filePath,
            NormalizationSettings settings,
            CancellationToken cancellationToken)
        {
            try
            {
                // Use InvariantCulture to ensure decimal separator is always '.' (not ',' in some locales)
                var args = $"-i \"{filePath}\" -af loudnorm=I={settings.TargetLoudness.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}:TP={settings.TruePeak.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}:LRA={settings.LoudnessRange.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}:print_format=json -f null -";

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

                    // Read output asynchronously to avoid deadlock
                    string standardOutput = null;
                    string standardError = null;

                    await Task.Run(async () =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { process.Kill(); } catch { }
                            throw new OperationCanceledException();
                        }

                        // Read output and error streams asynchronously to prevent deadlock
                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();

                        if (!process.WaitForExit(300000)) // 5 minute timeout
                        {
                            try { process.Kill(); } catch { }
                            throw new TimeoutException("FFmpeg analysis timed out after 5 minutes");
                        }

                        // Await the async reads with a timeout
                        standardOutput = await outputTask.ConfigureAwait(false);
                        standardError = await errorTask.ConfigureAwait(false);

                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException();
                        }
                    }, cancellationToken);

                    if (process.ExitCode != 0)
                    {
                        Logger.Error($"FFmpeg analysis failed (exit code {process.ExitCode}):\nOutput: {standardOutput}\nError: {standardError}");
                        return null;
                    }

                    // Parse JSON output from stderr (FFmpeg outputs loudnorm JSON to stderr)
                    // Log the full output for debugging
                    Logger.Debug($"FFmpeg analysis stderr output:\n{standardError}");
                    Logger.Debug($"FFmpeg analysis stdout output:\n{standardOutput}");
                    
                    return ParseLoudnormJson(standardError);
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Audio analysis cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error analyzing audio: {filePath}");
                return null;
            }
        }

        /// <summary>
        /// Parses loudnorm JSON output from FFmpeg.
        /// FFmpeg outputs JSON directly to stderr with root-level properties.
        /// </summary>
        /// <param name="jsonOutput">The JSON output string from FFmpeg stderr.</param>
        /// <returns>Parsed loudness measurements, or null if parsing fails.</returns>
        private LoudnormMeasurements ParseLoudnormJson(string jsonOutput)
        {
            try
            {
                // FFmpeg outputs JSON on stderr - find the JSON block (may be single or multiple lines)
                var jsonStart = jsonOutput.IndexOf('{');
                var jsonEnd = jsonOutput.LastIndexOf('}');
                
                if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
                {
                    Logger.Error($"Could not find JSON in FFmpeg output. Full output: {jsonOutput}");
                    return null;
                }

                var jsonString = jsonOutput.Substring(jsonStart, jsonEnd - jsonStart + 1);
                Logger.Debug($"Extracted JSON string: {jsonString}");
                
                var json = JObject.Parse(jsonString);

                // FFmpeg loudnorm JSON structure (root level, not nested under "input")
                // Properties: input_i, input_tp, input_lra, input_thresh, target_offset
                var inputI = json["input_i"]?.Value<string>();
                var inputTP = json["input_tp"]?.Value<string>();
                var inputLRA = json["input_lra"]?.Value<string>();
                var inputThresh = json["input_thresh"]?.Value<string>();
                var targetOffset = json["target_offset"]?.Value<string>();

                if (string.IsNullOrEmpty(inputI) || string.IsNullOrEmpty(inputTP) || 
                    string.IsNullOrEmpty(inputLRA) || string.IsNullOrEmpty(inputThresh))
                {
                    Logger.Error($"Missing required values in JSON. JSON: {jsonString}");
                    return null;
                }

                // FFmpeg outputs values as strings - parse to double
                var measurements = new LoudnormMeasurements
                {
                    MeasuredI = double.Parse(inputI, System.Globalization.CultureInfo.InvariantCulture),
                    MeasuredTP = double.Parse(inputTP, System.Globalization.CultureInfo.InvariantCulture),
                    MeasuredLRA = double.Parse(inputLRA, System.Globalization.CultureInfo.InvariantCulture),
                    MeasuredThreshold = double.Parse(inputThresh, System.Globalization.CultureInfo.InvariantCulture),
                    Offset = !string.IsNullOrEmpty(targetOffset) 
                        ? double.Parse(targetOffset, System.Globalization.CultureInfo.InvariantCulture) 
                        : 0
                };

                Logger.Info($"Parsed loudnorm measurements - I: {measurements.MeasuredI}, TP: {measurements.MeasuredTP}, LRA: {measurements.MeasuredLRA}, Thresh: {measurements.MeasuredThreshold}, Offset: {measurements.Offset}");
                
                return measurements;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error parsing loudnorm JSON. Output: {jsonOutput}");
                return null;
            }
        }

        /// <summary>
        /// Second pass: Applies normalization using measurements from first pass.
        /// Creates normalized file and optionally preserves original.
        /// </summary>
        private async Task<bool> ApplyNormalizationAsync(
            string filePath,
            NormalizationSettings settings,
            LoudnormMeasurements measurements,
            CancellationToken cancellationToken)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath);
                
                string finalOutputPath;
                string preservedOriginalPath = null;
                var normalizeSuffix = settings.NormalizationSuffix ?? "-normalized";

                if (settings.DoNotPreserveOriginals)
                {
                    // Space saver mode: Replace original file directly
                    // Simply append normalization suffix to current filename
                    var outputFileName = BuildNormalizedFileName(fileName, normalizeSuffix);
                    finalOutputPath = Path.Combine(directory, $"{outputFileName}{extension}");
                }
                else
                {
                    // Preservation mode: Create normalized file with suffix
                    // Simply append normalization suffix to current filename
                    var outputFileName = BuildNormalizedFileName(fileName, normalizeSuffix);
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
                
                var tempPath = Path.Combine(directory, $"{fileName}.normalized.tmp{extension}");

                // Build loudnorm filter with measurements from first pass.
                // Include offset if available (recommended for accurate normalization).
                // Use InvariantCulture to ensure decimal separator is always '.' (not ',' in some locales)
                var invariantCulture = System.Globalization.CultureInfo.InvariantCulture;
                var offsetParam = measurements.Offset != 0 
                    ? $":offset={measurements.Offset.ToString("F3", invariantCulture)}"
                    : "";
                    
                var loudnormFilter = $"loudnorm=I={settings.TargetLoudness.ToString("F1", invariantCulture)}:TP={settings.TruePeak.ToString("F1", invariantCulture)}:LRA={settings.LoudnessRange.ToString("F1", invariantCulture)}:" +
                                   $"measured_I={measurements.MeasuredI.ToString("F3", invariantCulture)}:measured_TP={measurements.MeasuredTP.ToString("F3", invariantCulture)}:" +
                                   $"measured_LRA={measurements.MeasuredLRA.ToString("F3", invariantCulture)}:measured_thresh={measurements.MeasuredThreshold.ToString("F3", invariantCulture)}{offsetParam}:" +
                                   "linear=true";

                // Use 48kHz sample rate (recommended for loudnorm filter)
                var args = $"-i \"{filePath}\" -af \"{loudnormFilter}\" -ar 48000 -c:a {settings.AudioCodec} -y \"{tempPath}\"";
                
                Logger.Info($"Normalization command: {settings.FFmpegPath} {args}");

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

                    await Task.Run(async () =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { process.Kill(); } catch { }
                            throw new OperationCanceledException();
                        }

                        // Read output and error streams asynchronously to prevent deadlock
                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();

                        if (!process.WaitForExit(600000)) // 10 minute timeout for normalization
                        {
                            try { process.Kill(); } catch { }
                            throw new TimeoutException("FFmpeg normalization timed out after 10 minutes");
                        }

                        // Await the async reads
                        standardOutput = await outputTask.ConfigureAwait(false);
                        standardError = await errorTask.ConfigureAwait(false);

                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException();
                        }
                    }, cancellationToken);

                    if (process.ExitCode != 0)
                    {
                        Logger.Error($"FFmpeg normalization failed (exit code {process.ExitCode}):\nOutput: {standardOutput}\nError: {standardError}");
                        try { File.Delete(tempPath); } catch { }
                        return false;
                    }
                    
                    Logger.Debug($"Normalization process completed successfully. Output: {standardOutput}");

                    if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                    {
                        Logger.Error($"Normalized file is missing or empty: {tempPath}");
                        try { File.Delete(tempPath); } catch { }
                        return false;
                    }

                    try
                    {
                        if (settings.DoNotPreserveOriginals)
                        {
                            // Space saver mode: Replace original file directly
                            File.Delete(filePath);
                            File.Move(tempPath, finalOutputPath); // Move normalized file to original location
                            Logger.Info($"Successfully normalized file (replaced original): {finalOutputPath}");
                        }
                        else
                        {
                            // Preservation mode: Create normalized file with suffix, move original to preserved folder
                            // First move normalized file to final location (with suffix)
                            if (File.Exists(finalOutputPath))
                            {
                                File.Delete(finalOutputPath);
                            }
                            File.Move(tempPath, finalOutputPath);
                            Logger.Info($"Successfully created normalized file: {finalOutputPath}");
                            
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
                        Logger.Error(ex, $"Failed to move normalized file to final location: {finalOutputPath}");
                        // Clean up temp file
                        try { File.Delete(tempPath); } catch { }
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Normalization cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error applying normalization: {filePath}");
                return false;
            }
        }
    }
}