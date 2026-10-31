using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    // Service interface for audio silence trimming operations
    public interface ITrimService
    {
        // Trims leading silence.
        Task<bool> TrimFileAsync(
            string filePath,
            TrimSettings settings,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken);

        Task<NormalizationResult> TrimBulkAsync(
            IEnumerable<string> filePaths,
            TrimSettings settings,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken);

        bool ValidateFFmpegAvailable(string ffmpegPath);
    }
}

