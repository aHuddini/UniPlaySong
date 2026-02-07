using Playnite.SDK.Models;
using System;

namespace UniPlaySong.Models
{
    // Represents a failed download attempt that can be retried manually
    public class FailedDownload
    {
        public Game Game { get; set; }

        // Reason for failure (e.g., "No albums found", "Download error")
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
