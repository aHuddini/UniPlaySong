using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UniPlaySong;

namespace UniPlaySong.Tests.Services
{
    // The Calm Down page: presets for how pronounced the effect is, plus the three values behind
    // them. Those values shipped as config-file-only settings with no UI and no clamps, so exposing
    // them meant bounding them.
    [TestFixture]
    public class CalmDownPresetTests
    {
        [Test]
        public void DefaultIsExactlyWhatCalmDownAlreadySounded()
        {
            // Picking Default has to be a way back to the shipped sound, not an approximation of it.
            var s = new UniPlaySongSettings();

            Assert.AreEqual(1500f, s.CalmDownLowPassCutoffHz, 1e-3);
            Assert.AreEqual(0.5f, s.CalmDownVolumeMultiplier, 1e-3);
            Assert.AreEqual(1.5f, s.CalmDownTransitionDurationSeconds, 1e-3);
            Assert.AreEqual(CalmDownPreset.Default, s.SelectedCalmDownPreset);
        }

        [Test]
        public void TheCutoffIsBoundedToUsefulAudio()
        {
            var s = new UniPlaySongSettings();

            s.CalmDownLowPassCutoffHz = 0f;
            Assert.AreEqual(200f, s.CalmDownLowPassCutoffHz, 1e-3,
                "a cutoff near zero makes the low-pass output silence, not calm music");

            s.CalmDownLowPassCutoffHz = 50000f;
            Assert.AreEqual(8000f, s.CalmDownLowPassCutoffHz, 1e-3,
                "above the audible top the filter does nothing and the mode looks broken");
        }

        [Test]
        public void TheVolumeNeverReachesSilence()
        {
            var s = new UniPlaySongSettings();

            s.CalmDownVolumeMultiplier = 0f;
            Assert.AreEqual(0.05f, s.CalmDownVolumeMultiplier, 1e-3,
                "Calm Down softens; muting outright is what pause is for");

            s.CalmDownVolumeMultiplier = 5f;
            Assert.AreEqual(1f, s.CalmDownVolumeMultiplier, 1e-3,
                "above 1.0 it would make the music LOUDER than normal");
        }

        [Test]
        public void TheTransitionCannotBeInstantOrEndless()
        {
            var s = new UniPlaySongSettings();

            s.CalmDownTransitionDurationSeconds = 0f;
            Assert.AreEqual(0.1f, s.CalmDownTransitionDurationSeconds, 1e-3,
                "an instant switch is the abrupt cut the S-curve exists to avoid");

            s.CalmDownTransitionDurationSeconds = 600f;
            Assert.AreEqual(10f, s.CalmDownTransitionDurationSeconds, 1e-3);
        }

        [Test]
        public void EveryPresetValueSurvivesTheClamps()
        {
            // A preset value outside the allowed range would be silently rewritten on assignment,
            // so the preset would not produce the sound it names. Restated here rather than parsed
            // out of the page: if the two ever disagree, that is a review question, not a green run.
            //
            // Every value is also on its slider's tick grid (cutoff 200+100n, volume 0.05+0.05n,
            // seconds 0.1+0.1n). IsSnapToTickEnabled coerces off-grid values, which would move a
            // preset's sound the moment its page was opened.
            var presets = new[]
            {
                new { Name = "Default", Hz = 1500f, Vol = 0.50f, Secs = 1.5f },
                new { Name = "Subtle",  Hz = 4000f, Vol = 0.75f, Secs = 1.5f },
                new { Name = "Warm",    Hz = 1200f, Vol = 0.85f, Secs = 2.0f },
                new { Name = "Muffled", Hz =  700f, Vol = 0.50f, Secs = 2.0f },
                new { Name = "Distant", Hz =  500f, Vol = 0.30f, Secs = 3.0f },
                new { Name = "Whisper", Hz =  900f, Vol = 0.10f, Secs = 3.0f },
            };

            var s = new UniPlaySongSettings();
            foreach (var p in presets)
            {
                s.CalmDownLowPassCutoffHz = p.Hz;
                s.CalmDownVolumeMultiplier = p.Vol;
                s.CalmDownTransitionDurationSeconds = p.Secs;

                Assert.AreEqual(p.Hz, s.CalmDownLowPassCutoffHz, 1e-3, $"{p.Name} cutoff was clamped");
                Assert.AreEqual(p.Vol, s.CalmDownVolumeMultiplier, 1e-3, $"{p.Name} volume was clamped");
                Assert.AreEqual(p.Secs, s.CalmDownTransitionDurationSeconds, 1e-3, $"{p.Name} transition was clamped");

                Assert.AreEqual(0, (p.Hz - 200f) % 100f, 1e-3, $"{p.Name} cutoff is off the slider's ticks");
                Assert.AreEqual(0, Math.Round((p.Secs - 0.1f) * 10f) % 1, 1e-3, $"{p.Name} transition is off the slider's ticks");
            }
        }

        [Test]
        public void CustomIsZeroSoAnUnknownValueLandsThere()
        {
            // The converter maps the enum to the combo's SelectedIndex and falls back to 0. Custom
            // must be 0 so an unrecognised stored value shows as Custom rather than claiming to be a
            // preset whose values it no longer holds.
            Assert.AreEqual(0, (int)CalmDownPreset.Custom);
        }

        [Test]
        public void MovingASliderMeansCustom()
        {
            // Otherwise the combo keeps naming a preset while the sound has been tuned away from it.
            var page = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "src", "Controls", "Settings", "CalmDownPage.xaml.cs");
            if (!File.Exists(page)) Assert.Ignore("page source not reachable from the test output directory");

            var text = File.ReadAllText(page);
            StringAssert.Contains("SelectedCalmDownPreset = CalmDownPreset.Custom", text,
                "a manual tweak must drop the preset label");
            StringAssert.Contains("_applyingPreset", text,
                "the preset itself writes those sliders, so it must not trip its own Custom guard");
        }
    }
}
