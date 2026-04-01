using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    // FFmpeg-based bulk audio format conversion
    public class AudioConversionService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly FileLogger _fileLogger;

        public AudioConversionService(FileLogger fileLogger = null)
        {
            _fileLogger = fileLogger;
        }

        public bool ValidateFFmpegAvailable(string ffmpegPath)
        {
            return FFmpegHelper.IsAvailable(ffmpegPath);
        }

        public async Task<ConversionResult> ConvertBulkAsync(
            IEnumerable<string> filePaths,
            string ffmpegPath,
            string targetFormat,
            string bitrate,
            bool keepOriginals,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken)
        {
            var result = new ConversionResult();
            var files = filePaths?.ToList() ?? new List<string>();
            result.TotalFiles = files.Count;

            if (result.TotalFiles == 0)
                return result;

            int maxParallelism = Math.Min(Environment.ProcessorCount, 3);

            var failedFiles = new ConcurrentBag<string>();
            int successCount = 0;
            int failureCount = 0;
            int completedCount = 0;
            long totalOriginalSize = 0;
            long totalNewSize = 0;

            progress?.Report(new NormalizationProgress
            {
                CurrentFile = "Starting parallel conversion...",
                CurrentIndex = 0,
                TotalFiles = files.Count,
                Status = $"Converting {files.Count} files with {maxParallelism} parallel threads..."
            });

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = cancellationToken
            };

            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(files, parallelOptions, (filePath, loopState) =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            loopState.Stop();
                            return;
                        }

                        try
                        {
                            var success = ConvertFileInternal(
                                filePath, ffmpegPath, targetFormat, bitrate,
                                keepOriginals, progress, cancellationToken,
                                out long originalSize, out long newSize);

                            if (success)
                            {
                                Interlocked.Increment(ref successCount);
                                Interlocked.Add(ref totalOriginalSize, originalSize);
                                Interlocked.Add(ref totalNewSize, newSize);
                            }
                            else
                            {
                                Interlocked.Increment(ref failureCount);
                                failedFiles.Add(filePath);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            loopState.Stop();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Error in parallel conversion for file: {filePath}");
                            Interlocked.Increment(ref failureCount);
                            failedFiles.Add(filePath);
                        }

                        int currentCompleted = Interlocked.Increment(ref completedCount);
                        progress?.Report(new NormalizationProgress
                        {
                            CurrentFile = Path.GetFileName(filePath),
                            CurrentIndex = currentCompleted,
                            TotalFiles = files.Count,
                            Status = $"Completed {currentCompleted} of {files.Count} ({maxParallelism} parallel)"
                        });
                    });
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // User cancelled
            }

            result.SuccessCount = successCount;
            result.FailureCount = failureCount;
            result.TotalOriginalBytes = totalOriginalSize;
            result.TotalNewBytes = totalNewSize;
            foreach (var file in failedFiles)
            {
                result.FailedFiles.Add(file);
            }

            progress?.Report(new NormalizationProgress
            {
                IsComplete = true,
                CurrentIndex = completedCount,
                TotalFiles = result.TotalFiles,
                Status = $"Complete: {result.SuccessCount} succeeded, {result.FailureCount} failed"
            });

            return result;
        }

        // Convert a single file, returning original and new sizes via out params
        private bool ConvertFileInternal(
            string filePath,
            string ffmpegPath,
            string targetFormat,
            string bitrate,
            bool keepOriginals,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken,
            out long originalSize,
            out long newSize)
        {
            originalSize = 0;
            newSize = 0;

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Logger.Error($"File does not exist: {filePath}");
                return false;
            }

            var currentExt = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            if (currentExt.Equals(targetFormat, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = Path.GetFileName(filePath),
                    Status = $"Skipped (already .{targetFormat})"
                });
                return true;
            }

            var directory = Path.GetDirectoryName(filePath);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            var tempFileName = $"{fileNameWithoutExt}.converting.{targetFormat}";
            var tempPath = Path.Combine(directory, tempFileName);
            var finalPath = Path.Combine(directory, $"{fileNameWithoutExt}.{targetFormat}");

            try
            {
                originalSize = new FileInfo(filePath).Length;

                progress?.Report(new NormalizationProgress
                {
                    CurrentFile = Path.GetFileName(filePath),
                    Status = $"Converting to .{targetFormat}..."
                });

                // Build FFmpeg arguments
                var args = $"-i \"{filePath}\" -b:a {bitrate}k -y \"{tempPath}\"";

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

                    string standardOutput = null;
                    string standardError = null;

                    var readTask = Task.Run(async () =>
                    {
                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();

                        if (!process.WaitForExit(120000)) // 2 minute timeout
                        {
                            try { process.Kill(); } catch { }
                            throw new TimeoutException($"FFmpeg conversion timed out after 2 minutes: {filePath}");
                        }

                        standardOutput = await outputTask.ConfigureAwait(false);
                        standardError = await errorTask.ConfigureAwait(false);
                    }, cancellationToken);

                    readTask.GetAwaiter().GetResult();

                    if (cancellationToken.IsCancellationRequested)
                    {
                        try { File.Delete(tempPath); } catch { }
                        throw new OperationCanceledException();
                    }

                    if (process.ExitCode != 0)
                    {
                        Logger.Error($"FFmpeg conversion failed (exit code {process.ExitCode}) for {filePath}:\nError: {standardError}");
                        _fileLogger?.Error($"FFmpeg conversion failed for {Path.GetFileName(filePath)}: {standardError}");
                        try { File.Delete(tempPath); } catch { }
                        return false;
                    }

                    if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                    {
                        Logger.Error($"Converted file is missing or empty: {tempPath}");
                        try { File.Delete(tempPath); } catch { }
                        return false;
                    }

                    // Handle original file
                    if (keepOriginals)
                    {
                        var backupPath = Path.Combine(directory, $"{fileNameWithoutExt}-preconvert{Path.GetExtension(filePath)}");
                        if (File.Exists(backupPath))
                            File.Delete(backupPath);
                        File.Move(filePath, backupPath);
                    }
                    else
                    {
                        File.Delete(filePath);
                    }

                    // Handle name collision with existing file of the same target name
                    if (File.Exists(finalPath))
                        File.Delete(finalPath);

                    File.Move(tempPath, finalPath);
                    newSize = new FileInfo(finalPath).Length;

                    progress?.Report(new NormalizationProgress
                    {
                        CurrentFile = Path.GetFileName(filePath),
                        Status = "Completed"
                    });

                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error converting file: {filePath}");
                try { File.Delete(tempPath); } catch { }
                return false;
            }
        }
    }

    // Result of a bulk conversion operation
    public class ConversionResult
    {
        public int TotalFiles { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<string> FailedFiles { get; set; } = new List<string>();
        public long TotalOriginalBytes { get; set; }
        public long TotalNewBytes { get; set; }

        public string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {suffixes[order]}";
        }
    }
}
