using UniPlaySong.Models;

namespace UniPlaySong.Services.ActiveMedia
{
    // Immutable value snapshot of the current active-media state. Produced by
    // ActiveMediaService.GetSnapshot(); consumed by ActiveMediaViewModel.
    public class ActiveMediaSnapshot
    {
        public bool HasActiveMedia { get; }
        public ActiveMediaSourceKind SourceKind { get; }
        public string SourceName { get; }
        public bool IsPlaying { get; }
        public bool IsMuted { get; }
        public double Progress { get; }      // 0–100
        public string PositionText { get; }  // "m:ss"
        public string DurationText { get; }  // "m:ss"
        public double Volume { get; }        // 0–100
        public bool CanNext { get; }
        public bool CanPrevious { get; }

        public ActiveMediaSnapshot(
            bool hasActiveMedia,
            ActiveMediaSourceKind sourceKind,
            string sourceName,
            bool isPlaying,
            bool isMuted,
            double progress,
            string positionText,
            string durationText,
            double volume,
            bool canNext,
            bool canPrevious)
        {
            HasActiveMedia = hasActiveMedia;
            SourceKind = sourceKind;
            SourceName = sourceName ?? string.Empty;
            IsPlaying = isPlaying;
            IsMuted = isMuted;
            Progress = progress;
            PositionText = positionText ?? string.Empty;
            DurationText = durationText ?? string.Empty;
            Volume = volume;
            CanNext = canNext;
            CanPrevious = canPrevious;
        }

        public static readonly ActiveMediaSnapshot Empty = new ActiveMediaSnapshot(
            false, ActiveMediaSourceKind.None, string.Empty, false, false,
            0.0, string.Empty, string.Empty, 0.0, false, false);
    }
}
