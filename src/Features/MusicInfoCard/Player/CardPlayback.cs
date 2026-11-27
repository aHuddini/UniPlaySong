using System;
using System.Collections.Generic;
using System.Linq;

namespace UniPlaySong.Features.MusicInfoCard.Player
{
    // What the player card shows and starts, without WPF
    internal static class CardPlayback
    {
        // The row of the song UPS is playing, -1 when it isn't one of this game's (Spotify, another game, nothing)
        public static int PlayingIndex(IReadOnlyList<string> songPaths, string playingPath)
        {
            if (songPaths == null || string.IsNullOrEmpty(playingPath)) return -1;
            for (int i = 0; i < songPaths.Count; i++)
                if (IsPlayingRow(songPaths[i], playingPath)) return i;
            return -1;
        }

        // A multi-track .hes file's rows share its path, so all of them match while it plays
        public static bool IsPlayingRow(string rowPath, string playingPath)
            => !string.IsNullOrEmpty(rowPath) && string.Equals(rowPath, playingPath, StringComparison.OrdinalIgnoreCase);

        // Play with nothing playing: the game's primary song if it's still in the list, else its first song
        public static string StartSong(IReadOnlyList<string> songPaths, string primaryPath)
        {
            if (songPaths == null || songPaths.Count == 0) return null;
            if (!string.IsNullOrEmpty(primaryPath) && songPaths.Any(p => string.Equals(p, primaryPath, StringComparison.OrdinalIgnoreCase)))
                return primaryPath;
            return songPaths[0];
        }

        public static string NowPlayingLine(bool hasMedia, bool isSpotify, string title, string artist, string position, string duration)
        {
            if (!hasMedia) return "Nothing playing · press Play for this game's music";
            var name = string.IsNullOrEmpty(artist) ? title : $"{title} — {artist}";
            var time = string.IsNullOrEmpty(duration) ? string.Empty : $" · {position} / {duration}";
            return (isSpotify ? "Spotify · " : string.Empty) + name + time;
        }
    }
}
