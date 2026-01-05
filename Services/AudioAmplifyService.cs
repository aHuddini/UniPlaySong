using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Service for audio amplification (volume adjustment) using NAudio for analysis and FFmpeg for processing
    /// </summary>
    public class AudioAmplifyService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string LogPrefix = "Amplify";
        private readonly ErrorHandlerService _errorHandler;
        private readonly IMusicPlaybackService _playbackService;
        private readonly string _backupBasePath;

        // Target number of samples for waveform display
        private const int TargetWaveformSamples = 1000;

        public AudioAmplifyService(
            ErrorHandlerService errorHandler = null,
            IMusicPlaybackService playbackService = null,
            string backupBasePath = null)
        {
            _errorHandler = errorHandler;
            _playbackService = playbackService;
            _backupBasePath = backupBasePath;
        }

        /// <summary>
        /// Generate waveform data for display, including peak amplitude info
        /// </summary>
        public async Task<AmplifyWaveformData> GenerateWaveformAsync(string audioFilePath, CancellationToken token = default)
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

                        // Track the raw (unnormalized) peak for clipping detection
                        float rawGlobalMax = 0;

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
                            if (maxAbs > rawGlobalMax) rawGlobalMax = maxAbs;
                            outputIndex++;
                        }

                        // Calculate peak in dB (relative to 1.0 = 0dBFS)
                        // A value of 1.0 means 0dB (full scale)
                        // A value of 0.5 means -6dB
                        float peakDb = rawGlobalMax > 0 ? 20f * (float)Math.Log10(rawGlobalMax) : -96f;

                        // Normalize samples to 0-1 range for display
                        if (rawGlobalMax > 0)
                        {
                            for (int i = 0; i < outputSamples.Length; i++)
                            {
                                outputSamples[i] /= rawGlobalMax;
                            }
                        }

                        Logger.DebugIf(LogPrefix,$"Waveform generated: {outputSamples.Length} samples, duration={reader.TotalTime:mm\\:ss\\.fff}, peak={peakDb:F1}dB");

                        return new AmplifyWaveformData
                        {
                            Samples = outputSamples,
                            Duration = reader.TotalTime,
                            SampleRate = reader.WaveFormat.SampleRate,
                            Channels = channels,
                            FilePath = audioFilePath,
                            PeakAmplitude = rawGlobalMax,
                            PeakDb = peakDb
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
        /// Calculate headroom - how much gain can be applied before clipping
        /// </summary>
        public float CalculateHeadroomDb(float peakDb)
        {
            // Headroom is the distance from peak to 0dBFS
            // If peak is -6dB, we have 6dB of headroom
            return -peakDb;
        }

        /// <summary>
        /// Check if applying the given gain would cause clipping
        /// </summary>
        public bool WouldClip(float peakDb, float gainDb)
        {
            // If peak + gain > 0dBFS, we'd clip
            return (peakDb + gainDb) > 0f;
        }

        /// <summary>
        /// Apply volume adjustment using FFmpeg
        /// </summary>
        public async Task<bool> ApplyAmplifyAsync(
            string inputPath,
            float gainDb,
            string suffix,
            string ffmpegPath,
            CancellationToken token = default)
        {
            Logger.DebugIf(LogPrefix,$"ApplyAmplifyAsync started - input: {Path.GetFileName(inputPath)}, gain: {gainDb:+0.0;-0.0;0}dB");
            try
            {
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    Logger.Error("FFmpeg path is null or empty");
                    return false;
                }

                if (Math.Abs(gainDb) < 0.1f)
                {
                    Logger.Info("Gain too small, skipping amplification");
                    return true; // No change needed
                }

                var directory = Path.GetDirectoryName(inputPath);
                var fileName = Path.GetFileNameWithoutExtension(inputPath);
                var extension = Path.GetExtension(inputPath);

                // Build output filename with suffix
                var outputFileName = $"{fileName}{suffix}";
                var finalOutputPath = Path.Combine(directory, $"{outputFileName}{extension}");
                var tempPath = Path.Combine(directory, $"{fileName}.amplify.tmp{extension}");

                Logger.DebugIf(LogPrefix,$"Output path: {finalOutputPath}");
                Logger.DebugIf(LogPrefix,$"Temp path: {tempPath}");
                Logger.Info($"Amplify: {fileName} by {gainDb:+0.0;-0.0;0}dB");

                // Stop playback if this file is playing
                _playbackService?.Stop();

                // Preserve original file
                string preservedOriginalPath = await PreserveOriginalAsync(inputPath, directory, fileName, extension);

                // Build FFmpeg command for volume adjustment
                // Format gain with decimal point regardless of locale
                var gainStr = gainDb.ToString("F1", CultureInfo.InvariantCulture);

                // Determine codec based on extension
                var codecArgs = GetCodecArgs(extension);

                // FFmpeg command: apply volume filter
                var args = $"-y -i \"{inputPath}\" -af \"volume={gainStr}dB\" {codecArgs} \"{tempPath}\"";

                Logger.DebugIf(LogPrefix,$"FFmpeg args: {args}");

                var success = await RunFFmpegAsync(ffmpegPath, args, token);

                if (success && File.Exists(tempPath))
                {
                    Logger.DebugIf(LogPrefix,"FFmpeg completed successfully, temp file exists");

                    // Delete the original file from game folder (it's preserved in PreservedOriginals)
                    if (File.Exists(inputPath))
                    {
                        Logger.DebugIf(LogPrefix,$"Deleting original from game folder: {inputPath}");
                        File.Delete(inputPath);
                    }

                    // If output file already exists (e.g., re-amplifying), remove it first
                    if (File.Exists(finalOutputPath))
                    {
                        Logger.DebugIf(LogPrefix,$"Removing existing output file: {finalOutputPath}");
                        File.Delete(finalOutputPath);
                    }

                    // Move temp to final output
                    Logger.DebugIf(LogPrefix,$"Moving temp to final: {finalOutputPath}");
                    File.Move(tempPath, finalOutputPath);

                    Logger.Info($"Amplify completed: {Path.GetFileName(finalOutputPath)} (original moved to PreservedOriginals)");
                    return true;
                }
                else
                {
                    Logger.DebugIf(LogPrefix,$"FFmpeg failed or temp file missing. success={success}, tempExists={File.Exists(tempPath)}");

                    // Cleanup temp file on failure
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }

                    // Restore original if preserved
                    if (!string.IsNullOrEmpty(preservedOriginalPath) && File.Exists(preservedOriginalPath))
                    {
                        try
                        {
                            File.Copy(preservedOriginalPath, inputPath, true);
                            Logger.Info("Restored original file after failed amplify");
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
                Logger.DebugIf(LogPrefix,"Amplify operation cancelled by user");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error applying amplify to: {inputPath}");
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
                    }
                    else
                    {
                        var parentDir = Directory.GetParent(directory)?.FullName ?? directory;
                        preservedOriginalsDir = Path.Combine(parentDir, Constants.PreservedOriginalsFolderName);
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

                    return preservedPath;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error preserving original file");
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

                    using (var process = Process.Start(processInfo))
                    {
                        if (process == null)
                        {
                            Logger.Error("Failed to start FFmpeg process");
                            return false;
                        }

                        // Read output asynchronously to prevent deadlock
                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();

                        // Wait for process with timeout (2 minutes)
                        var timeout = 120000;
                        if (!process.WaitForExit(timeout))
                        {
                            try { process.Kill(); } catch { }
                            Logger.Error("FFmpeg process timed out");
                            return false;
                        }

                        token.ThrowIfCancellationRequested();

                        var stderr = errorTask.Result;

                        if (process.ExitCode != 0)
                        {
                            Logger.Error($"FFmpeg failed with exit code {process.ExitCode}: {stderr}");
                            return false;
                        }

                        Logger.DebugIf(LogPrefix,"FFmpeg completed successfully");
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error running FFmpeg");
                    return false;
                }
            }, token);
        }

        /// <summary>
        /// Validate FFmpeg is available
        /// </summary>
        public bool ValidateFFmpegAvailable(string ffmpegPath)
        {
            return FFmpegHelper.IsAvailable(ffmpegPath);
        }
    }

    /// <summary>
    /// Waveform data with amplitude analysis for amplification
    /// </summary>
    public class AmplifyWaveformData
    {
        /// <summary>
        /// Normalized samples (0 to 1) for display
        /// </summary>
        public float[] Samples { get; set; }

        /// <summary>
        /// Total duration of the audio file
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Sample rate of source audio
        /// </summary>
        public int SampleRate { get; set; }

        /// <summary>
        /// Number of channels (1=mono, 2=stereo)
        /// </summary>
        public int Channels { get; set; }

        /// <summary>
        /// Source file path
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Raw peak amplitude (0.0 to 1.0 where 1.0 = 0dBFS)
        /// </summary>
        public float PeakAmplitude { get; set; }

        /// <summary>
        /// Peak level in dB (0dB = full scale, negative values are below full scale)
        /// </summary>
        public float PeakDb { get; set; }

        /// <summary>
        /// Whether the waveform data is valid and ready for display
        /// </summary>
        public bool IsValid => Samples != null && Samples.Length > 0 && Duration.TotalSeconds > 0;

        /// <summary>
        /// Get headroom in dB (how much gain can be applied before clipping)
        /// </summary>
        public float HeadroomDb => -PeakDb;

        /// <summary>
        /// Check if a specific gain would cause clipping
        /// </summary>
        public bool WouldClip(float gainDb) => (PeakDb + gainDb) > 0f;

        /// <summary>
        /// Get scaled samples for display with a given gain adjustment
        /// Returns samples scaled by the gain factor, capped at 1.0
        /// Also returns which samples would clip
        /// </summary>
        public (float[] scaledSamples, bool[] clipping) GetScaledSamples(float gainDb)
        {
            if (Samples == null || Samples.Length == 0)
                return (new float[0], new bool[0]);

            var scaleFactor = (float)Math.Pow(10, gainDb / 20.0);
            var scaled = new float[Samples.Length];
            var clipping = new bool[Samples.Length];

            for (int i = 0; i < Samples.Length; i++)
            {
                var scaledValue = Samples[i] * scaleFactor;
                clipping[i] = scaledValue > 1.0f;
                scaled[i] = Math.Min(scaledValue, 1.0f);
            }

            return (scaled, clipping);
        }
    }
}
