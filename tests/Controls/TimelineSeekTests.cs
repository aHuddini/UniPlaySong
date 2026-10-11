using NUnit.Framework;
using UniPlaySong.Controls;

namespace UniPlaySong.Tests.Controls
{
    // Which progress bars become seekable: only ones showing UPS's timeline, never other bars in Playnite.
    [TestFixture]
    public class TimelineSeekTests
    {
        [Test]
        public void ThemeBindingToActiveMediaProgress_IsATimeline()
        {
            Assert.IsTrue(TimelineSeek.IsUpsTimeline("Settings.ActiveMediaProgress", null));
        }

        [Test]
        public void UpsMediaElementProgress_IsATimeline()
        {
            Assert.IsTrue(TimelineSeek.IsUpsTimeline("Progress", new ActiveMediaViewModel(null, null)));
        }

        // A "Progress" on some other data context (a download, an install) is not ours to seek.
        [Test]
        public void OtherProgressBars_AreLeftAlone()
        {
            Assert.IsFalse(TimelineSeek.IsUpsTimeline("Progress", new object()));
            Assert.IsFalse(TimelineSeek.IsUpsTimeline("Settings.ActiveMediaVolume", null));
            Assert.IsFalse(TimelineSeek.IsUpsTimeline(null, null));
        }

        // Right stick tilt: right is forward, left is back, speed grows with the tilt.
        [Test]
        public void Tilt_RightIsForward_LeftIsBack()
        {
            Assert.Greater(TimelineSeekMath.TiltSecondsPerSecond(0.8, 0), 0);
            Assert.Less(TimelineSeekMath.TiltSecondsPerSecond(-0.8, 0), 0);
        }

        [Test]
        public void Tilt_InsideTheDeadZone_DoesNothing()
        {
            Assert.AreEqual(0, TimelineSeekMath.TiltSecondsPerSecond(0.2, 0));
            Assert.AreEqual(0, TimelineSeekMath.TiltSecondsPerSecond(0, 0));
        }

        // Pushing mostly up or down isn't a seek, even when far from the centre.
        [Test]
        public void Tilt_MostlyVertical_DoesNothing()
        {
            Assert.AreEqual(0, TimelineSeekMath.TiltSecondsPerSecond(0.5, 0.9));
        }

        [Test]
        public void Tilt_RangesFromGentleToFull()
        {
            Assert.AreEqual(TimelineSeekMath.MinSpeed, TimelineSeekMath.TiltSecondsPerSecond(TimelineSeekMath.TiltDeadzone, 0), 1e-9);
            Assert.AreEqual(TimelineSeekMath.MaxSpeed, TimelineSeekMath.TiltSecondsPerSecond(1, 0), 1e-9);
            Assert.Less(TimelineSeekMath.TiltSecondsPerSecond(0.6, 0), TimelineSeekMath.TiltSecondsPerSecond(0.9, 0));
        }
    }
}
