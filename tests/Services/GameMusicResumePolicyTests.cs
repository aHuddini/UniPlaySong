using System;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Pins the rules that decide whether returning to a game picks up where it left off.
    // The interesting cases are all refusals: the feature is only correct when it declines
    // in every situation where a remembered position would be wrong.
    [TestFixture]
    public class GameMusicResumePolicyTests
    {
        private const string Song = @"C:\music\GameA\track.mp3";
        private const string OtherSong = @"C:\music\GameA\other.mp3";
        private const string Chiptune = @"C:\music\GameA\track.nsf";

        private static readonly TimeSpan Thirty = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan FourMinutes = TimeSpan.FromMinutes(4);

        private static GameMusicResumePolicy.Mark MarkAt(string path, TimeSpan position)
            => new GameMusicResumePolicy.Mark { SongPath = path, Position = position };

        // --- ShouldRemember ---

        [Test]
        public void ShouldRemember_Table()
        {
            var nearEnd = FourMinutes - TimeSpan.FromSeconds(2);
            var atThreshold = FourMinutes - TimeSpan.FromSeconds(GameMusicResumePolicy.MinRemainingSeconds);
            var cases = new[]
            {
                new { Name = "mid-track, feature on", On = true, Path = Song, Pos = Thirty, Total = (TimeSpan?)FourMinutes, Expected = true },
                // With resume off nothing is remembered, so Switch Mode shuffle behaves precisely as
                // it did before this feature existed.
                new { Name = "feature off", On = false, Path = Song, Pos = Thirty, Total = (TimeSpan?)FourMinutes, Expected = false },
                // Only tracks left part-way through are worth a mark. At the very start there is
                // nothing to resume to, and a mark would only cost a pointless seek.
                new { Name = "at the very start", On = true, Path = Song, Pos = TimeSpan.Zero, Total = (TimeSpan?)FourMinutes, Expected = false },
                // Resuming with under MinRemainingSeconds left plays a stub then auto-advances,
                // which reads as a bug rather than as resume.
                new { Name = "near the end", On = true, Path = Song, Pos = nearEnd, Total = (TimeSpan?)FourMinutes, Expected = false },
                new { Name = "exactly at the remaining threshold", On = true, Path = Song, Pos = atThreshold, Total = (TimeSpan?)FourMinutes, Expected = true },
                // A backend that cannot report length should not disable the feature outright.
                new { Name = "unknown total time", On = true, Path = Song, Pos = Thirty, Total = (TimeSpan?)null, Expected = true },
                new { Name = "null song path", On = true, Path = (string)null, Pos = Thirty, Total = (TimeSpan?)FourMinutes, Expected = false },
                new { Name = "empty song path", On = true, Path = "", Pos = Thirty, Total = (TimeSpan?)FourMinutes, Expected = false },
            };
            Assert.Multiple(() =>
            {
                foreach (var c in cases)
                    Assert.AreEqual(c.Expected, GameMusicResumePolicy.ShouldRemember(c.On, c.Path, c.Pos, c.Total), c.Name);
            });
        }

        [Test]
        public void ShouldRemember_Chiptune_DoesNot()
        {
            // NAudio's Play(TimeSpan) seeks synchronously on the calling thread and a GME seek
            // takes hundreds of ms to seconds, so chiptune must always start from the beginning.
            Assert.IsFalse(GameMusicResumePolicy.ShouldRemember(true, Chiptune, Thirty, FourMinutes));
        }

        [Test]
        public void ShouldRemember_Psf_DoesNot()
        {
            // A PSF has no position to seek to at all - reaching one means re-running the driver
            // from zero - so PsfReader only honours a seek to zero and resuming would be a lie.
            Assert.IsFalse(GameMusicResumePolicy.ShouldRemember(true, @"C:\music\GameA\103 Run Straight.psf", Thirty, FourMinutes));
            Assert.IsFalse(GameMusicResumePolicy.ShouldRemember(true, @"C:\music\GameA\ff7-101.minipsf", Thirty, FourMinutes));
        }

        // --- ResumeFrom ---

        [Test]
        public void ResumeFrom_Table()
        {
            var cases = new[]
            {
                new { Name = "same track", On = true, Path = Song, Mark = MarkAt(Song, Thirty), Expected = Thirty },
                // Randomization can hand back a different track next visit. Dropping song A's
                // position into song B is worse than starting from the beginning. The store is keyed
                // by path so a mismatch should be unreachable, but ResumeFrom still refuses it - a
                // lookup bug must not transplant one track's position onto another.
                new { Name = "different track for the same game", On = true, Path = OtherSong, Mark = MarkAt(Song, Thirty), Expected = TimeSpan.Zero },
                new { Name = "path casing differs", On = true, Path = Song.ToUpperInvariant(), Mark = MarkAt(Song, Thirty), Expected = Thirty },
                // With resume off nothing is honoured either.
                new { Name = "feature off", On = false, Path = Song, Mark = MarkAt(Song, Thirty), Expected = TimeSpan.Zero },
                // A default(Mark) is what Dictionary.TryGetValue leaves behind on a miss - e.g. a
                // track never left part-way through. Shuffle's fresh picks start at zero.
                new { Name = "no mark for this track", On = true, Path = Song, Mark = default(GameMusicResumePolicy.Mark), Expected = TimeSpan.Zero },
                new { Name = "mark at zero", On = true, Path = Song, Mark = MarkAt(Song, TimeSpan.Zero), Expected = TimeSpan.Zero },
                new { Name = "no incoming song", On = true, Path = (string)null, Mark = MarkAt(Song, Thirty), Expected = TimeSpan.Zero },
            };
            Assert.Multiple(() =>
            {
                foreach (var c in cases)
                    Assert.AreEqual(c.Expected, GameMusicResumePolicy.ResumeFrom(c.On, c.Path, c.Mark), c.Name);
            });
        }

        // --- composing with Switch Mode (RandomizeOnEverySelect) ---
        //
        // Marks are keyed by SONG, not by game, and these pin why. An earlier design keyed them
        // by game and pinned one track per game so the position could apply, which stopped
        // Switch Mode shuffling at all - both settings on looked like shuffle was broken.
        // Per-song marks let shuffle pick freely; whichever track it lands on resumes on its own.

        [Test]
        public void EachTrackKeepsItsOwnPosition()
        {
            // The property that makes shuffle and resume compose: two tracks, two independent
            // positions, neither inheriting the other's.
            var songMark = MarkAt(Song, Thirty);
            var otherMark = MarkAt(OtherSong, TimeSpan.FromSeconds(90));

            Assert.AreEqual(Thirty, GameMusicResumePolicy.ResumeFrom(true, Song, songMark));
            Assert.AreEqual(TimeSpan.FromSeconds(90), GameMusicResumePolicy.ResumeFrom(true, OtherSong, otherMark));
        }
    }
}
