using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace UniPlaySong.Models
{
    // What has actually been listened to, as opposed to what is on disk.
    //
    // Aggregates rather than an event log. A log of every play would grow with how long the plugin
    // has been installed; this grows only with how much of the library has actually been heard, and
    // it answers every question the statistics pages ask without a reduce step over history.
    //
    // Property names are snake_case on the wire and PascalCase in code, matching search_cache.json.

    // One track's running totals.
    public class TrackListeningRecord
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        // Cached so the statistics page can name a track whose file has since been deleted or
        // renamed. Re-deriving it from Path would print nothing for exactly those cases.
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("play_count")]
        public int PlayCount { get; set; }

        [JsonProperty("total_seconds")]
        public double TotalSeconds { get; set; }

        [JsonProperty("last_played_utc")]
        public DateTime LastPlayedUtc { get; set; }
    }

    // One game's running totals, summed across every track in its folder.
    public class GameListeningRecord
    {
        [JsonProperty("game_id")]
        public string GameId { get; set; }

        [JsonProperty("game_name")]
        public string GameName { get; set; }

        [JsonProperty("play_count")]
        public int PlayCount { get; set; }

        [JsonProperty("total_seconds")]
        public double TotalSeconds { get; set; }

        [JsonProperty("last_played_utc")]
        public DateTime LastPlayedUtc { get; set; }
    }

    // The root document.
    public class ListeningHistoryData
    {
        // Bumped when the shape changes. A mismatch discards rather than migrates - the data is a
        // convenience, and losing a play count is not worth carrying migration code for years.
        public const string CurrentVersion = "1.0";

        [JsonProperty("version")]
        public string Version { get; set; }

        // Every second of audible playback, including tracks too short to count as a play.
        [JsonProperty("total_seconds")]
        public double TotalSeconds { get; set; }

        [JsonProperty("total_plays")]
        public int TotalPlays { get; set; }

        [JsonProperty("tracks")]
        public Dictionary<string, TrackListeningRecord> Tracks { get; set; }

        [JsonProperty("games")]
        public Dictionary<string, GameListeningRecord> Games { get; set; }

        public ListeningHistoryData()
        {
            Version = CurrentVersion;
            Tracks = new Dictionary<string, TrackListeningRecord>(StringComparer.OrdinalIgnoreCase);
            Games = new Dictionary<string, GameListeningRecord>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // One finished stretch of listening, handed from the tracker to the store.
    //
    // CountsAsPlay is decided by the tracker, not here: the rule needs to know whether the track
    // ended naturally, which only the playback service can say.
    public class ListeningSpan
    {
        public string Path { get; set; }
        public string Title { get; set; }
        public string GameId { get; set; }
        public string GameName { get; set; }
        public double Seconds { get; set; }
        public bool CountsAsPlay { get; set; }
    }

    // The raw records, copied out of the store so nothing downstream holds its lock or mutates it.
    //
    // This is the seam between storage and analysis. The store knows how to keep records; it does
    // not know what a "top game" is. Anything that computes a metric takes one of these and is a
    // pure function over it - no disk, no lock, testable without a temp directory.
    public class ListeningSnapshot
    {
        public double TotalSeconds { get; set; }
        public int TotalPlays { get; set; }
        public IReadOnlyList<TrackListeningRecord> Tracks { get; set; }
        public IReadOnlyList<GameListeningRecord> Games { get; set; }

        public ListeningSnapshot()
        {
            Tracks = new List<TrackListeningRecord>();
            Games = new List<GameListeningRecord>();
        }
    }

    // A read-only snapshot for the statistics surfaces.
    public class ListeningSummary
    {
        public TimeSpan TotalTime { get; set; }
        public int TotalPlays { get; set; }
        public int DistinctTracks { get; set; }
        public TrackListeningRecord TopTrack { get; set; }
        public IReadOnlyList<GameListeningRecord> TopGames { get; set; }

        public ListeningSummary()
        {
            TopGames = new List<GameListeningRecord>();
        }
    }
}
