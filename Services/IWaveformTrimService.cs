using System;
using System.Threading;
using System.Threading.Tasks;
using UniPlaySong.Models.WaveformTrim;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Service for waveform generation and precise audio trimming
    /// </summary>
    public interface IWaveformTrimService
    {
        /// <summary>
        /// Generate waveform data for display (~1000 samples)
        /// </summary>
        /// <param name="audioFilePath">Path to the audio file</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Waveform data for rendering</returns>
        Task<WaveformData> GenerateWaveformAsync(string audioFilePath, CancellationToken token = default);

        /// <summary>
        /// Apply trim using FFmpeg, save to output path.
        /// Original file is moved to PreservedOriginals folder.
        /// </summary>
        /// <param name="inputPath">Path to the audio file to trim</param>
        /// <param name="trimWindow">The trim selection window</param>
        /// <param name="suffix">Suffix to add to trimmed file (e.g., "-ptrimmed")</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>True if trim was successful</returns>
        Task<bool> ApplyTrimAsync(string inputPath, TrimWindow trimWindow, string suffix, CancellationToken token = default);

        /// <summary>
        /// Apply trim using FFmpeg with explicit FFmpeg path.
        /// Original file is moved to PreservedOriginals folder.
        /// </summary>
        /// <param name="inputPath">Path to the audio file to trim</param>
        /// <param name="trimWindow">The trim selection window</param>
        /// <param name="suffix">Suffix to add to trimmed file (e.g., "-ptrimmed")</param>
        /// <param name="ffmpegPath">Path to FFmpeg executable</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>True if trim was successful</returns>
        Task<bool> ApplyTrimAsync(string inputPath, TrimWindow trimWindow, string suffix, string ffmpegPath, CancellationToken token = default);

        /// <summary>
        /// Get audio duration for calculating trim positions
        /// </summary>
        /// <param name="audioFilePath">Path to the audio file</param>
        /// <returns>Duration of the audio file</returns>
        Task<TimeSpan> GetAudioDurationAsync(string audioFilePath);

        /// <summary>
        /// Validate that FFmpeg is available at the specified path
        /// </summary>
        /// <param name="ffmpegPath">Path to FFmpeg executable</param>
        /// <returns>True if FFmpeg is available and working</returns>
        bool ValidateFFmpegAvailable(string ffmpegPath);

        /// <summary>
        /// Check if a file has already been precise-trimmed (has the suffix)
        /// </summary>
        /// <param name="filePath">Path to check</param>
        /// <param name="suffix">The trim suffix to check for</param>
        /// <returns>True if the file has already been trimmed</returns>
        bool IsAlreadyTrimmed(string filePath, string suffix);
    }
}
