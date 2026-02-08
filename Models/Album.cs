using System.Collections.Generic;

namespace UniPlaySong.Models
{
    // Represents a collection of songs (soundtrack/album)
    public class Album : DownloadItem
    {
        // Sentinel value indicating user pressed Back button to return to source selection.
        // Check using Album.IsBackSignal() method.
        public static readonly Album BackSignal = new Album { Id = "__BACK_SIGNAL__", Name = "__BACK_SIGNAL__" };

        // Checks if this album instance is the BackSignal sentinel.
        public static bool IsBackSignal(Album album) => album != null && album.Id == "__BACK_SIGNAL__";

        public string Type { get; set; }
        public string Artist { get; set; }
        public uint? Count { get; set; }
        public string Year { get; set; }
        public IEnumerable<string> Platforms { get; set; }
        public IEnumerable<Song> Songs { get; set; }

        // YouTube Channel ID (if source is YouTube) - used for channel whitelisting to prefer reliable soundtrack sources
        public string ChannelId { get; set; }

        // YouTube Channel Name (if source is YouTube) - human-readable channel name for display purposes
        public string ChannelName { get; set; }
    }
}

