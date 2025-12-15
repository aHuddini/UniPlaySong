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
        public int FailureCount { get; set; }
        public System.Collections.Generic.List<string> FailedFiles { get; set; } = new System.Collections.Generic.List<string>();
        public bool IsComplete { get; set; }
    }
}