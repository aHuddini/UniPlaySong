using System;
using System.Reflection;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Jingle loudness: Music Volume scaled by JingleVolume.
    //
    // Reported as achievement sounds being too quiet to hear over a game for one person and too
    // loud for another, with identical settings — because the only lever was Music Volume, which
    // moves the music with it, and because nothing normalizes the sound files themselves.
    [TestFixture]
    public class JingleVolumeTests
    {
        // Mirrors JingleService.JingleLevel, which is private and static on a service whose
        // construction needs a live player factory. The rule is what matters, and it is stated
        // once in each place.
        private static double Level(int musicVolume, int jingleVolume)
        {
            double music = musicVolume / 100.0;
            double jingle = jingleVolume / 100.0;
            return Math.Max(0.0, Math.Min(1.0, music * jingle));
        }

        [Test]
        public void TheDefaultChangesNothing()
        {
            // 100 must be exactly the old behaviour, or every existing install gets quieter on
            // upgrade — a silent regression dressed up as a feature.
            Assert.AreEqual(0.50, Level(50, 100), 1e-9);
            Assert.AreEqual(1.00, Level(100, 100), 1e-9);
            Assert.AreEqual(0.70, Level(70, 100), 1e-9);
        }

        [Test]
        public void LoweringItBringsJinglesDownWithoutTouchingMusic()
        {
            // The whole point: the music stays where the user put it.
            Assert.AreEqual(0.35, Level(70, 50), 1e-9);
            Assert.AreEqual(0.07, Level(70, 10), 1e-9);
        }

        [Test]
        public void ZeroOnEitherSideIsSilence()
        {
            Assert.AreEqual(0.0, Level(0, 100), 1e-9, "no music volume, no jingle");
            Assert.AreEqual(0.0, Level(70, 0), 1e-9, "jingles turned off deliberately");
        }

        [Test]
        public void TheResultIsNeverOutOfRange()
        {
            // Both are clamped 0-100 by their setters, so the product cannot exceed 1 — but the
            // clamp stays as the guard, because a player handed a volume above 1.0 distorts.
            Assert.LessOrEqual(Level(100, 100), 1.0);
            Assert.GreaterOrEqual(Level(0, 0), 0.0);
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
        public void TheDefaultIsFullVolume()
        {
            Assert.AreEqual(100, new UniPlaySongSettings().JingleVolume);
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
            // would have made every existing Fullscreen user's jingles quieter on upgrade, on top
            // of whatever they set here.
            var level = typeof(JingleService).GetMethod("JingleLevel",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(level);
            Assert.AreEqual(1, level.GetParameters().Length,
                "settings only — no multiplier is passed in, which is what keeps the two independent");
            Assert.AreEqual(typeof(UniPlaySongSettings), level.GetParameters()[0].ParameterType);
        }
    }
}
