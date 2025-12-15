using System.Collections.Generic;

namespace UniPlaySong.Models
{
    /// <summary>
    /// Represents a collection of songs (soundtrack/album)
    /// </summary>
    public class Album : DownloadItem
    {
        /// <summary>
        /// Type of album (OST, Remastered, GameRip, etc.)
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Artist or composer
        /// </summary>
        public string Artist { get; set; }

        /// <summary>
        /// Number of songs in the album
        /// </summary>
        public uint? Count { get; set; }

        /// <summary>
        /// Release year
        /// </summary>
        public string Year { get; set; }

        /// <summary>
        /// Platforms this album is associated with
        /// </summary>
        public IEnumerable<string> Platforms { get; set; }

        /// <summary>
        /// Songs contained in this album
        /// </summary>
        public IEnumerable<Song> Songs { get; set; }

        /// <summary>
        /// YouTube Channel ID (if source is YouTube)
        /// Used for channel whitelisting to prefer reliable soundtrack sources
        /// </summary>
        public string ChannelId { get; set; }

        /// <summary>
        /// YouTube Channel Name (if source is YouTube)
        /// Human-readable channel name for display purposes
        /// </summary>
        public string ChannelName { get; set; }
    }
}

