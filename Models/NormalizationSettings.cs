namespace UniPlaySong.Models
{
    /// <summary>
    /// Configuration settings for audio normalization
    /// </summary>
    public class NormalizationSettings
    {
        /// <summary>
        /// Target loudness in LUFS (EBU R128 standard is -16 LUFS)
        /// </summary>
        public double TargetLoudness { get; set; } = -16.0;

        /// <summary>
        /// True peak limit in dBTP (EBU R128 standard is -1.0 to -1.5 dBTP)
        /// </summary>
        public double TruePeak { get; set; } = -1.5;

        /// <summary>
        /// Loudness range in LU (EBU R128 standard is 7-18 LU, default 11)
        /// </summary>
        public double LoudnessRange { get; set; } = 11.0;

        /// <summary>
        /// Audio codec to use (default: libmp3lame for MP3 files)
        /// </summary>
        public string AudioCodec { get; set; } = "libmp3lame";

        /// <summary>
        /// Suffix to append to normalized file names (e.g., "-normalized")
        /// Normalized files are created with this suffix when preserving originals
        /// </summary>
        public string NormalizationSuffix { get; set; } = "-normalized";

        /// <summary>
        /// Suffix to append to trimmed file names (e.g., "-trimmed")
        /// Used for suffix order management when files are both trimmed and normalized
        /// </summary>
        public string TrimSuffix { get; set; } = "-trimmed";

        /// <summary>
        /// Skip files that are already normalized
        /// </summary>
        public bool SkipAlreadyNormalized { get; set; } = true;

        /// <summary>
        /// Do not preserve original files (space saver mode)
        /// When enabled, original files are normalized directly and replaced
        /// When disabled, original files are moved to PreservedOriginals folder
        /// </summary>
        public bool DoNotPreserveOriginals { get; set; } = false;

        /// <summary>
        /// Path to FFmpeg executable
        /// </summary>
        public string FFmpegPath { get; set; }
    }

    /// <summary>
    /// Measurements from first pass of normalization analysis
    /// </summary>
    public class LoudnormMeasurements
    {
        public double MeasuredI { get; set; }      // Integrated loudness (input_i)
        public double MeasuredTP { get; set; }     // True peak (input_tp)
        public double MeasuredLRA { get; set; }    // Loudness range (input_lra)
        public double MeasuredThreshold { get; set; }  // Threshold (input_thresh)
        public double Offset { get; set; }         // Target offset (target_offset)
    }

    /// <summary>
    /// Progress information for normalization operations
    /// </summary>
    public class NormalizationProgress
    {
        public string CurrentFile { get; set; }
        public int CurrentIndex { get; set; }
        public int TotalFiles { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public string Status { get; set; }
        public bool IsComplete { get; set; }
    }

    /// <summary>
    /// Result of normalization operation
    /// </summary>
    public class NormalizationResult
    {
        public int TotalFiles { get; set; }
        public int CurrentIndex { get; set; }
        public int SuccessCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailureCount { get; set; }
        public System.Collections.Generic.List<string> FailedFiles { get; set; } = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> SkippedFiles { get; set; } = new System.Collections.Generic.List<string>();
        public bool IsComplete { get; set; }
    }

    /// <summary>
    /// Configuration settings for audio silence trimming
    /// </summary>
    public class TrimSettings
    {
        /// <summary>
        /// Silence threshold in dB for detection (default: -50.0 dB)
        /// </summary>
        public double SilenceThreshold { get; set; } = -50.0;

        /// <summary>
        /// Minimum silence duration to detect in seconds (default: 0.1 seconds)
        /// </summary>
        public double SilenceDuration { get; set; } = 0.1;

        /// <summary>
        /// Minimum silence length in seconds to trim (skip if shorter)
        /// Default: 0.5 seconds - files with less than 0.5s of leading silence are skipped
        /// </summary>
        public double MinSilenceToTrim { get; set; } = 0.5;

        /// <summary>
        /// Buffer time in seconds to add after silence end to avoid clicks
        /// Default: 0.15 seconds - ensures we're well into actual audio before cutting
        /// </summary>
        public double TrimBuffer { get; set; } = 0.15;

        /// <summary>
        /// Suffix to append to trimmed file names (e.g., "-trimmed")
        /// Trimmed files are created with this suffix when preserving originals
        /// </summary>
        public string TrimSuffix { get; set; } = "-trimmed";

        /// <summary>
        /// Skip files that are already trimmed (have trim suffix)
        /// </summary>
        public bool SkipAlreadyTrimmed { get; set; } = true;

        /// <summary>
        /// Do not preserve original files (space saver mode)
        /// When enabled, original files are trimmed directly and replaced
        /// When disabled, original files are moved to PreservedOriginals folder
        /// </summary>
        public bool DoNotPreserveOriginals { get; set; } = false;

        /// <summary>
        /// Path to FFmpeg executable
        /// </summary>
        public string FFmpegPath { get; set; }
    }

    /// <summary>
    /// Status for individual game downloads in batch operations
    /// </summary>
    public enum BatchDownloadStatus
    {
        Pending,
        Downloading,
        Completed,
        Failed,
        Skipped,
        Cancelled
    }

    /// <summary>
    /// Individual game download item for batch progress tracking
    /// </summary>
    public class BatchDownloadItem : System.ComponentModel.INotifyPropertyChanged
    {
        private string _gameName;
        private BatchDownloadStatus _status;
        private string _statusMessage;
        private string _albumName;
        private string _sourceName;

        public string GameName
        {
            get => _gameName;
            set { _gameName = value; OnPropertyChanged(nameof(GameName)); }
        }

        public BatchDownloadStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(StatusIcon)); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        public string AlbumName
        {
            get => _albumName;
            set { _albumName = value; OnPropertyChanged(nameof(AlbumName)); }
        }

        public string SourceName
        {
            get => _sourceName;
            set { _sourceName = value; OnPropertyChanged(nameof(SourceName)); }
        }

        public string StatusColor
        {
            get
            {
                switch (Status)
                {
                    case BatchDownloadStatus.Pending: return "#757575";
                    case BatchDownloadStatus.Downloading: return "#2196F3";
                    case BatchDownloadStatus.Completed: return "#4CAF50";
                    case BatchDownloadStatus.Failed: return "#F44336";
                    case BatchDownloadStatus.Skipped: return "#FF9800";
                    case BatchDownloadStatus.Cancelled: return "#9E9E9E";
                    default: return "#757575";
                }
            }
        }

        public string StatusIcon
        {
            get
            {
                switch (Status)
                {
                    case BatchDownloadStatus.Pending: return "Clock";
                    case BatchDownloadStatus.Downloading: return "Download";
                    case BatchDownloadStatus.Completed: return "Check";
                    case BatchDownloadStatus.Failed: return "Close";
                    case BatchDownloadStatus.Skipped: return "ArrowRightBold";
                    case BatchDownloadStatus.Cancelled: return "Cancel";
                    default: return "Clock";
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Overall progress for batch download operations
    /// </summary>
    public class BatchDownloadProgress
    {
        public int TotalGames { get; set; }
        public int CompletedCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public int InProgressCount { get; set; }
        public bool IsComplete { get; set; }
        public string CurrentStatus { get; set; }
    }
}