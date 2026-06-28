using System;

namespace UniPlaySong.Services.Spotify
{
    // Immutable snapshot of Spotify's current track (metadata only — no audio).
    public struct SpotifyNowPlaying
    {
        public static readonly SpotifyNowPlaying Empty = new SpotifyNowPlaying(null, null);

        public string Title { get; }
        public string Artist { get; }
        public string Album { get; }
        // Comma-joined genre list (SMTC exposes genres as a list); empty when none.
        public string Genre { get; }
        // Total track length from the SMTC timeline; TimeSpan.Zero when unavailable.
        public TimeSpan Duration { get; }

        public SpotifyNowPlaying(string title, string artist)
            : this(title, artist, null, null, TimeSpan.Zero)
        {
        }

        public SpotifyNowPlaying(string title, string artist, string album, string genre, TimeSpan duration)
        {
            Title = title;
            Artist = artist;
            Album = album;
            Genre = genre;
            Duration = duration;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Title);
    }
}
