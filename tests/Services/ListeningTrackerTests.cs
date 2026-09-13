using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // How long a track was AUDIBLE, which is not how long it was loaded.
    //
    // The tempting implementations are both wrong and both were available:
    //
    //   DateTime.Now - _songStartTime counts paused time, because _songStartTime is never
    //   re-marked on resume. Pause for an hour and it reports an hour of listening.
    //
    //   IMusicPlayer.CurrentTime is worse - reading it on a chiptune track during an in-flight
    //   gme_seek blocks the calling thread for seconds, and the caller here is the UI thread.
    //
    // So the tracker accumulates from the pause and resume transitions instead. These tests exist
    // to fail if anyone ever "simplifies" it back.
    [TestFixture]
    public class ListeningTrackerTests
    {
        private string _dir;
        private ListeningHistoryStore _store;
        private ListeningTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ups_listen_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _store = new ListeningHistoryStore(_dir, enabled: true);
            _tracker = new ListeningTracker(_store);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        // The core claim. A track paused in the middle must not bank the paused stretch.
        //
        // Asserted against the wall clock this test actually observed, not against hard-coded
        // seconds. A fixed threshold here is one tuned to whatever the machine was doing that day:
        // the first version compared against 0.65s and failed once under build load, which proves
        // nothing about the code and costs the next person an afternoon.
        [Test]
        public void PausedTimeIsNotCountedAsListening()
        {
            var wall = Stopwatch.StartNew();

            _tracker.Begin("C:\\music\\a.mp3", "game-1", "Game One");
            Thread.Sleep(200);

            _tracker.Suspend();
            var pauseStarted = wall.Elapsed;
            Thread.Sleep(500);        // the stretch that must NOT be counted
            var paused = wall.Elapsed - pauseStarted;
            _tracker.Resume();

            Thread.Sleep(200);
            _tracker.Abandon();
            wall.Stop();

            var recorded = ListeningInsights.Summarize(_store.Snapshot()).TotalTime.TotalSeconds;

            // Counting paused time puts the figure at the full wall clock; excluding it puts the
            // figure a whole pause below that. Half a pause apart separates the two however slowly
            // the sleeps actually ran.
            Assert.Less(recorded, wall.Elapsed.TotalSeconds - (paused.TotalSeconds * 0.5),
                "paused time is being counted - this is DateTime.Now - _songStartTime behaviour");
            Assert.Greater(recorded, 0.05, "audible time was not accumulated at all");
        }

        // Pause sources stack. Only the first is a real transition, and the service already gates
        // on that - but a double Suspend must not corrupt the clock either way.
        [Test]
        public void RepeatedSuspendAndResumeAreIdempotent()
        {
            // Started BEFORE Begin, so the clock covers every second the tracker could have
            // counted. Starting it later left 150ms of audible time inside `recorded` that `wall`
            // never saw, which cut the margin to about 50ms and made this flake roughly one run
            // in three - a threshold that close is measuring the machine, not the code.
            var wall = Stopwatch.StartNew();

            _tracker.Begin("C:\\music\\a.mp3", null, null);
            Thread.Sleep(150);

            _tracker.Suspend();
            _tracker.Suspend();
            var pauseStarted = wall.Elapsed;
            Thread.Sleep(400);
            var paused = wall.Elapsed - pauseStarted;
            _tracker.Resume();
            _tracker.Resume();

            Thread.Sleep(150);
            _tracker.Abandon();
            wall.Stop();

            var recorded = ListeningInsights.Summarize(_store.Snapshot()).TotalTime.TotalSeconds;
            Assert.Less(recorded, wall.Elapsed.TotalSeconds - (paused.TotalSeconds * 0.5),
                "a repeated Suspend or Resume let paused time leak in");
            Assert.Greater(recorded, 0.05, "audible time was lost");
        }

        // A short skip is listening time but not a play. Otherwise flicking through fifty tracks
        // invents fifty plays.
        [Test]
        public void ShortSkipCountsTimeButNotAPlay()
        {
            _tracker.Begin("C:\\music\\a.mp3", "game-1", "Game One");
            Thread.Sleep(120);
            _tracker.Abandon();

            var summary = ListeningInsights.Summarize(_store.Snapshot());
            Assert.AreEqual(0, summary.TotalPlays, "a fraction of a second should not be a play");
            Assert.Greater(summary.TotalTime.TotalSeconds, 0, "the time listened was thrown away");
        }

        // ...but a SHORT TRACK HEARD IN FULL is a play. This is why Complete() exists separately
        // from Abandon(): a twelve-second chiptune loop that ended on its own is a real play, and
        // no duration threshold alone can tell it apart from a twelve-second skip.
        [Test]
        public void ShortTrackThatEndsNaturallyCountsAsAPlay()
        {
            _tracker.Begin("C:\\music\\loop.nsf", "game-1", "Game One");
            Thread.Sleep(120);
            _tracker.Complete();

            Assert.AreEqual(1, ListeningInsights.Summarize(_store.Snapshot()).TotalPlays,
                "a track that reached its own end is a play however short it is");
        }

        // Begin banks whatever was in flight. The playback service relies on this - MarkSongStart
        // is called on every track change and nothing calls a separate flush.
        [Test]
        public void BeginBanksThePreviousTrack()
        {
            _tracker.Begin("C:\\music\\a.mp3", "game-1", "Game One");
            Thread.Sleep(120);
            _tracker.Begin("C:\\music\\b.mp3", "game-1", "Game One");
            Thread.Sleep(120);
            _tracker.Abandon();

            Assert.AreEqual(2, ListeningInsights.Summarize(_store.Snapshot()).DistinctTracks,
                "the first track was never banked, so a track change loses it");
        }

        // Default and radio music has no game. Attributing it to whichever game was under the
        // cursor would invent a figure rather than record one.
        [Test]
        public void TrackWithNoGameDoesNotCreateAGameRecord()
        {
            _tracker.Begin("C:\\music\\default.mp3", null, null);
            Thread.Sleep(120);
            _tracker.Abandon();

            var summary = ListeningInsights.Summarize(_store.Snapshot());
            Assert.Greater(summary.TotalTime.TotalSeconds, 0, "the listening time was lost");
            Assert.IsEmpty(summary.TopGames, "default music was attributed to a game");
        }

        // Turning the setting off stops recording at once, not at the next restart.
        [Test]
        public void DisabledStoreRecordsNothing()
        {
            _store.Enabled = false;

            _tracker.Begin("C:\\music\\a.mp3", "game-1", "Game One");
            Thread.Sleep(120);
            _tracker.Abandon();

            Assert.AreEqual(0, ListeningInsights.Summarize(_store.Snapshot()).DistinctTracks);
        }
    }
}
