using System;
using System.Collections.Generic;
using System.Linq;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    // Everything the listening data can be asked, and nothing about how it is stored.
    //
    // Split out of ListeningHistoryStore, which was doing both. The store's job is to keep records
    // safely - locking, atomic writes, surviving a corrupt file at startup. Deciding what a "top
    // game" is has nothing to do with any of that, and leaving the two together meant every new
    // metric would land in the class that must not break.
    //
    // Every method here is a pure function of a snapshot. That is the whole point: a new metric is
    // a new method, it needs no change to storage, and it is testable without a temp directory or
    // a single byte of disk IO.
    public static class ListeningInsights
    {
        public static TimeSpan TotalTime(ListeningSnapshot snapshot)
        {
            return snapshot == null
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(snapshot.TotalSeconds);
        }

        public static int TotalPlays(ListeningSnapshot snapshot)
        {
            return snapshot?.TotalPlays ?? 0;
        }

        public static int DistinctTracks(ListeningSnapshot snapshot)
        {
            return snapshot?.Tracks?.Count ?? 0;
        }

        public static int DistinctGames(ListeningSnapshot snapshot)
        {
            return snapshot?.Games?.Count ?? 0;
        }

        // Ranked by time listened, not by play count.
        //
        // They disagree more often than not - a short loop played forty times is not the track you
        // spent the evening with - and time is the figure the cards are labelled with.
        public static IReadOnlyList<TrackListeningRecord> TopTracks(ListeningSnapshot snapshot, int count)
        {
            if (snapshot?.Tracks == null || count < 1)
            {
                return new List<TrackListeningRecord>();
            }

            return snapshot.Tracks
                .OrderByDescending(t => t.TotalSeconds)
                .Take(count)
                .ToList();
        }

        public static IReadOnlyList<GameListeningRecord> TopGames(ListeningSnapshot snapshot, int count)
        {
            if (snapshot?.Games == null || count < 1)
            {
                return new List<GameListeningRecord>();
            }

            return snapshot.Games
                .OrderByDescending(g => g.TotalSeconds)
                .Take(count)
                .ToList();
        }

        public static TrackListeningRecord MostPlayedTrack(ListeningSnapshot snapshot)
        {
            return TopTracks(snapshot, 1).FirstOrDefault();
        }

        public static GameListeningRecord ForGame(ListeningSnapshot snapshot, string gameId)
        {
            if (snapshot?.Games == null || string.IsNullOrEmpty(gameId))
            {
                return null;
            }

            return snapshot.Games.FirstOrDefault(
                g => string.Equals(g.GameId, gameId, StringComparison.OrdinalIgnoreCase));
        }

        // The bundle both statistics surfaces happen to want today. A convenience over the
        // functions above, not a replacement for them - a surface that wants one figure should ask
        // for that figure rather than building a summary to read one field off it.
        public static ListeningSummary Summarize(ListeningSnapshot snapshot, int topGameCount = 5)
        {
            return new ListeningSummary
            {
                TotalTime = TotalTime(snapshot),
                TotalPlays = TotalPlays(snapshot),
                DistinctTracks = DistinctTracks(snapshot),
                TopTrack = MostPlayedTrack(snapshot),
                TopGames = TopGames(snapshot, topGameCount)
            };
        }
    }
}
