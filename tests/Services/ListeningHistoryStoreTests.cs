using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UniPlaySong.Models;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // The store accumulates totals and survives being reloaded - and survives being found corrupt,
    // which matters more: it is read during plugin startup, so a throw here takes UniPlaySong down
    // with it rather than just losing a statistic.
    [TestFixture]
    public class ListeningHistoryStoreTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ups_hist_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static ListeningSpan Span(string path, double seconds, bool play,
                                          string gameId = null, string gameName = null)
        {
            return new ListeningSpan
            {
                Path = path,
                Title = Path.GetFileNameWithoutExtension(path),
                Seconds = seconds,
                CountsAsPlay = play,
                GameId = gameId,
                GameName = gameName
            };
        }

        private string HistoryFile => Path.Combine(_dir, "listening-history.json");

        [Test]
        public void TotalsAccumulateAcrossSpans()
        {
            var store = new ListeningHistoryStore(_dir, enabled: true);

            store.Record(Span("a.mp3", 60, true, "g1", "Game One"));
            store.Record(Span("a.mp3", 30, true, "g1", "Game One"));
            store.Record(Span("b.mp3", 10, false, "g2", "Game Two"));

            var summary = ListeningInsights.Summarize(store.Snapshot());
            Assert.AreEqual(100, summary.TotalTime.TotalSeconds);
            Assert.AreEqual(2, summary.TotalPlays, "the non-play span must not raise the play count");
            Assert.AreEqual(2, summary.DistinctTracks);
            Assert.AreEqual("a", summary.TopTrack.Title);
            Assert.AreEqual(2, summary.TopTrack.PlayCount);
        }

        [Test]
        public void GamesAreRankedByTimeListenedNotPlayCount()
        {
            var store = new ListeningHistoryStore(_dir, enabled: true);

            // Short game played often, long game played once. Time wins - the card says "by time".
            store.Record(Span("a.mp3", 10, true, "g1", "Often"));
            store.Record(Span("a.mp3", 10, true, "g1", "Often"));
            store.Record(Span("a.mp3", 10, true, "g1", "Often"));
            store.Record(Span("b.mp3", 600, true, "g2", "Once"));

            var top = ListeningInsights.Summarize(store.Snapshot()).TopGames.First();
            Assert.AreEqual("Once", top.GameName);
        }

        [Test]
        public void RoundTripsThroughDisk()
        {
            var store = new ListeningHistoryStore(_dir, enabled: true);
            store.Record(Span("a.mp3", 90, true, "g1", "Game One"));
            store.Flush();

            Assert.IsTrue(File.Exists(HistoryFile), "nothing was written");

            var reloaded = new ListeningHistoryStore(_dir, enabled: true);
            var summary = ListeningInsights.Summarize(reloaded.Snapshot());

            Assert.AreEqual(90, summary.TotalTime.TotalSeconds);
            Assert.AreEqual(1, summary.TotalPlays);
            Assert.AreEqual("Game One", summary.TopGames.First().GameName);
        }

        // The one that protects startup. A half-written file must not throw out of the constructor.
        [Test]
        public void TruncatedFileStartsFreshRatherThanThrowing()
        {
            var store = new ListeningHistoryStore(_dir, enabled: true);
            store.Record(Span("a.mp3", 90, true, "g1", "Game One"));
            store.Flush();

            var json = File.ReadAllText(HistoryFile);
            File.WriteAllText(HistoryFile, json.Substring(0, json.Length / 2));

            ListeningHistoryStore reloaded = null;
            Assert.DoesNotThrow(() => reloaded = new ListeningHistoryStore(_dir, enabled: true),
                "a corrupt history file must never take plugin startup down");
            Assert.AreEqual(0, ListeningInsights.Summarize(reloaded.Snapshot()).DistinctTracks);
        }

        // A version bump discards rather than migrates. Losing a play count is not worth carrying
        // migration code for years.
        [Test]
        public void FileFromAnOlderFormatIsDiscarded()
        {
            File.WriteAllText(HistoryFile,
                "{\"version\":\"0.1\",\"total_seconds\":500,\"total_plays\":9,\"tracks\":{},\"games\":{}}");

            var store = new ListeningHistoryStore(_dir, enabled: true);
            Assert.AreEqual(0, ListeningInsights.Summarize(store.Snapshot()).TotalTime.TotalSeconds);
        }

        [Test]
        public void ClearRemovesTheFileAndTheTotals()
        {
            var store = new ListeningHistoryStore(_dir, enabled: true);
            store.Record(Span("a.mp3", 90, true, "g1", "Game One"));
            store.Flush();

            store.Clear();

            Assert.IsFalse(File.Exists(HistoryFile), "the file outlived the clear");
            Assert.AreEqual(0, ListeningInsights.Summarize(store.Snapshot()).DistinctTracks);
        }

        // A game switch can load and close a track without ever sounding it. That is a loading
        // artefact, not a listen - but the floor is deliberately low enough that a track genuinely
        // heard for a fraction of a second still counts.
        [Test]
        public void NegligibleSpansAreIgnored()
        {
            var store = new ListeningHistoryStore(_dir, enabled: true);
            store.Record(Span("a.mp3", 0.004, false, "g1", "Game One"));

            Assert.AreEqual(0, ListeningInsights.Summarize(store.Snapshot()).DistinctTracks);
        }

        [Test]
        public void BriefButRealListeningIsStillRecorded()
        {
            var store = new ListeningHistoryStore(_dir, enabled: true);
            store.Record(Span("a.mp3", 0.3, false, "g1", "Game One"));

            Assert.AreEqual(1, ListeningInsights.Summarize(store.Snapshot()).DistinctTracks,
                "a third of a second is short, but it is listening that happened");
        }

        [Test]
        public void DisabledStoreRecordsNothing()
        {
            var store = new ListeningHistoryStore(_dir, enabled: false);
            store.Record(Span("a.mp3", 90, true, "g1", "Game One"));

            Assert.AreEqual(0, ListeningInsights.Summarize(store.Snapshot()).DistinctTracks);
        }

        // A track renamed on disk should stop showing its old name.
        [Test]
        public void TitleIsRefreshedOnEveryRecord()
        {
            var store = new ListeningHistoryStore(_dir, enabled: true);

            var first = Span("a.mp3", 60, true);
            first.Title = "Old Name";
            store.Record(first);

            var second = Span("a.mp3", 60, true);
            second.Title = "New Name";
            store.Record(second);

            Assert.AreEqual("New Name", ListeningInsights.Summarize(store.Snapshot()).TopTrack.Title);
        }
    }
}
