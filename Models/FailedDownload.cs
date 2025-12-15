using Playnite.SDK.Models;
using System;

namespace UniPlaySong.Models
{
    /// <summary>
    /// Represents a failed download attempt that can be retried manually
    /// </summary>
    public class FailedDownload
    {
        /// <summary>
        /// The game for which the download failed
        /// </summary>
        public Game Game { get; set; }

        /// <summary>
        /// Reason for failure (e.g., "No albums found", "Download error")
        /// </summary>
        public string FailureReason { get; set; }

        /// <summary>
        /// Timestamp of the failure
        /// </summary>
        public DateTime FailedAt { get; set; }

        /// <summary>
        /// Whether this failed download has been resolved/retried
        /// </summary>
        public bool Resolved { get; set; }

        public FailedDownload()
        {
            FailedAt = DateTime.Now;
            Resolved = false;
        }
    }
}
