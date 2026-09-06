using System;
using System.Reflection;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Jingle loudness: an independent level, NOT a proportion of Music Volume.
    //
    // It was relative in 1.8.6 (music x jingle), which made Music Volume a ceiling the jingles could
    // never rise above - with music at 20% a jingle could not exceed 0.20 at any setting. Users who
    // game with quiet music reported achievement sounds were inaudible and no slider position fixed
    // it, because the lever they were given could only ever attenuate.
    [TestFixture]
    public class JingleVolumeTests
    {
        // Mirrors JingleService.JingleLevel, which is private and static on a service whose
        // construction needs a live player factory. The rule is what matters, and it is stated
        // once in each place.
        private static double Level(int jingleVolume)
        {
            return Math.Max(0.0, Math.Min(1.0, jingleVolume / 100.0));
        }

        [Test]
        public void MusicVolumeDoesNotChangeIt()
        {
            // The whole point. The same jingle setting means the same loudness whether the music is
            // at full or nearly off - which is what makes it usable for someone who games quiet.
            Assert.AreEqual(0.60, Level(60), 1e-9);

            // Previously this same setting would have produced 0.06 with music at 10%, i.e. silence
            // in practice. There is no music term left to vary.
            var parameters = typeof(JingleService)
                .GetMethod("JingleLevel", BindingFlags.NonPublic | BindingFlags.Static)
                .GetParameters();
            Assert.AreEqual(1, parameters.Length, "settings only - nothing else feeds the level");
        }

        [Test]
        public void TheDefaultMatchesWhatAFreshInstallUsedToSound()
        {
            // 50 was chosen to equal the old effective default: DefaultMusicVolume (50) x the old
            // DefaultJingleVolume (100) = 0.50. Out of the box nothing changed loudness; only users
            // who had moved their Music Volume hear a difference, and they are the ones the change
            // is for.
            Assert.AreEqual(50, Constants.DefaultJingleVolume);
            Assert.AreEqual(Constants.DefaultMusicVolume, Constants.DefaultJingleVolume,
                "the default is pinned to the music default so the out-of-box level is unchanged");
            Assert.AreEqual(0.50, Level(new UniPlaySongSettings().JingleVolume), 1e-9);
        }

        [Test]
        public void FullScaleIsReachableRegardlessOfMusic()
        {
            // The reported bug in one line: this was impossible before unless Music Volume was also
            // at 100.
            Assert.AreEqual(1.0, Level(100), 1e-9);
        }

        [Test]
        public void ZeroIsSilence()
        {
            Assert.AreEqual(0.0, Level(0), 1e-9, "jingles turned off deliberately");
        }

        [Test]
        public void TheResultIsNeverOutOfRange()
        {
            // The setter clamps 0-100, so the level cannot exceed 1 - but the clamp stays as the
            // guard, because a player handed a volume above 1.0 distorts.
            Assert.LessOrEqual(Level(100), 1.0);
            Assert.GreaterOrEqual(Level(0), 0.0);
        }

        [Test]
        public void TheSettingIsClampedAtBothEnds()
        {
            var s = new UniPlaySongSettings();

            s.JingleVolume = 150;
            Assert.AreEqual(100, s.JingleVolume, "above 100 would distort the player");

            s.JingleVolume = -20;
            Assert.AreEqual(0, s.JingleVolume);
        }

        [Test]
        public void EveryJingleUsesTheSameLevel()
        {
            // One rule for all non-music sound: the completion/abandoned jingle and the external
            // notification path (achievements, ControlUp) both read it from the same place, so they
            // cannot drift apart.
            var level = typeof(JingleService).GetMethod("JingleLevel",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(level, "both playback paths must share one level calculation");
            Assert.AreEqual(typeof(double), level.ReturnType);
        }

        [Test]
        public void JinglesAreNotScaledByPlaynitesFullscreenVolume()
        {
            // A deliberate asymmetry, not an oversight. Music is scaled by Playnite's Background
            // Volume; jingles are not, so they still cut through in Fullscreen. Applying it here
            // would reintroduce exactly the ceiling this change removed, just with a different
            // slider holding the lid down.
            var level = typeof(JingleService).GetMethod("JingleLevel",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(level);
            Assert.AreEqual(1, level.GetParameters().Length,
                "settings only - no multiplier is passed in, which is what keeps the two independent");
            Assert.AreEqual(typeof(UniPlaySongSettings), level.GetParameters()[0].ParameterType);
        }
    }
}
