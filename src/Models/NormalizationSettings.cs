namespace UniPlaySong.Models
{
    // Audio normalization settings (EBU R128)
    public class NormalizationSettings
    {
        public double TargetLoudness { get; set; } = -16.0;     // LUFS
        public double TruePeak { get; set; } = -1.5;            // dBTP
        public double LoudnessRange { get; set; } = 11.0;       // LU
        public string AudioCodec { get; set; } = "libmp3lame";
        public string NormalizationSuffix { get; set; } = "-normalized";
        public string TrimSuffix { get; set; } = "-trimmed";
        public bool SkipAlreadyNormalized { get; set; } = true;
        // When true, replaces originals instead of preserving them
        public bool DoNotPreserveOriginals { get; set; } = false;
        public string FFmpegPath { get; set; }
    }

    public class LoudnormMeasurements
    {
        public double MeasuredI { get; set; }
        public double MeasuredTP { get; set; }
        public double MeasuredLRA { get; set; }
        public double MeasuredThreshold { get; set; }
        public double Offset { get; set; }
    }

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

    // Audio silence trimming settings
    public class TrimSettings
    {
        public double SilenceThreshold { get; set; } = -50.0;   // dB
        public double SilenceDuration { get; set; } = 0.1;      // seconds; min duration to detect
        public double MinSilenceToTrim { get; set; } = 0.5;     // seconds; skip if shorter
        public double TrimBuffer { get; set; } = 0.15;          // seconds after silence to avoid clicks
        public string TrimSuffix { get; set; } = "-trimmed";
        public bool SkipAlreadyTrimmed { get; set; } = true;
        // When true, replaces originals instead of preserving them
        public bool DoNotPreserveOriginals { get; set; } = false;
        public string FFmpegPath { get; set; }
    }

    public enum BatchDownloadStatus
    {
        Pending, Downloading, Completed, Failed, Skipped, Cancelled
    }

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