using Playnite.SDK.Models;
using System;

namespace UniPlaySong.Models
{
    /// <summary>
    /// Represents a failed download attempt that can be retried manually
    /// </summary>
    public class FailedDownload
    {
        public Game Game { get; set; }

        /// <summary>
        /// Reason for failure (e.g., "No albums found", "Download error")
        /// </summary>
        public string FailureReason { get; set; }

        public DateTime FailedAt { get; set; }

        public bool Resolved { get; set; }

        public FailedDownload()
        {
            FailedAt = DateTime.Now;
            Resolved = false;
        }
    }
}
