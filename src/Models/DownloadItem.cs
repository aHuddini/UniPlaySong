namespace UniPlaySong.Models
{
    // Base class for downloadable items (songs, albums)
    public abstract class DownloadItem
    {
        public string Name { get; set; }

        // Unique identifier for the item (URL path, video ID, etc.)
        public string Id { get; set; }

        public string IconUrl { get; set; }

        public Source Source { get; set; }

        public override string ToString() => Name ?? base.ToString();
    }
}

