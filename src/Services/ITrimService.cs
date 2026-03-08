using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Service interface for audio silence trimming operations
    /// </summary>
    public interface ITrimService
    {
        /// <summary>
        /// Trim leading silence from a single audio file
        /// </summary>
        /// <param name="filePath">Path to the audio file to trim</param>
        /// <param name="settings">Trim settings</param>
        /// <param name="progress">Progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if trim succeeded</returns>
        Task<bool> TrimFileAsync(
            string filePath,
            TrimSettings settings,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken);

        /// <summary>
        /// Trim leading silence from multiple audio files in bulk
        /// </summary>
        /// <param name="filePaths">Paths to audio files to trim</param>
        /// <param name="settings">Trim settings</param>
        /// <param name="progress">Progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Trim result with statistics</returns>
        Task<NormalizationResult> TrimBulkAsync(
            IEnumerable<string> filePaths,
            TrimSettings settings,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken);

        /// <summary>
        /// Validate that FFmpeg is available and accessible
        /// </summary>
        /// <param name="ffmpegPath">Path to FFmpeg executable</param>
        /// <returns>True if FFmpeg is available</returns>
        bool ValidateFFmpegAvailable(string ffmpegPath);
    }
}

