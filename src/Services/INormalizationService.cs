using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    // Service interface for audio normalization operations
    public interface INormalizationService
    {
        // Two-pass loudnorm.
        Task<bool> NormalizeFileAsync(
            string filePath, 
            NormalizationSettings settings, 
            IProgress<NormalizationProgress> progress, 
            CancellationToken cancellationToken);

        Task<NormalizationResult> NormalizeBulkAsync(
            IEnumerable<string> filePaths, 
            NormalizationSettings settings, 
            IProgress<NormalizationProgress> progress, 
            CancellationToken cancellationToken);

        bool ValidateFFmpegAvailable(string ffmpegPath);

        // Deletes files carrying normalizationSuffix (e.g. "-normalized") under the given files or directories; originals are kept.
        Task<NormalizationResult> RestoreFromBackupsAsync(
            IEnumerable<string> filePaths,
            string normalizationSuffix,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken);
    }
}
