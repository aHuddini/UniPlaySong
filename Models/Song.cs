using System;

namespace UniPlaySong.Models
{
    /// <summary>
    /// Represents a single music track/song
    /// </summary>
    public class Song : DownloadItem
    {
        /// <summary>
        /// Description or additional information about the song
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// File size in megabytes
        /// </summary>
        public string SizeInMb { get; set; }

        /// <summary>
        /// Duration of the song
        /// </summary>
        public TimeSpan? Length { get; set; }

        /// <summary>
        /// Local file path if downloaded
        /// </summary>
        public string LocalPath { get; set; }
    }
}

