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

        public string Type { get; set; }
        public string Artist { get; set; }
        public uint? Count { get; set; }
        public string Year { get; set; }
        public IEnumerable<string> Platforms { get; set; }
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

