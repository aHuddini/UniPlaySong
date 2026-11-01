using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Playnite.SDK;
using UniPlaySong.Audio;
using UniPlaySong.Common;
using UniPlaySong.Models.WaveformTrim;

namespace UniPlaySong.Services
{
    // Waveform generation and precise audio trimming using NAudio and FFmpeg
    public class WaveformTrimService : IWaveformTrimService
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();
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

        // Generate waveform data for display
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

                    var (stream, sampleReader) = WaveformReader.Open(audioFilePath);
                    using (stream)
                    {
                        var totalSamples = stream.Length / (stream.WaveFormat.BitsPerSample / 8);
                        var channels = stream.WaveFormat.Channels;
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

                            int samplesRead = sampleReader.Read(buffer, 0, buffer.Length);
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

                        Logger.DebugIf(LogPrefix,$"Waveform generated: {outputSamples.Length} samples, duration={stream.TotalTime:mm\\:ss\\.fff}, rate={stream.WaveFormat.SampleRate}Hz, channels={channels}");

                        return new WaveformData
                        {
                            Samples = outputSamples,
                            Duration = stream.TotalTime,
                            SampleRate = stream.WaveFormat.SampleRate,
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

                    var (durationStream, _) = WaveformReader.Open(audioFilePath);
                    using (durationStream)
                    {
                        return durationStream.TotalTime;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error getting duration for: {audioFilePath}");
                    return TimeSpan.Zero;
                }
            });
        }

        // Apply trim via FFmpeg (reflection fallback - prefer the overload with explicit ffmpegPath)
        public async Task<bool> ApplyTrimAsync(string inputPath, TrimWindow trimWindow, string suffix, CancellationToken token = default)
        {
            // Try to get FFmpeg path from settings via reflection (fallback method)
            var ffmpegPath = GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                Logger.Error("FFmpeg path not configured - use the overload that accepts ffmpegPath directly");
                return false;
            }
            return await ApplyTrimAsync(inputPath, trimWindow, suffix, ffmpegPath, token);
        }

        // Apply trim using FFmpeg with explicit FFmpeg path
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

                // Stop playback if this file is playing
                _playbackService?.Stop();

                // Preserve original file
                string preservedOriginalPath = await FFmpegHelper.PreserveOriginalAsync(_backupBasePath, inputPath, directory, fileName, extension, LogPrefix);

                // Build FFmpeg command for precise trim
                // Using -ss before -i for fast seeking, -to for end time
                // Use format with period for decimal separator regardless of locale
                var startSeconds = trimWindow.StartTime.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
                var durationSeconds = trimWindow.Duration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);

                // Determine codec based on extension
                var codecArgs = FFmpegHelper.GetCodecArgs(extension);

                // FFmpeg command: seek to start, limit duration, copy or re-encode
                var args = $"-y -ss {startSeconds} -i \"{inputPath}\" -t {durationSeconds} {codecArgs} \"{tempPath}\"";

                Logger.DebugIf(LogPrefix,$"FFmpeg path: {ffmpegPath}");
                Logger.DebugIf(LogPrefix,$"FFmpeg args: {args}");

                var success = await FFmpegHelper.RunAsync(ffmpegPath, args, token, LogPrefix);

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
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error applying trim to: {inputPath}");
                Logger.DebugIf(LogPrefix,$"ApplyTrimAsync exception: {ex.Message}");
                return false;
            }
        }

        // Get FFmpeg path from settings via Application.Current.Properties (reflection)
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

        public bool ValidateFFmpegAvailable(string ffmpegPath)
        {
            Logger.DebugIf(LogPrefix,$"ValidateFFmpegAvailable - path: {ffmpegPath}");
            var result = FFmpegHelper.IsAvailable(ffmpegPath);
            Logger.DebugIf(LogPrefix,$"FFmpeg validation result: {result}");
            return result;
        }

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
