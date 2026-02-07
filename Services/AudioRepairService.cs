using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Result of audio file analysis
    /// </summary>
    public class AudioProbeResult
    {
        public bool Success { get; set; }
        public bool HasIssues { get; set; }
        public string FilePath { get; set; }
        public string Format { get; set; }
        public string Codec { get; set; }
        public int? Bitrate { get; set; }
        public int? SampleRate { get; set; }
        public int? Channels { get; set; }
        public double? Duration { get; set; }
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Specific issues detected with the file
        /// </summary>
        public AudioIssues Issues { get; set; } = new AudioIssues();
    }

    /// <summary>
    /// Specific issues that can be detected in audio files
    /// </summary>
    public class AudioIssues
    {
        public bool MissingBitrate { get; set; }
        public bool MissingSampleRate { get; set; }
        public bool MissingDuration { get; set; }
        public bool CorruptHeaders { get; set; }
        public bool UnusualEncoding { get; set; }
        public bool VeryHighBitrate { get; set; }
        public bool VeryLowBitrate { get; set; }

        public bool HasAnyIssue => MissingBitrate || MissingSampleRate || MissingDuration ||
                                   CorruptHeaders || UnusualEncoding || VeryHighBitrate || VeryLowBitrate;

        public override string ToString()
        {
            var issues = new System.Collections.Generic.List<string>();
            if (MissingBitrate) issues.Add("missing bitrate");
            if (MissingSampleRate) issues.Add("missing sample rate");
            if (MissingDuration) issues.Add("missing duration");
            if (CorruptHeaders) issues.Add("corrupt headers");
            if (UnusualEncoding) issues.Add("unusual encoding");
            if (VeryHighBitrate) issues.Add("very high bitrate");
            if (VeryLowBitrate) issues.Add("very low bitrate");
            return issues.Count > 0 ? string.Join(", ", issues) : "none";
        }
    }

    /// <summary>
    /// Service for detecting and repairing problematic audio files that may cause
    /// SDL_mixer "Out of memory" or similar playback errors.
    /// Uses FFmpeg to probe files for issues and re-encode them if necessary.
    /// </summary>
    public class AudioRepairService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly ErrorHandlerService _errorHandler;
        private readonly IMusicPlaybackService _playbackService;
        private readonly string _backupBasePath;

        // Regex patterns for parsing FFmpeg probe output
        private static readonly Regex DurationRegex = new Regex(@"Duration:\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2})", RegexOptions.Compiled);
        private static readonly Regex BitrateRegex = new Regex(@"bitrate:\s*(\d+)\s*kb/s", RegexOptions.Compiled);
        private static readonly Regex AudioStreamRegex = new Regex(@"Stream\s+#\d+:\d+.*Audio:\s*(\w+).*?(\d+)\s*Hz.*?(\d+)\s*kb/s", RegexOptions.Compiled);
        private static readonly Regex AudioStreamSimpleRegex = new Regex(@"Stream\s+#\d+:\d+.*Audio:\s*(\w+)", RegexOptions.Compiled);
        private static readonly Regex SampleRateRegex = new Regex(@"(\d+)\s*Hz", RegexOptions.Compiled);
        private static readonly Regex ChannelsRegex = new Regex(@"(mono|stereo|\d+\s*channels)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public AudioRepairService(ErrorHandlerService errorHandler = null, IMusicPlaybackService playbackService = null, string backupBasePath = null)
        {
            _errorHandler = errorHandler;
            _playbackService = playbackService;
            _backupBasePath = backupBasePath;
        }

        /// <summary>
        /// Validates that FFmpeg is available at the specified path
        /// </summary>
        public bool ValidateFFmpegAvailable(string ffmpegPath)
        {
            return FFmpegHelper.IsAvailable(ffmpegPath);
        }

        /// <summary>
        /// Probes an audio file using FFmpeg to detect potential issues
        /// that could cause SDL_mixer to fail loading the file.
        /// </summary>
        /// <param name="filePath">Path to the audio file</param>
        /// <param name="ffmpegPath">Path to FFmpeg executable</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Probe result with detected issues</returns>
        public async Task<AudioProbeResult> ProbeFileAsync(string filePath, string ffmpegPath, CancellationToken cancellationToken = default)
        {
            var result = new AudioProbeResult { FilePath = filePath };

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                result.Success = false;
                result.ErrorMessage = "File does not exist";
                return result;
            }

            if (!ValidateFFmpegAvailable(ffmpegPath))
            {
                result.Success = false;
                result.ErrorMessage = "FFmpeg not available";
                return result;
            }

            try
            {
                // Use FFmpeg to probe the file (it outputs info to stderr)
                var args = $"-i \"{filePath}\" -hide_banner -f null -";

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

                string stderr = null;

                using (var process = new Process())
                {
                    process.StartInfo = processInfo;
                    process.Start();

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
                            throw new TimeoutException("FFmpeg probe timed out");
                        }

                        await outputTask.ConfigureAwait(false);
                        stderr = await errorTask.ConfigureAwait(false);
                    }, cancellationToken);

                    // FFmpeg returns non-zero for some valid files when using -f null
                    // We care about the stderr output, not the exit code
                    result.Success = true;

                    // Parse the probe output
                    ParseProbeOutput(result, stderr);

                    // Detect issues based on parsed data
                    DetectIssues(result);

                    result.HasIssues = result.Issues.HasAnyIssue;
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Operation cancelled";
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error probing audio file: {filePath}");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Issues.CorruptHeaders = true;
                result.HasIssues = true;
            }

            return result;
        }

        /// <summary>
        /// Parses FFmpeg probe output to extract audio metadata
        /// </summary>
        private void ParseProbeOutput(AudioProbeResult result, string stderr)
        {
            if (string.IsNullOrWhiteSpace(stderr))
            {
                result.Issues.CorruptHeaders = true;
                return;
            }

            // Parse duration
            var durationMatch = DurationRegex.Match(stderr);
            if (durationMatch.Success)
            {
                var hours = int.Parse(durationMatch.Groups[1].Value);
                var minutes = int.Parse(durationMatch.Groups[2].Value);
                var seconds = int.Parse(durationMatch.Groups[3].Value);
                var centiseconds = int.Parse(durationMatch.Groups[4].Value);
                result.Duration = hours * 3600 + minutes * 60 + seconds + centiseconds / 100.0;
            }

            // Parse overall bitrate
            var bitrateMatch = BitrateRegex.Match(stderr);
            if (bitrateMatch.Success)
            {
                result.Bitrate = int.Parse(bitrateMatch.Groups[1].Value);
            }

            // Parse audio stream info (more detailed)
            var audioStreamMatch = AudioStreamRegex.Match(stderr);
            if (audioStreamMatch.Success)
            {
                result.Codec = audioStreamMatch.Groups[1].Value;
                result.SampleRate = int.Parse(audioStreamMatch.Groups[2].Value);
                // Audio-specific bitrate might differ from overall
            }
            else
            {
                // Try simpler pattern
                var simpleMatch = AudioStreamSimpleRegex.Match(stderr);
                if (simpleMatch.Success)
                {
                    result.Codec = simpleMatch.Groups[1].Value;
                }

                // Try to find sample rate separately
                var sampleRateMatch = SampleRateRegex.Match(stderr);
                if (sampleRateMatch.Success)
                {
                    result.SampleRate = int.Parse(sampleRateMatch.Groups[1].Value);
                }
            }

            // Parse channels
            var channelsMatch = ChannelsRegex.Match(stderr);
            if (channelsMatch.Success)
            {
                var channelStr = channelsMatch.Groups[1].Value.ToLowerInvariant();
                if (channelStr == "mono") result.Channels = 1;
                else if (channelStr == "stereo") result.Channels = 2;
                else if (int.TryParse(channelStr.Replace("channels", "").Trim(), out int ch))
                    result.Channels = ch;
            }

            // Determine format from file extension
            result.Format = Path.GetExtension(result.FilePath)?.TrimStart('.').ToUpperInvariant() ?? "UNKNOWN";
        }

        /// <summary>
        /// Detects potential issues with the audio file that could cause playback problems
        /// </summary>
        private void DetectIssues(AudioProbeResult result)
        {
            // Missing critical metadata
            if (!result.Bitrate.HasValue || result.Bitrate == 0)
            {
                result.Issues.MissingBitrate = true;
            }

            if (!result.SampleRate.HasValue || result.SampleRate == 0)
            {
                result.Issues.MissingSampleRate = true;
            }

            if (!result.Duration.HasValue || result.Duration <= 0)
            {
                result.Issues.MissingDuration = true;
            }

            // Unusual bitrates that might cause issues
            if (result.Bitrate.HasValue)
            {
                if (result.Bitrate > 500) // Very high bitrate (>500 kbps)
                {
                    result.Issues.VeryHighBitrate = true;
                }
                else if (result.Bitrate < 32) // Very low bitrate (<32 kbps)
                {
                    result.Issues.VeryLowBitrate = true;
                }
            }

            // Check for unusual sample rates
            if (result.SampleRate.HasValue)
            {
                var commonRates = new[] { 8000, 11025, 16000, 22050, 32000, 44100, 48000, 96000 };
                bool isCommonRate = false;
                foreach (var rate in commonRates)
                {
                    if (result.SampleRate == rate)
                    {
                        isCommonRate = true;
                        break;
                    }
                }
                if (!isCommonRate)
                {
                    result.Issues.UnusualEncoding = true;
                }
            }
        }

        /// <summary>
        /// Repairs a problematic audio file by re-encoding it to a standard format.
        /// Creates a backup of the original file before repair.
        /// </summary>
        /// <param name="filePath">Path to the audio file to repair</param>
        /// <param name="ffmpegPath">Path to FFmpeg executable</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if repair was successful, false otherwise</returns>
        public async Task<bool> RepairFileAsync(string filePath, string ffmpegPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Logger.Error($"Cannot repair - file does not exist: {filePath}");
                return false;
            }

            if (!ValidateFFmpegAvailable(ffmpegPath))
            {
                Logger.Error($"Cannot repair - FFmpeg not available at: {ffmpegPath}");
                return false;
            }

            var fileName = Path.GetFileName(filePath);

            // Stop playback to prevent file locking
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
                Logger.Warn(ex, "Error stopping playback before repair");
            }

            try
            {
                var directory = Path.GetDirectoryName(filePath);
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath);

                // Create temp file for re-encoded output
                var tempPath = Path.Combine(directory, $"{fileNameWithoutExt}.repair.tmp{extension}");

                // Determine codec arguments based on file extension
                var codecArgs = GetCodecArgs(extension);

                // Re-encode the file with standard settings
                // -y: overwrite output, -ar 48000: 48kHz sample rate (matches normalization), -ac 2: stereo
                var args = $"-y -i \"{filePath}\" -ar 48000 -ac 2 {codecArgs} \"{tempPath}\"";

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

                string stderr = null;

                using (var process = new Process())
                {
                    process.StartInfo = processInfo;
                    process.Start();

                    await Task.Run(async () =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { process.Kill(); } catch { }
                            throw new OperationCanceledException();
                        }

                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();

                        if (!process.WaitForExit(300000)) // 5 minute timeout
                        {
                            try { process.Kill(); } catch { }
                            throw new TimeoutException("FFmpeg repair timed out after 5 minutes");
                        }

                        await outputTask.ConfigureAwait(false);
                        stderr = await errorTask.ConfigureAwait(false);
                    }, cancellationToken);

                    if (process.ExitCode != 0)
                    {
                        Logger.Error($"FFmpeg repair failed (exit code {process.ExitCode}): {stderr}");
                        try { File.Delete(tempPath); } catch { }
                        return false;
                    }

                    if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                    {
                        Logger.Error($"Repaired file is missing or empty: {tempPath}");
                        try { File.Delete(tempPath); } catch { }
                        return false;
                    }

                    // Backup original to PreservedOriginals folder
                    string preservedPath = null;
                    try
                    {
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

                        var gameFolderName = Path.GetFileName(directory);
                        var gamePreservedDir = Path.Combine(preservedOriginalsDir, gameFolderName);
                        Directory.CreateDirectory(gamePreservedDir);

                        // Use a suffix to indicate this was a repair backup
                        preservedPath = Path.Combine(gamePreservedDir, $"{fileNameWithoutExt}_prerepar{extension}");

                        // Handle existing backup
                        if (File.Exists(preservedPath))
                        {
                            File.Delete(preservedPath);
                        }

                        File.Move(filePath, preservedPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "Failed to backup original file, will overwrite");
                        // If backup fails, delete original to allow replacement
                        try { File.Delete(filePath); } catch { }
                    }

                    // Move repaired file to original location
                    try
                    {
                        File.Move(tempPath, filePath);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to move repaired file to original location");

                        // Try to restore original if we backed it up
                        if (!string.IsNullOrEmpty(preservedPath) && File.Exists(preservedPath))
                        {
                            try
                            {
                                File.Move(preservedPath, filePath);
                            }
                            catch { }
                        }

                        try { File.Delete(tempPath); } catch { }
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error repairing audio file: {filePath}");
                return false;
            }
        }

        /// <summary>
        /// Gets codec arguments for FFmpeg based on file extension
        /// </summary>
        private string GetCodecArgs(string extension)
        {
            switch (extension.ToLowerInvariant())
            {
                case ".mp3":
                    // Use libmp3lame with high quality (VBR ~190 kbps)
                    return "-c:a libmp3lame -q:a 2";
                case ".flac":
                    return "-c:a flac";
                case ".wav":
                    return "-c:a pcm_s16le";
                case ".ogg":
                    return "-c:a libvorbis -q:a 6";
                case ".m4a":
                case ".aac":
                    return "-c:a aac -b:a 192k";
                default:
                    // Default to MP3 for unknown formats
                    return "-c:a libmp3lame -q:a 2";
            }
        }

        /// <summary>
        /// Checks if a file needs repair and repairs it if necessary.
        /// This is a convenience method that combines probe and repair.
        /// </summary>
        /// <param name="filePath">Path to the audio file</param>
        /// <param name="ffmpegPath">Path to FFmpeg executable</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if file is now playable (was already OK or was repaired), false if repair failed</returns>
        public async Task<bool> CheckAndRepairIfNeededAsync(string filePath, string ffmpegPath, CancellationToken cancellationToken = default)
        {
            var probeResult = await ProbeFileAsync(filePath, ffmpegPath, cancellationToken);

            if (!probeResult.Success)
            {
                Logger.Warn($"Probe failed for {Path.GetFileName(filePath)}: {probeResult.ErrorMessage}");
                // Try repair anyway since probe failed
                return await RepairFileAsync(filePath, ffmpegPath, cancellationToken);
            }

            if (!probeResult.HasIssues)
            {
                return true; // File is OK
            }

            // Issues detected, repair needed
            return await RepairFileAsync(filePath, ffmpegPath, cancellationToken);
        }
    }
}
