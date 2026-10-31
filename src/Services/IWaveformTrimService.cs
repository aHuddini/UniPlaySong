using System;
using System.Threading;
using System.Threading.Tasks;
using UniPlaySong.Models.WaveformTrim;

namespace UniPlaySong.Services
{
    // Service for waveform generation and precise audio trimming
    public interface IWaveformTrimService
    {
        // ~1000 samples, enough to draw the waveform.
        Task<WaveformData> GenerateWaveformAsync(string audioFilePath, CancellationToken token = default);

        // Trims with FFmpeg and appends suffix (e.g. "-ptrimmed"); the original moves to PreservedOriginals.
        Task<bool> ApplyTrimAsync(string inputPath, TrimWindow trimWindow, string suffix, CancellationToken token = default);

        // Same as above with an explicit FFmpeg path.
        Task<bool> ApplyTrimAsync(string inputPath, TrimWindow trimWindow, string suffix, string ffmpegPath, CancellationToken token = default);

        Task<TimeSpan> GetAudioDurationAsync(string audioFilePath);

        bool ValidateFFmpegAvailable(string ffmpegPath);

        // True if the filename already carries the trim suffix.
        bool IsAlreadyTrimmed(string filePath, string suffix);
    }
}
