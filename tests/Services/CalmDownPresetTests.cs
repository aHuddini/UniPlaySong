using System.IO;
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
        // A cutoff near zero outputs silence, and above the audible top the filter does nothing. The volume
        // softens but never mutes (that is pause) and never goes above 1.0 (louder than normal). The transition
        // is never the instant cut the S-curve exists to avoid, nor endless.
        [TestCase("cutoff", 0f, 200f)]
        [TestCase("cutoff", 50000f, 8000f)]
        [TestCase("volume", 0f, 0.05f)]
        [TestCase("volume", 5f, 1f)]
        [TestCase("transition", 0f, 0.1f)]
        [TestCase("transition", 600f, 10f)]
        public void EachValueIsClampedToAUsefulRange(string value, float assigned, float expected)
        {
            var s = new UniPlaySongSettings();
            switch (value)
            {
                case "cutoff":
                    s.CalmDownLowPassCutoffHz = assigned;
                    Assert.AreEqual(expected, s.CalmDownLowPassCutoffHz, 1e-3);
                    break;
                case "volume":
                    s.CalmDownVolumeMultiplier = assigned;
                    Assert.AreEqual(expected, s.CalmDownVolumeMultiplier, 1e-3);
                    break;
                default:
                    s.CalmDownTransitionDurationSeconds = assigned;
                    Assert.AreEqual(expected, s.CalmDownTransitionDurationSeconds, 1e-3);
                    break;
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
