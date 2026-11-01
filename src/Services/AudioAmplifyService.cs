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

namespace UniPlaySong.Services
{
    // Audio amplification service using NAudio for analysis and FFmpeg for processing
    public class AudioAmplifyService
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();
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

        // Generate waveform data for display, including peak amplitude info
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

                    var (stream, sampleReader) = WaveformReader.Open(audioFilePath);
                    using (stream)
                    {
                        var totalSamples = stream.Length / (stream.WaveFormat.BitsPerSample / 8);
                        var channels = stream.WaveFormat.Channels;
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

                        Logger.DebugIf(LogPrefix,$"Waveform generated: {outputSamples.Length} samples, duration={stream.TotalTime:mm\\:ss\\.fff}, peak={peakDb:F1}dB");

                        return new AmplifyWaveformData
                        {
                            Samples = outputSamples,
                            Duration = stream.TotalTime,
                            SampleRate = stream.WaveFormat.SampleRate,
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

        public bool WouldClip(float peakDb, float gainDb)
        {
            // If peak + gain > 0dBFS, we'd clip
            return (peakDb + gainDb) > 0f;
        }

        // Apply volume adjustment using FFmpeg
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
                    return true; // Gain too small, no change needed
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

                // Stop playback if this file is playing
                _playbackService?.Stop();

                // Preserve original file
                string preservedOriginalPath = await FFmpegHelper.PreserveOriginalAsync(_backupBasePath, inputPath, directory, fileName, extension, LogPrefix);

                // Build FFmpeg command for volume adjustment
                // Format gain with decimal point regardless of locale
                var gainStr = gainDb.ToString("F1", CultureInfo.InvariantCulture);

                // Determine codec based on extension
                var codecArgs = FFmpegHelper.GetCodecArgs(extension);

                // FFmpeg command: apply volume filter
                var args = $"-y -i \"{inputPath}\" -af \"volume={gainStr}dB\" {codecArgs} \"{tempPath}\"";

                Logger.DebugIf(LogPrefix,$"FFmpeg args: {args}");

                var success = await FFmpegHelper.RunAsync(ffmpegPath, args, token, LogPrefix);

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

        public bool ValidateFFmpegAvailable(string ffmpegPath)
        {
            return FFmpegHelper.IsAvailable(ffmpegPath);
        }
    }

    // Waveform data with amplitude analysis for amplification
    public class AmplifyWaveformData
    {
        public float[] Samples { get; set; } // Normalized 0-1 for display
        public TimeSpan Duration { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; } // 1=mono, 2=stereo
        public string FilePath { get; set; }
        public float PeakAmplitude { get; set; } // Raw peak (0.0-1.0, 1.0 = 0dBFS)
        public float PeakDb { get; set; } // Peak in dB (0dB = full scale)
        public bool IsValid => Samples != null && Samples.Length > 0 && Duration.TotalSeconds > 0;
        public float HeadroomDb => -PeakDb; // How much gain before clipping
        public bool WouldClip(float gainDb) => (PeakDb + gainDb) > 0f;

        // Scale samples by gain factor (capped at 1.0) and flag clipping
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
