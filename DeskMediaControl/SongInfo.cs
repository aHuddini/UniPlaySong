using System;

namespace UniPlaySong.Services
{
    /// <summary>
    /// Represents song metadata for display.
    /// </summary>
    public class SongInfo
    {
        public static readonly SongInfo Empty = new SongInfo(null, null, null, TimeSpan.Zero);

        public string FilePath { get; }
        public string Title { get; }
        public string Artist { get; }
        public TimeSpan Duration { get; }

        public bool IsEmpty => string.IsNullOrEmpty(FilePath);
        public bool HasArtist => !string.IsNullOrWhiteSpace(Artist);
        public bool HasDuration => Duration.TotalSeconds > 0;

        /// <summary>
        /// Gets the formatted display text for the song.
        /// Format: "Title - Artist  3:45" or "Title  3:45" or just "Title"
        /// </summary>
        public string DisplayText => DeskMediaControl.SongTitleCleaner.FormatDisplayText(Title, Artist, Duration);

        /// <summary>
        /// Gets the duration formatted as m:ss or h:mm:ss.
        /// </summary>
        public string DurationText => DeskMediaControl.SongTitleCleaner.FormatDuration(Duration);

        public SongInfo(string filePath, string title, string artist, TimeSpan duration)
        {
            FilePath = filePath;
            Title = title ?? string.Empty;
            Artist = artist;
            Duration = duration;
        }
    }
}
