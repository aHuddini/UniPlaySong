using NUnit.Framework;
using UniPlaySong.Controls;

namespace UniPlaySong.Tests.Controls
{
    // The disc's gesture maths: which way is forward, how far a turn seeks, and the ±180 seam.
    [TestFixture]
    public class SeekDiscMathTests
    {
        // Screen convention (y down): right -> down is a quarter turn clockwise, which is forward.
        [Test]
        public void ClockwiseQuarterTurn_IsForward()
        {
            double from = SeekDiscMath.AngleDegrees(1, 0);
            double to = SeekDiscMath.AngleDegrees(0, 1);
            Assert.AreEqual(90, SeekDiscMath.WrapDegrees(to - from), 1e-9);
        }

        [Test]
        public void AntiClockwiseQuarterTurn_IsBack()
        {
            double from = SeekDiscMath.AngleDegrees(1, 0);
            double to = SeekDiscMath.AngleDegrees(0, -1);
            Assert.AreEqual(-90, SeekDiscMath.WrapDegrees(to - from), 1e-9);
        }

        // Crossing the seam on the left is a tiny turn, not nearly a full one: 170 -> -170 is 20 degrees clockwise.
        [Test]
        public void CrossingTheSeam_IsASmallTurn()
        {
            Assert.AreEqual(20, SeekDiscMath.WrapDegrees(-170 - 170), 1e-9);
            Assert.AreEqual(-20, SeekDiscMath.WrapDegrees(170 - -170), 1e-9);
        }

        [TestCase(360, 30)]
        [TestCase(90, 7.5)]
        [TestCase(-180, -15)]
        public void Turning_SeeksInProportion(double degrees, double seconds)
        {
            Assert.AreEqual(seconds, SeekDiscMath.SecondsForDegrees(degrees), 1e-9);
        }
    }
}
