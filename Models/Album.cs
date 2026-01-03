using System.Collections.Generic;

namespace UniPlaySong.Models
{
    /// <summary>
    /// Represents a collection of songs (soundtrack/album)
    /// </summary>
    public class Album : DownloadItem
    {
        /// <summary>
        /// Sentinel value indicating user pressed Back button to return to source selection.
        /// Check using Album.IsBackSignal() method.
        /// </summary>
        public static readonly Album BackSignal = new Album { Id = "__BACK_SIGNAL__", Name = "__BACK_SIGNAL__" };

        /// <summary>
        /// Checks if this album instance is the BackSignal sentinel.
        /// </summary>
        public static bool IsBackSignal(Album album) => album != null && album.Id == "__BACK_SIGNAL__";

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

