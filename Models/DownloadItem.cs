namespace UniPlaySong.Models
{
    /// <summary>
    /// Base class for downloadable items (songs, albums)
    /// </summary>
    public abstract class DownloadItem
    {
        public string Name { get; set; }

        /// <summary>
        /// Unique identifier for the item (URL path, video ID, etc.)
        /// </summary>
        public string Id { get; set; }

        public string IconUrl { get; set; }

        public Source Source { get; set; }

        public override string ToString() => Name ?? base.ToString();
    }
}

