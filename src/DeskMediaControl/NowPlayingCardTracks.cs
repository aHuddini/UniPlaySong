using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UniPlaySong.Services;

namespace UniPlaySong.DeskMediaControl
{
    public enum NowPlayingCardContext { None, GameSongs, RadioUpNext, SpotifyRecent }

    public sealed class NowPlayingCardTrack : INotifyPropertyChanged
    {
        private bool _isCurrent;
        private bool _isLive;
        private string _length = string.Empty;

        public string Title { get; }
        public string Detail { get; }
        public string Path { get; }     // the song file; null for Spotify rows
        public bool Playable { get; }
        public bool IsPrimary { get; }  // the game's primary song (plays first when the game is selected)

        // Changes in place when the next song starts, so the card moves its marker without rebuilding the list
        public bool IsCurrent
        {
            get => _isCurrent;
            internal set
            {
                if (_isCurrent == value) return;
                _isCurrent = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            }
        }

        // The playing row while music plays and the card is open: its bars bounce
        public bool IsLive
        {
            get => _isLive;
            internal set
            {
                if (_isLive == value) return;
                _isLive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLive)));
            }
        }

        // "3:08"; empty until read (lengths are read off the UI thread and fill in)
        public string Length
        {
            get => _length;
            internal set
            {
                if (_length == value) return;
                _length = value ?? string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Length)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        // Same row as another: same song in the same place (the playing flag aside)
        internal bool SameRow(NowPlayingCardTrack other) =>
            other != null && Title == other.Title && Detail == other.Detail && Path == other.Path && Playable == other.Playable
            && IsPrimary == other.IsPrimary;

        public NowPlayingCardTrack(string title, string detail, string path, bool isCurrent, bool playable, bool isPrimary = false)
        {
            IsPrimary = isPrimary;
            Title = title ?? string.Empty;
            Detail = detail ?? string.Empty;
            Path = path;
            _isCurrent = isCurrent;
            Playable = playable;
        }
    }

    // What the Now Playing card lists: always "what you can play from here". Pure, so the rules are tested here and
    // the card only draws rows.
    public static class NowPlayingCardTracks
    {
        public static (NowPlayingCardContext Context, IReadOnlyList<NowPlayingCardTrack> Rows) Build(
            bool spotify, bool radio, bool defaultMusic, string currentPath, string currentTitle,
            IReadOnlyList<string> gameSongs, IReadOnlyList<RecentTrack> upNext, IReadOnlyList<RecentTrack> spotifyRecent,
            string primaryPath = null)
        {
            // Spotify's history: what was heard, not something UniPlaySong can start
            if (spotify)
                return (NowPlayingCardContext.SpotifyRecent,
                    (spotifyRecent ?? new RecentTrack[0]).Select(t => new NowPlayingCardTrack(t.Title, t.Artist, null, false, false)).ToList());

            // Radio: the song that's on, then the queue (which never holds it)
            if (radio)
            {
                var rows = new List<NowPlayingCardTrack> { new NowPlayingCardTrack(currentTitle, null, currentPath, true, false) };
                rows.AddRange((upNext ?? new RecentTrack[0]).Select(t => new NowPlayingCardTrack(t.Title, t.Artist, t.Key, false, true)));
                return (NowPlayingCardContext.RadioUpNext, rows);
            }

            // Default music: its pool can be the whole library, and none of it is this game's to play
            if (defaultMusic)
                return (NowPlayingCardContext.None, new NowPlayingCardTrack[0]);

            return (NowPlayingCardContext.GameSongs, (gameSongs ?? new string[0])
                .Select(p => new NowPlayingCardTrack(System.IO.Path.GetFileNameWithoutExtension(p), null, p,
                    string.Equals(p, currentPath, System.StringComparison.OrdinalIgnoreCase), true,
                    string.Equals(p, primaryPath, System.StringComparison.OrdinalIgnoreCase)))
                .ToList());
        }
    }
}
