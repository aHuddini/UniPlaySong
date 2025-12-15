namespace UniPlaySong.Models
{
    /// <summary>
    /// Base class for downloadable items (songs, albums)
    /// </summary>
    public abstract class DownloadItem
    {
        /// <summary>
        /// Display name of the item
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Unique identifier for the item (URL path, video ID, etc.)
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Icon/thumbnail URL
        /// </summary>
        public string IconUrl { get; set; }

        /// <summary>
        /// Source where this item comes from
        /// </summary>
        public Source Source { get; set; }

        public override string ToString() => Name ?? base.ToString();
    }
}

