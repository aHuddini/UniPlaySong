namespace UniPlaySong.Services.Spotify
{
    // Immutable snapshot of Spotify's current track (metadata only — no audio).
    public struct SpotifyNowPlaying
    {
        public static readonly SpotifyNowPlaying Empty = new SpotifyNowPlaying(null, null);

        public string Title { get; }
        public string Artist { get; }

        public SpotifyNowPlaying(string title, string artist)
        {
            Title = title;
            Artist = artist;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Title);
    }
}
