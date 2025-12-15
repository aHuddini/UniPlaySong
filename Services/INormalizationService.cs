using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Service interface for audio normalization operations
    /// </summary>
    public interface INormalizationService
    {
        /// <summary>
        /// Normalize a single audio file using two-pass loudnorm
        /// </summary>
        /// <param name="filePath">Path to the audio file to normalize</param>
        /// <param name="settings">Normalization settings</param>
        /// <param name="progress">Progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if normalization succeeded</returns>
        Task<bool> NormalizeFileAsync(
            string filePath, 
            NormalizationSettings settings, 
            IProgress<NormalizationProgress> progress, 
            CancellationToken cancellationToken);

        /// <summary>
        /// Normalize multiple audio files in bulk
        /// </summary>
        /// <param name="filePaths">Paths to audio files to normalize</param>
        /// <param name="settings">Normalization settings</param>
        /// <param name="progress">Progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Normalization result with statistics</returns>
        Task<NormalizationResult> NormalizeBulkAsync(
            IEnumerable<string> filePaths, 
            NormalizationSettings settings, 
            IProgress<NormalizationProgress> progress, 
            CancellationToken cancellationToken);

        /// <summary>
        /// Validate that FFmpeg is available and accessible
        /// </summary>
        /// <param name="ffmpegPath">Path to FFmpeg executable</param>
        /// <returns>True if FFmpeg is available</returns>
        bool ValidateFFmpegAvailable(string ffmpegPath);

        /// <summary>
        /// Delete normalized files (files with normalization suffix). Original files are preserved.
        /// </summary>
        /// <param name="filePaths">Paths to music files or directories to search</param>
        /// <param name="normalizationSuffix">Suffix used for normalized files (e.g., "-normalized")</param>
        /// <param name="progress">Progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Deletion result with statistics</returns>
        Task<NormalizationResult> RestoreFromBackupsAsync(
            IEnumerable<string> filePaths,
            string normalizationSuffix,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken);
    }
}