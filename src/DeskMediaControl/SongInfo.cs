using System;

namespace UniPlaySong.Services
{
    // Represents song metadata for display
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

        // Formatted display text: "Title - Artist" or just "Title"
        public string DisplayText => DeskMediaControl.SongTitleCleaner.FormatDisplayText(Title, Artist, Duration);

        // Duration formatted as m:ss or h:mm:ss
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
