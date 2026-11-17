using System.IO;
using System.Reflection;
using NUnit.Framework;
using UniPlaySong;
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
        // The setting alone decides the level: full scale is reachable however quiet the music is, 50 is the
        // out-of-box level (the old 50% music x 100% jingle), and 0 is jingles turned off deliberately.
        [TestCase(0, 0.0)]
        [TestCase(50, 0.50)]
        [TestCase(60, 0.60)]
        [TestCase(100, 1.0)]
        public void JingleLevel_IsTheJingleSettingAlone(int jingleVolume, double expected)
        {
            Assert.AreEqual(expected, JingleService.JingleLevel(new UniPlaySongSettings { JingleVolume = jingleVolume }), 1e-9);
        }

        // Settings only - no Music Volume and no Playnite fullscreen multiplier are passed in, which is what keeps
        // jingles independent of both. Both playback paths (completion/abandoned and external notifications) call it.
        [Test]
        public void JingleLevel_TakesTheSettingsAndNothingElse()
        {
            var level = typeof(JingleService).GetMethod("JingleLevel", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(level, "both playback paths must share one level calculation");
            Assert.AreEqual(typeof(double), level.ReturnType);
            Assert.AreEqual(1, level.GetParameters().Length, "settings only - nothing else feeds the level");
            Assert.AreEqual(typeof(UniPlaySongSettings), level.GetParameters()[0].ParameterType);
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
        public void TheSettingsPreviewUsesTheSameLevelAsTheRealThing()
        {
            // The Preview buttons on the settings pages played at Music Volume while the real sound
            // played at Jingle Volume. Harmless while the two were multiplied together; once 1.8.7
            // decoupled them the preview could be wildly off, which is exactly what someone
            // testing the new slider would notice first. Reported as "the volume slider doesn't
            // apply when clicking Preview, but works when one pops for real".
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "src", "UniPlaySongSettingsViewModel.cs");
            if (!File.Exists(path)) Assert.Ignore("view model source not reachable from the test output directory");

            var text = File.ReadAllText(path);
            StringAssert.Contains("JingleService.JingleLevel(Settings)", text,
                "the preview must read the shared level, not compute its own");
            Assert.IsFalse(text.Contains("_previewVolume = Settings.MusicVolume"),
                "the preview must not fall back to Music Volume");
        }
    }
}
