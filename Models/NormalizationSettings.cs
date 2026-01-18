namespace UniPlaySong.Models
{
    /// <summary>Audio normalization settings (EBU R128)</summary>
    public class NormalizationSettings
    {
        /// <summary>Target loudness in LUFS (default: -16)</summary>
        public double TargetLoudness { get; set; } = -16.0;

        /// <summary>True peak limit in dBTP (default: -1.5)</summary>
        public double TruePeak { get; set; } = -1.5;

        /// <summary>Loudness range in LU (default: 11)</summary>
        public double LoudnessRange { get; set; } = 11.0;

        /// <summary>Audio codec (default: libmp3lame)</summary>
        public string AudioCodec { get; set; } = "libmp3lame";

        /// <summary>Suffix for normalized files</summary>
        public string NormalizationSuffix { get; set; } = "-normalized";

        /// <summary>Suffix for trimmed files</summary>
        public string TrimSuffix { get; set; } = "-trimmed";

        public bool SkipAlreadyNormalized { get; set; } = true;

        /// <summary>When true, replaces originals instead of preserving them</summary>
        public bool DoNotPreserveOriginals { get; set; } = false;

        public string FFmpegPath { get; set; }
    }

    /// <summary>FFmpeg loudnorm filter measurements</summary>
    public class LoudnormMeasurements
    {
        public double MeasuredI { get; set; }
        public double MeasuredTP { get; set; }
        public double MeasuredLRA { get; set; }
        public double MeasuredThreshold { get; set; }
        public double Offset { get; set; }
    }

    /// <summary>Normalization progress tracking</summary>
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

    /// <summary>Normalization operation result</summary>
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

    /// <summary>Audio silence trimming settings</summary>
    public class TrimSettings
    {
        /// <summary>Silence threshold in dB (default: -50)</summary>
        public double SilenceThreshold { get; set; } = -50.0;

        /// <summary>Min silence duration to detect in seconds (default: 0.1)</summary>
        public double SilenceDuration { get; set; } = 0.1;

        /// <summary>Min silence to trim in seconds; skip if shorter (default: 0.5)</summary>
        public double MinSilenceToTrim { get; set; } = 0.5;

        /// <summary>Buffer after silence to avoid clicks (default: 0.15s)</summary>
        public double TrimBuffer { get; set; } = 0.15;

        public string TrimSuffix { get; set; } = "-trimmed";
        public bool SkipAlreadyTrimmed { get; set; } = true;

        /// <summary>When true, replaces originals instead of preserving them</summary>
        public bool DoNotPreserveOriginals { get; set; } = false;

        public string FFmpegPath { get; set; }
    }

    /// <summary>Batch download status</summary>
    public enum BatchDownloadStatus
    {
        Pending, Downloading, Completed, Failed, Skipped, Cancelled
    }

    /// <summary>Individual game download item for batch progress</summary>
    public class BatchDownloadItem : System.ComponentModel.INotifyPropertyChanged
    {
        private string _gameName;
        private BatchDownloadStatus _status;
        private string _statusMessage;
        private string _albumName;
        private string _sourceName;
        private string _libraryName;
        private Playnite.SDK.Models.Game _game;

        public Playnite.SDK.Models.Game Game
        {
            get => _game;
            set { _game = value; OnPropertyChanged(nameof(Game)); }
        }

        public string GameName
        {
            get => _gameName;
            set { _gameName = value; OnPropertyChanged(nameof(GameName)); }
        }

        public string LibraryName
        {
            get => _libraryName;
            set { _libraryName = value; OnPropertyChanged(nameof(LibraryName)); }
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

        private bool _wasRedownloaded;
        public bool WasRedownloaded
        {
            get => _wasRedownloaded;
            set
            {
                _wasRedownloaded = value;
                OnPropertyChanged(nameof(WasRedownloaded));
                OnPropertyChanged(nameof(StatusMessageColor));
                OnPropertyChanged(nameof(StatusMessageFontWeight));
            }
        }

        private bool _hadSongsAdded;
        public bool HadSongsAdded
        {
            get => _hadSongsAdded;
            set
            {
                _hadSongsAdded = value;
                OnPropertyChanged(nameof(HadSongsAdded));
                OnPropertyChanged(nameof(StatusMessageColor));
                OnPropertyChanged(nameof(StatusMessageFontWeight));
            }
        }

        public string StatusMessageColor =>
            WasRedownloaded ? "#FF9800" : HadSongsAdded ? "#CE93D8" : "Gray";

        public string StatusMessageFontWeight => (WasRedownloaded || HadSongsAdded) ? "Bold" : "Normal";

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
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    /// <summary>Batch download progress tracking</summary>
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