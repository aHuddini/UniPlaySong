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
        public void ShouldRemember_MidTrackWithFeatureOn_Remembers()
        {
            Assert.IsTrue(GameMusicResumePolicy.ShouldRemember(true, Song, Thirty, FourMinutes));
        }

        [Test]
        public void ShouldRemember_FeatureOff_DoesNot()
        {
            Assert.IsFalse(GameMusicResumePolicy.ShouldRemember(false, Song, Thirty, FourMinutes));
        }

        [Test]
        public void ShouldRemember_AtTheVeryStart_DoesNot()
        {
            // Nothing to resume to, and a mark here would only cost a pointless seek.
            Assert.IsFalse(GameMusicResumePolicy.ShouldRemember(true, Song, TimeSpan.Zero, FourMinutes));
        }

        [Test]
        public void ShouldRemember_NearTheEnd_DoesNot()
        {
            // Resuming with under MinRemainingSeconds left plays a stub then auto-advances,
            // which reads as a bug rather than as resume.
            var nearEnd = FourMinutes - TimeSpan.FromSeconds(2);
            Assert.IsFalse(GameMusicResumePolicy.ShouldRemember(true, Song, nearEnd, FourMinutes));
        }

        [Test]
        public void ShouldRemember_ExactlyAtTheRemainingThreshold_Remembers()
        {
            var atThreshold = FourMinutes - TimeSpan.FromSeconds(GameMusicResumePolicy.MinRemainingSeconds);
            Assert.IsTrue(GameMusicResumePolicy.ShouldRemember(true, Song, atThreshold, FourMinutes));
        }

        [Test]
        public void ShouldRemember_Chiptune_DoesNot()
        {
            // NAudio's Play(TimeSpan) seeks synchronously on the calling thread and a GME seek
            // takes hundreds of ms to seconds, so chiptune must always start from the beginning.
            Assert.IsFalse(GameMusicResumePolicy.ShouldRemember(true, Chiptune, Thirty, FourMinutes));
        }

        [Test]
        public void ShouldRemember_UnknownTotalTime_StillRemembers()
        {
            // A backend that cannot report length should not disable the feature outright.
            Assert.IsTrue(GameMusicResumePolicy.ShouldRemember(true, Song, Thirty, null));
        }

        [Test]
        public void ShouldRemember_NoSongPath_DoesNot()
        {
            Assert.IsFalse(GameMusicResumePolicy.ShouldRemember(true, null, Thirty, FourMinutes));
            Assert.IsFalse(GameMusicResumePolicy.ShouldRemember(true, "", Thirty, FourMinutes));
        }

        // --- ResumeFrom ---

        [Test]
        public void ResumeFrom_SameTrack_ReturnsTheRememberedPosition()
        {
            Assert.AreEqual(Thirty, GameMusicResumePolicy.ResumeFrom(true, Song, MarkAt(Song, Thirty)));
        }

        [Test]
        public void ResumeFrom_DifferentTrackForTheSameGame_StartsOver()
        {
            // Randomization can hand back a different track next visit. Dropping song A's
            // position into song B is worse than starting from the beginning.
            Assert.AreEqual(TimeSpan.Zero, GameMusicResumePolicy.ResumeFrom(true, OtherSong, MarkAt(Song, Thirty)));
        }

        [Test]
        public void ResumeFrom_PathCasingDiffers_StillResumes()
        {
            var shouty = Song.ToUpperInvariant();
            Assert.AreEqual(Thirty, GameMusicResumePolicy.ResumeFrom(true, shouty, MarkAt(Song, Thirty)));
        }

        [Test]
        public void ResumeFrom_FeatureOff_StartsOver()
        {
            Assert.AreEqual(TimeSpan.Zero, GameMusicResumePolicy.ResumeFrom(false, Song, MarkAt(Song, Thirty)));
        }

        [Test]
        public void ResumeFrom_NoMarkForThisGame_StartsOver()
        {
            // A default(Mark) is what Dictionary.TryGetValue leaves behind on a miss.
            Assert.AreEqual(TimeSpan.Zero, GameMusicResumePolicy.ResumeFrom(true, Song, default(GameMusicResumePolicy.Mark)));
        }

        [Test]
        public void ResumeFrom_MarkAtZero_StartsOver()
        {
            Assert.AreEqual(TimeSpan.Zero, GameMusicResumePolicy.ResumeFrom(true, Song, MarkAt(Song, TimeSpan.Zero)));
        }

        [Test]
        public void ResumeFrom_NoIncomingSong_StartsOver()
        {
            Assert.AreEqual(TimeSpan.Zero, GameMusicResumePolicy.ResumeFrom(true, null, MarkAt(Song, Thirty)));
        }

        // --- the round trip the feature actually promises ---

        [Test]
        public void LeaveGameAThirtySecondsIn_ReturnToGameA_ResumesAtThirty()
        {
            Assert.IsTrue(GameMusicResumePolicy.ShouldRemember(true, Song, Thirty, FourMinutes),
                "leaving mid-track should record a mark");

            var resumed = GameMusicResumePolicy.ResumeFrom(true, Song, MarkAt(Song, Thirty));
            Assert.AreEqual(Thirty, resumed, "returning to the same track should pick up where it left off");
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

        [Test]
        public void ShuffleLandingOnAnUnheardTrack_StartsFromTheBeginning()
        {
            // No mark exists for a track never left part-way through, which is what
            // Dictionary.TryGetValue yields on a miss. Shuffle's fresh picks start at zero.
            Assert.AreEqual(TimeSpan.Zero,
                GameMusicResumePolicy.ResumeFrom(true, OtherSong, default(GameMusicResumePolicy.Mark)));
        }

        [Test]
        public void AMarkNeverLeaksOntoADifferentTrack()
        {
            // The store is keyed by path so a mismatch should be unreachable, but ResumeFrom
            // still refuses it - a lookup bug must not transplant one track's position onto another.
            Assert.AreEqual(TimeSpan.Zero, GameMusicResumePolicy.ResumeFrom(true, OtherSong, MarkAt(Song, Thirty)));
        }

        [Test]
        public void ResumeOff_LeavesSwitchModeExactlyAsItWas()
        {
            // With resume off nothing is remembered and nothing is honoured, so shuffle behaves
            // precisely as it did before this feature existed.
            Assert.IsFalse(GameMusicResumePolicy.ShouldRemember(false, Song, Thirty, FourMinutes));
            Assert.AreEqual(TimeSpan.Zero, GameMusicResumePolicy.ResumeFrom(false, Song, MarkAt(Song, Thirty)));
        }

        [Test]
        public void TrackNeverLeftMidSong_RecordsNothing()
        {
            // Only tracks left part-way through are worth a mark. A first play, or one left in
            // the last few seconds, records nothing at all.
            var nearEnd = FourMinutes - TimeSpan.FromSeconds(2);
            Assert.IsFalse(GameMusicResumePolicy.ShouldRemember(true, Song, nearEnd, FourMinutes));
            Assert.IsFalse(GameMusicResumePolicy.ShouldRemember(true, Song, TimeSpan.Zero, FourMinutes));
        }
    }
}
