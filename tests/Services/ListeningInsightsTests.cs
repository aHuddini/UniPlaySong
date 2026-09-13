using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // The metrics, tested without touching a disk.
    //
    // That is the point of splitting them out of the store: every method here is a pure function
    // of a snapshot, so a new metric needs no temp directory, no file format, and no locking - and
    // a bug in a metric can never be confused with a bug in persistence.
    [TestFixture]
    public class ListeningInsightsTests
    {
        private static ListeningSnapshot Snapshot(
            IEnumerable<TrackListeningRecord> tracks = null,
            IEnumerable<GameListeningRecord> games = null)
        {
            var t = (tracks ?? Enumerable.Empty<TrackListeningRecord>()).ToList();
            var g = (games ?? Enumerable.Empty<GameListeningRecord>()).ToList();

            return new ListeningSnapshot
            {
                Tracks = t,
                Games = g,
                TotalSeconds = t.Sum(x => x.TotalSeconds),
                TotalPlays = t.Sum(x => x.PlayCount)
            };
        }

        private static TrackListeningRecord Track(string title, double seconds, int plays)
        {
            return new TrackListeningRecord
            {
                Path = "C:\\music\\" + title + ".mp3",
                Title = title,
                TotalSeconds = seconds,
                PlayCount = plays
            };
        }

        private static GameListeningRecord Game(string id, string name, double seconds, int plays)
        {
            return new GameListeningRecord
            {
                GameId = id,
                GameName = name,
                TotalSeconds = seconds,
                PlayCount = plays
            };
        }

        // Time, not play count. The two disagree often - a short loop played forty times is not
        // the track you spent the evening with - and the cards are labelled by time.
        [Test]
        public void TopTracksAreRankedByTimeNotPlayCount()
        {
            var snapshot = Snapshot(new[]
            {
                Track("Short Loop", 120, 40),
                Track("Long Suite", 900, 2)
            });

            Assert.AreEqual("Long Suite", ListeningInsights.MostPlayedTrack(snapshot).Title);
        }

        [Test]
        public void TopGamesAreRankedByTimeAndHonourTheLimit()
        {
            var snapshot = Snapshot(games: new[]
            {
                Game("a", "Alpha", 100, 1),
                Game("b", "Beta", 900, 1),
                Game("c", "Gamma", 500, 1)
            });

            var top = ListeningInsights.TopGames(snapshot, 2);

            Assert.AreEqual(2, top.Count, "the count limit was ignored");
            Assert.AreEqual("Beta", top[0].GameName);
            Assert.AreEqual("Gamma", top[1].GameName);
        }

        [Test]
        public void ForGameFindsARecordRegardlessOfIdCasing()
        {
            var snapshot = Snapshot(games: new[] { Game("AbC-123", "Alpha", 100, 1) });

            Assert.IsNotNull(ListeningInsights.ForGame(snapshot, "abc-123"));
            Assert.IsNull(ListeningInsights.ForGame(snapshot, "nope"));
        }

        // Every one of these runs before a single track has been played, on a fresh install. None
        // of them may throw, and none may report something that reads as broken.
        [Test]
        public void EmptySnapshotProducesEmptyAnswersRatherThanThrowing()
        {
            var snapshot = Snapshot();

            Assert.AreEqual(TimeSpan.Zero, ListeningInsights.TotalTime(snapshot));
            Assert.AreEqual(0, ListeningInsights.TotalPlays(snapshot));
            Assert.AreEqual(0, ListeningInsights.DistinctTracks(snapshot));
            Assert.AreEqual(0, ListeningInsights.DistinctGames(snapshot));
            Assert.IsNull(ListeningInsights.MostPlayedTrack(snapshot));
            Assert.IsEmpty(ListeningInsights.TopGames(snapshot, 5));
            Assert.IsEmpty(ListeningInsights.TopTracks(snapshot, 5));
        }

        // The settings dialog can open before services finish initializing, so the store - and
        // therefore the snapshot - can legitimately be null.
        [Test]
        public void NullSnapshotIsSurvivable()
        {
            Assert.AreEqual(TimeSpan.Zero, ListeningInsights.TotalTime(null));
            Assert.AreEqual(0, ListeningInsights.TotalPlays(null));
            Assert.AreEqual(0, ListeningInsights.DistinctTracks(null));
            Assert.IsNull(ListeningInsights.MostPlayedTrack(null));
            Assert.IsEmpty(ListeningInsights.TopGames(null, 5));
            Assert.IsNull(ListeningInsights.ForGame(null, "a"));

            var summary = ListeningInsights.Summarize(null);
            Assert.AreEqual(0, summary.TotalPlays);
            Assert.IsEmpty(summary.TopGames);
        }

        [Test]
        public void NonPositiveCountReturnsNothingRatherThanEverything()
        {
            var snapshot = Snapshot(games: new[] { Game("a", "Alpha", 100, 1) });

            Assert.IsEmpty(ListeningInsights.TopGames(snapshot, 0));
            Assert.IsEmpty(ListeningInsights.TopGames(snapshot, -3));
        }

        [Test]
        public void SummarizeGathersTheFiguresTheCardsShow()
        {
            var snapshot = Snapshot(
                new[] { Track("One", 300, 3), Track("Two", 100, 1) },
                new[] { Game("a", "Alpha", 400, 4) });

            var summary = ListeningInsights.Summarize(snapshot, topGameCount: 5);

            Assert.AreEqual(400, summary.TotalTime.TotalSeconds);
            Assert.AreEqual(4, summary.TotalPlays);
            Assert.AreEqual(2, summary.DistinctTracks);
            Assert.AreEqual("One", summary.TopTrack.Title);
            Assert.AreEqual("Alpha", summary.TopGames.Single().GameName);
        }
    }
}
