using System;

namespace UniPlaySong.Models
{
    /// <summary>
    /// Represents a single music track/song
    /// </summary>
    public class Song : DownloadItem
    {
        public string Description { get; set; }
        public string SizeInMb { get; set; }
        public TimeSpan? Length { get; set; }
        public string LocalPath { get; set; }
    }
}

